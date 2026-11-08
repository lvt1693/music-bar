using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace MusicBar.Services;

public sealed record NotificationItem(uint Id, string AppName, string AppId, ImageSource? Logo, string Title, string Body, DateTimeOffset Time);

/// <summary>
/// The notifications waiting in the Windows notification center, read through
/// UserNotificationListener. Polled (the change event is not available to a
/// desktop app): often while the control center is open, slowly for the badge.
/// </summary>
public sealed class NotificationFeed
{
    static readonly TimeSpan FastPoll = TimeSpan.FromSeconds(2);
    static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(8);

    readonly DispatcherTimer _poll = new() { Interval = SlowPoll };
    readonly Dictionary<string, ImageSource?> _logos = new();
    // What each notification said, read once: they don't change while they wait.
    readonly Dictionary<uint, NotificationItem> _read = new();
    readonly HashSet<uint> _seen = new();
    readonly HashSet<uint> _removed = new();
    UserNotificationListener? _listener;
    bool _refreshing;
    string _signature = "";

    public NotificationFeed()
    {
        _poll.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Windows lets MusicBar read notifications.</summary>
    public bool Available { get; private set; }

    public IReadOnlyList<NotificationItem> Items { get; private set; } = [];

    /// <summary>Notifications that arrived since the control center was last opened.</summary>
    public int Unseen => Items.Count(i => !_seen.Contains(i.Id));

    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = _listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified) access = await _listener.RequestAccessAsync();
            Available = access == UserNotificationListenerAccessStatus.Allowed;
            Log.Info($"Notification access: {access}");
        }
        catch (Exception ex)
        {
            Available = false;
            Log.Error("Notification listener unavailable", ex);
        }
        if (!Available) return;

        // What is already there when MusicBar starts is not news.
        await RefreshAsync();
        foreach (var item in Items) _seen.Add(item.Id);
        Changed?.Invoke();
        _poll.Start();
    }

    public void Stop() => _poll.Stop();

    public void SetFastPolling(bool fast) => _poll.Interval = fast ? FastPoll : SlowPoll;

    public void MarkAllSeen()
    {
        foreach (var item in Items) _seen.Add(item.Id);
        Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        if (_listener == null || !Available || _refreshing) return;
        _refreshing = true;
        try
        {
            // Each notification's text and app is a call into Windows' notification service, about 8 ms apiece:
            // read on a pool thread, and only for the ones not read before. Every 8 seconds on the UI thread, a
            // notification center holding dozens froze the bar and the lyrics for most of a second (#17).
            var listener = _listener;
            var known = _read.Keys.ToHashSet();
            var (ids, fresh) = await Task.Run(async () =>
            {
                var raw = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                var fresh = new List<NotificationItem>();
                foreach (var n in raw.Where(n => !known.Contains(n.Id))) fresh.Add(await ReadAsync(n));
                return (raw.Select(n => n.Id).ToList(), fresh);
            });
            foreach (var item in fresh) _read[item.Id] = item;
            foreach (var gone in _read.Keys.Except(ids).ToList()) _read.Remove(gone);

            // One the user took away but Windows still lists (or a read that started before the clear): try again, keep it hidden.
            _removed.IntersectWith(ids);
            foreach (var id in _removed.ToList()) Remove(id);
            var items = ids.Where(id => !_removed.Contains(id)).Select(id => _read[id]).OrderByDescending(i => i.Time).ToList();

            string signature = string.Join(",", items.Select(i => i.Id));
            Items = items;
            if (signature != _signature)
            {
                _signature = signature;
                _seen.IntersectWith(items.Select(i => i.Id));
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Reading notifications failed", ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dismiss(uint id)
    {
        Remove(id);
        Items = Items.Where(i => i.Id != id).ToList();
        _signature = string.Join(",", Items.Select(i => i.Id));
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes every notification away. ClearNotifications leaves other apps' toasts in place for a
    /// desktop app (they came back a few seconds later), so each one is removed by itself,
    /// including any that arrived since the list was last read.
    /// </summary>
    public async void ClearAll()
    {
        var ids = Items.Select(i => i.Id).ToList();
        Items = [];
        _signature = "";
        Changed?.Invoke();
        try
        {
            if (_listener != null) ids.AddRange((await _listener.GetNotificationsAsync(NotificationKinds.Toast)).Select(n => n.Id));
        }
        catch (Exception ex)
        {
            Log.Error("Reading notifications to clear failed", ex);
        }
        foreach (var id in ids.Distinct()) Remove(id);
    }

    /// <summary>Removes one notification from Windows, and keeps it off the list should Windows still hand it back.</summary>
    void Remove(uint id)
    {
        _removed.Add(id);
        try
        {
            _listener?.RemoveNotification(id);
        }
        catch (Exception ex)
        {
            Log.Error("Removing a notification failed", ex);
        }
    }

    /// <summary>Opens the app that sent the notification and takes the notification away, as Windows does.</summary>
    public void Open(NotificationItem item)
    {
        if (item.AppId.Length > 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.AppId}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"Opening {item.AppId} failed", ex);
            }
        }
        Dismiss(item.Id);
    }

    /// <summary>A notification's text, app and logo (on a pool thread: every part is a call into Windows).</summary>
    async Task<NotificationItem> ReadAsync(UserNotification n)
    {
        string title = "", body = "";
        try
        {
            var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
            title = texts?.FirstOrDefault()?.Text?.Trim() ?? "";
            body = texts == null ? "" : string.Join("\n", texts.Skip(1).Select(t => t.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t)));
        }
        catch (Exception)
        {
        }
        string appName = "", appId = "";
        try
        {
            appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            appId = n.AppInfo?.AppUserModelId ?? "";
        }
        catch (Exception)
        {
        }
        return new NotificationItem(n.Id, appName, appId, await LogoAsync(n), title, body, n.CreationTime);
    }

    // Only touched by the one read running at a time, on its pool thread.
    async Task<ImageSource?> LogoAsync(UserNotification n)
    {
        string key;
        try
        {
            key = n.AppInfo?.AppUserModelId ?? "";
        }
        catch (Exception)
        {
            return null;
        }
        if (_logos.TryGetValue(key, out var cached)) return cached;

        ImageSource? logo = null;
        try
        {
            var reference = n.AppInfo!.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
            using var stream = await reference.OpenReadAsync();
            using var net = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = net;
            bitmap.DecodePixelWidth = 32;
            bitmap.EndInit();
            bitmap.Freeze();
            logo = bitmap;
        }
        catch (Exception)
        {
        }
        _logos[key] = logo;
        return logo;
    }
}
