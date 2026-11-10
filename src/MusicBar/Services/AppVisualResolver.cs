using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Interop;

namespace MusicBar.Services;

public sealed record AppVisual(string Name, BitmapSource? Icon, Color? Color);

/// <summary>Turns a media session's AppUserModelId into a friendly name, icon and brand color.</summary>
public static class AppVisualResolver
{
    // Filled by lookups on the thread pool (ResolveAsync) and on the UI thread (Resolve) alike.
    static readonly ConcurrentDictionary<string, AppVisual> Cache = new(StringComparer.OrdinalIgnoreCase);
    // Held while an app is looked up (see Resolve); an app asked for again meanwhile waits and takes that answer.
    static readonly object Gate = new();

    static readonly (string Key, string Name, string? Hex)[] Brands =
    [
        ("spotify", "Spotify", "#1ED760"),
        ("applemusic", "Apple Music", "#FA2D48"),
        ("zunemusic", "Media Player", "#FF6A3D"),
        ("deezer", "Deezer", "#A238FF"),
        ("soundcloud", "SoundCloud", "#FF5500"),
        ("amazonmusic", "Amazon Music", "#25D1DA"),
        ("youtubemusic", "YouTube Music", "#FF0033"),
        ("tidal", "TIDAL", null),
        ("vlc", "VLC", "#FF8800"),
        ("aimp", "AIMP", "#FFB300"),
        ("foobar2000", "foobar2000", null),
        ("musicbee", "MusicBee", "#F5B400"),
    ];

    /// <summary>The app's name, icon and colour when they were looked up before; nothing is looked up here.</summary>
    public static bool TryGetCached(string appId, [NotNullWhen(true)] out AppVisual? visual)
    {
        if (string.IsNullOrEmpty(appId))
        {
            visual = new AppVisual("", null, null);
            return true;
        }
        return Cache.TryGetValue(appId, out visual);
    }

    /// <summary>
    /// What the app id alone tells, without opening a process or a file: a known player's name and
    /// colour, else the name in the id (as Resolve gives when it finds nothing). Shown while the lookup runs.
    /// </summary>
    public static AppVisual Placeholder(string appId)
        => FindBrand(appId) is { } brand
            ? new AppVisual(brand.Name, null, brand.Color)
            : new AppVisual(Path.GetFileNameWithoutExtension(appId), null, null);

    /// <summary>
    /// Resolve, run on the thread pool. The first lookup of an app lists every process, opens the player's
    /// and reads its icon through the shell: up to some 120 ms on a fast PC, more while the shell starts
    /// after sign-in or a disk wakes up, and on the UI thread that held up the bar at every new player.
    /// An app looked up before comes back at once. Never fails: a lookup that throws gives the
    /// placeholder, and is tried again the next time the app is picked.
    /// </summary>
    public static Task<AppVisual> ResolveAsync(string appId)
    {
        if (TryGetCached(appId, out var cached)) return Task.FromResult(cached);
        return Task.Run(() =>
        {
            try
            {
                return Resolve(appId);
            }
            catch (Exception ex)
            {
                Log.Error($"Looking up the app {appId} failed", ex);
                return Placeholder(appId);
            }
        });
    }

    /// <summary>
    /// The lookup itself, on the caller's thread: kept for lists the user has just asked for (the menu's
    /// players, the settings), which need every name at once.
    /// </summary>
    public static AppVisual Resolve(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return new AppVisual("", null, null);
        if (Cache.TryGetValue(appId, out var cached)) return cached;
        // One lookup at a time, as when they all ran on the UI thread: asked for two icons at once, the
        // shell answers one of them E_PENDING, and that app would go without its icon for the whole run.
        lock (Gate) return Cache.TryGetValue(appId, out cached) ? cached : LookUp(appId);
    }

    static AppVisual LookUp(string appId)
    {
        string? name = null;
        BitmapSource? icon = null;
        bool looksLikeExe = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeExe) (name, icon) = ShellImages.Load(@"shell:AppsFolder\" + appId, 64);

        if ((icon == null || name == null) && FindExecutable(appId) is { } exe)
        {
            var (shellName, exeIcon) = ShellImages.Load(exe, 64);
            icon ??= exeIcon;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                name ??= NonEmpty(info.FileDescription) ?? NonEmpty(info.ProductName);
            }
            catch (Exception)
            {
            }
            name ??= shellName;
        }

        Color? color = null;
        if (FindBrand(appId) is { } brand) (name, color) = (brand.Name, brand.Color);

        if (color == null && icon != null) color = ColorExtractor.FromBitmap(icon)?.Primary;
        name ??= Path.GetFileNameWithoutExtension(appId);

        var visual = new AppVisual(name, icon, color);
        Cache[appId] = visual;
        return visual;
    }

    /// <summary>A player known by its app id: its name, and its colour when it has one of its own.</summary>
    static (string Name, Color? Color)? FindBrand(string appId)
    {
        var key = appId.Replace(" ", "").ToLowerInvariant();
        foreach (var brand in Brands)
        {
            if (!key.Contains(brand.Key)) continue;
            Color? color = brand.Hex != null && ColorUtil.TryParse(brand.Hex, out var bc) ? bc : null;
            return (brand.Name, color);
        }
        return null;
    }

    static string? FindExecutable(string appId)
    {
        var processName = Path.GetFileNameWithoutExtension(appId);
        if (string.IsNullOrWhiteSpace(processName)) return null;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch (Exception)
            {
                // Access denied for protected processes; try the next one.
            }
            finally
            {
                p.Dispose();
            }
        }
        return null;
    }

    static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Brings the player's window to the front (best effort).</summary>
    public static void Activate(string appId)
    {
        try
        {
            var processName = Path.GetFileNameWithoutExtension(appId);
            foreach (var p in Process.GetProcessesByName(processName))
            {
                using (p)
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                    Native.SetForegroundWindow(hwnd);
                    return;
                }
            }
            if (!appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + appId) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Activating player failed", ex);
        }
    }
}
