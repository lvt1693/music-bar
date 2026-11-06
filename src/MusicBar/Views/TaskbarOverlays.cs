using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// MusicBar on the taskbars the user picked, separately for the progress bar and for the player
/// with its lyrics (Settings → General → Screens): the main screen's, every screen's, or one
/// screen's. The main taskbar always has an overlay, since it owns the control center and the
/// menu; it only steps aside when other screens are picked. Secondary taskbars get one each
/// while they are wanted and exist. All of them share one audio meter and one track-info cache.
/// </summary>
public sealed class TaskbarOverlays : IDisposable
{
    readonly AppSettings _s;
    readonly MediaService _media;
    readonly LyricsService _lyrics;
    readonly AudioMeter _audio = new();
    readonly TrackInfoService _trackInfo;
    readonly Dictionary<string, TaskbarOverlay> _secondary = [];
    readonly DispatcherTimer _sync = new() { Interval = TimeSpan.FromSeconds(2) };
    // A moment after waking, unlocking or a change of displays (they come in bursts), everything is drawn anew.
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    string _lastPlan = "";

    public TaskbarOverlays(AppSettings settings, MediaService media, LyricsService lyrics)
    {
        _s = settings;
        _media = media;
        _lyrics = lyrics;
        _trackInfo = new TrackInfoService(settings);
        Main = new TaskbarOverlay(settings, media, lyrics, _audio, _trackInfo, monitorDevice: null);
        _sync.Tick += (_, _) => Sync();
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            Sync();
            Main.Refresh();
            foreach (var overlay in _secondary.Values) overlay.Refresh();
        };
        _s.PropertyChanged += OnSettingChanged;
    }

    /// <summary>The main taskbar's overlay: control center, menu, settings previews.</summary>
    public TaskbarOverlay Main { get; }

    public void Start()
    {
        Main.Start();
        Sync();
        _sync.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) RefreshSoon("woke from sleep");
    }

    void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            or Microsoft.Win32.SessionSwitchReason.RemoteConnect) RefreshSoon("unlocked");
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => RefreshSoon("the displays changed");

    /// <summary>The events come on their own thread, and often several at once: one redraw, a moment later.</summary>
    void RefreshSoon(string why) => _ui.BeginInvoke(() =>
    {
        Log.Info($"Drawing everything anew: {why}");
        _refresh.Stop();
        _refresh.Start();
    });

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.DisplayTarget) or nameof(AppSettings.DisplayMonitor)
            or nameof(AppSettings.PlayerTarget) or nameof(AppSettings.PlayerMonitor)) Sync();
    }

    /// <summary>Which taskbars a choice of screens covers: the main one or not, and which secondary ones.</summary>
    readonly record struct Screens(bool Main, HashSet<string> Secondary)
    {
        public bool Has(string device) => Secondary.Contains(device);
    }

    static Screens Pick(DisplayTarget target, string monitor, HashSet<string> secondaries) => target switch
    {
        DisplayTarget.All => new(true, secondaries),
        DisplayTarget.One when secondaries.Contains(monitor) => new(false, [monitor]),
        // The main screen, or the picked screen is the main one or not connected right now.
        _ => new(true, []),
    };

    /// <summary>
    /// Matches the overlays to the taskbars that exist now and the screens picked: one for every
    /// taskbar that gets the progress bar, the player, or both.
    /// </summary>
    void Sync()
    {
        var taskbars = Monitors.Taskbars();
        var secondaries = taskbars.Where(t => !t.IsMain).Select(t => t.Monitor.DeviceName).ToHashSet();
        var bar = Pick(_s.DisplayTarget, _s.DisplayMonitor, secondaries);
        var player = _s.PlayerTarget switch
        {
            PlayerTarget.SameAsBar => bar,
            PlayerTarget.All => Pick(DisplayTarget.All, "", secondaries),
            PlayerTarget.One => Pick(DisplayTarget.One, _s.PlayerMonitor, secondaries),
            _ => Pick(DisplayTarget.Main, "", secondaries),
        };
        var wanted = bar.Secondary.Union(player.Secondary).ToHashSet();

        Main.ShowsBar = bar.Main;
        Main.ShowsPlayer = player.Main;
        foreach (var device in _secondary.Keys.Where(d => !wanted.Contains(d)).ToList())
        {
            _secondary[device].Dispose();
            _secondary.Remove(device);
        }
        foreach (var device in wanted)
        {
            if (!_secondary.TryGetValue(device, out var overlay))
            {
                overlay = new TaskbarOverlay(_s, _media, _lyrics, _audio, _trackInfo, device);
                _secondary[device] = overlay;
                overlay.ShowsBar = bar.Has(device);
                overlay.ShowsPlayer = player.Has(device);
                overlay.Start();
                continue;
            }
            overlay.ShowsBar = bar.Has(device);
            overlay.ShowsPlayer = player.Has(device);
        }

        static string Describe(Screens s) => $"main {(s.Main ? "on" : "off")}, secondary [{string.Join(", ", s.Secondary.Order())}]";
        string plan = $"bar {Describe(bar)}; player {Describe(player)}";
        if (plan != _lastPlan) Log.Info($"Taskbars: {plan}");
        _lastPlan = plan;
    }

    public void Dispose()
    {
        _sync.Stop();
        _refresh.Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _s.PropertyChanged -= OnSettingChanged;
        foreach (var overlay in _secondary.Values) overlay.Dispose();
        _secondary.Clear();
        Main.Dispose();
        _audio.Dispose();
    }
}
