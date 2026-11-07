using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views;

namespace MusicBar;

public partial class App : Application
{
    const string MutexName = "MusicBar.SingleInstance.v1";
    const string ShowSettingsEventName = "MusicBar.ShowSettings.v1";
    const string ExitEventName = "MusicBar.Exit.v1";
    const string LinkEventName = "MusicBar.OpenLink.v1";

    Mutex? _mutex;
    bool _ownsMutex;
    EventWaitHandle? _showSignal;
    EventWaitHandle? _exitSignal;
    EventWaitHandle? _linkSignal;
    volatile bool _exiting;

    SettingsService? _settings;
    MediaService? _media;
    LyricsService? _lyrics;
    TaskbarOverlays? _bars;
    SettingsWindow? _settingsWindow;
    TrayIcon? _tray;
    UpdateService? _updates;
    UpdateWindow? _updateWindow;
    System.Windows.Threading.DispatcherTimer? _updateTimer;
    ListeningStats? _stats;
    RecapWindow? _recapWindow;
    RecapBannerWindow? _recapBanner;
    System.Windows.Threading.DispatcherTimer? _recapTimer;

    public static App Instance => (App)Current;

    public TaskbarOverlay? Overlay => _bars?.Main;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            PutWindowsBack();
            Shutdown();
            return;
        }

        bool exitRequest = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        // A musicbar:// link from the website (an item to put on the taskbar).
        var link = e.Args.FirstOrDefault(GalleryLinks.IsLink);
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex || exitRequest)
        {
            // Already running: forward the request to that instance, then leave.
            try
            {
                if (link != null && !exitRequest) System.IO.File.WriteAllText(GalleryLinks.Pending, link);
                using var signal = EventWaitHandle.OpenExisting(exitRequest ? ExitEventName : link != null ? LinkEventName : ShowSettingsEventName);
                signal.Set();
            }
            catch (Exception)
            {
            }
            if (_ownsMutex) _mutex.ReleaseMutex();
            _ownsMutex = false;
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _linkSignal = new EventWaitHandle(false, EventResetMode.AutoReset, LinkEventName);
        new Thread(() =>
        {
            var handles = new WaitHandle[] { _showSignal, _exitSignal, _linkSignal };
            while (!_exiting)
            {
                int which = WaitHandle.WaitAny(handles);
                if (_exiting) break;
                if (which == 0) Dispatcher.BeginInvoke(() => ShowSettings());
                else if (which == 1) Dispatcher.BeginInvoke(ExitApp);
                else Dispatcher.BeginInvoke(OpenPendingLink);
            }
        })
        { IsBackground = true, Name = "MusicBar.Signals" }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Log.Error("Fatal exception", ex.ExceptionObject as Exception);
            // Do not leave the tray's volume icon and notification button hidden behind us, nor Windows in our mode.
            TrayMerge.Restore();
            WindowsModeSync.PutBackNow();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            TrayMerge.Restore();
            WindowsModeSync.PutBackNow();
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Error("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        Log.Info($"MusicBar starting ({string.Join(" ", e.Args)})");
        CrashReport.Started();
        SafeStart.Detect(e.Args);
        // The website's switches as last read, before anything reaches into Explorer.
        RemoteFlags.LoadCached();
        _settings = new SettingsService();
        var settings = _settings.Current;
        // Hardware acceleration off (or MUSICBAR_RENDER=software, for measuring): every window draws on the CPU,
        // for graphics drivers that crash or flicker under MusicBar. Skins need the card, so they rest meanwhile.
        if (!settings.HardwareAcceleration || Environment.GetEnvironmentVariable("MUSICBAR_RENDER") == "software")
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Log.Info("Drawing on the CPU: hardware acceleration is off");
        }
        // The very first time, before anything is touched: how Windows looked, to give back on uninstall.
        SystemSnapshot.TakeOnce(settings.WindowsModeBefore);
        Loc.Instance.Language = settings.Language;
        // The default lyric font is Windows 11's; on Windows 10 use the Segoe UI it does have.
        if (settings.FontFamily == "Segoe UI Variable Text"
            && !System.Windows.Media.Fonts.SystemFontFamilies.Any(f => f.Source == settings.FontFamily))
            settings.FontFamily = "Segoe UI";
        if (settings.StartWithWindows) StartupService.Apply(true);
        settings.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(AppSettings.StartWithWindows)) StartupService.Apply(settings.StartWithWindows);
            else if (ev.PropertyName == nameof(AppSettings.Language)) SwitchLanguage(settings.Language);
            // The see-through taskbar turned on again: the helper may be tried again.
            else if (ev.PropertyName == nameof(AppSettings.TransparentTaskbar) && settings.TransparentTaskbar) settings.HelperQuarantined = false;
        };
        CrashReport.ExplorerCrashed += report => Dispatcher.BeginInvoke(() => OnExplorerCrashed(report));

        _media = new MediaService(settings);
        _lyrics = new LyricsService(settings);
        _bars = new TaskbarOverlays(settings, _media, _lyrics);
        _bars.Start();

        _tray = new TrayIcon(() => ShowSettings(), _bars.Main.ShowMenu);
        _media.TrackChanged += (_, _) =>
            _tray.SetTooltip(_media.HasSession && _media.Title.Length > 0 ? $"MusicBar · {_media.Title}" : "MusicBar");

        if (e.Args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase))) _media.StartDemo();
        // Not awaited: right after signing in it may take a few tries, and nothing else waits on it.
        else _ = _media.InitializeAsync();
        _stats = new ListeningStats(settings, _media);

        // The first time, the welcome tour instead of the settings window.
        if (_settings.IsFirstRun || e.Args.Any(a => a.Equals("--welcome", StringComparison.OrdinalIgnoreCase)))
            ShowWelcome();
        else if (e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            ShowSettings();
        GalleryLinks.Register();
        if (link != null) OpenLink(link);
        StartUpdateChecks(settings);
        StartMemoryLog();
        StartRecapChecks(settings, now: e.Args.Any(a => a.Equals("--recap", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// From Christmas Eve to the 7th of January, a banner offers the year's recap, at most once a
    /// day, until it is opened or turned down for the year. <paramref name="now"/> (the --recap
    /// switch) shows it straight away whatever the date.
    /// </summary>
    void StartRecapChecks(AppSettings settings, bool now)
    {
        void Check(bool forced)
        {
            if (_stats == null || _exiting || _recapBanner != null || _recapWindow != null) return;
            var today = DateTime.Now;
            int year = YearRecap.SeasonYear(today);
            if (!forced && (!settings.KeepListeningStats || !YearRecap.InSeason(today) || settings.RecapDoneYear == year
                || settings.RecapAskedOn == today.ToString("yyyy-MM-dd"))) return;
            var recap = YearRecap.From(_stats.Year(year));
            if (!forced && !recap.Worthwhile) return;
            settings.RecapAskedOn = today.ToString("yyyy-MM-dd");
            Log.Info($"Offering the {year} recap ({recap.Minutes:0} min)");
            var accent = Overlay?.PreviewPalette()?.Primary ?? ColorUtil.Brand;
            _recapBanner = new RecapBannerWindow(recap, settings, accent);
            _recapBanner.OpenRequested += () => ShowRecap(year);
            _recapBanner.Closed += (_, _) => _recapBanner = null;
            _recapBanner.Show();
        }

        var first = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(now ? 3 : 40) };
        first.Tick += (_, _) =>
        {
            first.Stop();
            Check(now);
        };
        first.Start();
        _recapTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _recapTimer.Tick += (_, _) => Check(false);
        _recapTimer.Start();
    }

    /// <summary>The year in music (the season's year at the end of the year, this year otherwise).</summary>
    public void ShowRecap(int? year = null)
    {
        if (_stats == null || _settings == null || _exiting) return;
        if (_recapWindow != null)
        {
            _recapWindow.Activate();
            return;
        }
        var today = DateTime.Now;
        int y = year ?? (YearRecap.InSeason(today) ? YearRecap.SeasonYear(today) : today.Year);
        _stats.Save();
        var accent = Overlay?.PreviewPalette()?.Primary ?? ColorUtil.Brand;
        _recapWindow = new RecapWindow(YearRecap.From(_stats.Year(y)), _settings.Current, accent);
        _recapWindow.Closed += (_, _) => _recapWindow = null;
        _recapWindow.Show();
        _recapWindow.Activate();
    }

    /// <summary>A while after startup (starting with Windows, the network may still be waking up), then once a day.</summary>
    void StartUpdateChecks(AppSettings settings)
    {
        _updates = new UpdateService(settings);
        var first = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        first.Tick += (_, _) =>
        {
            first.Stop();
            _ = CheckForUpdatesAsync(manual: false);
            _ = RemoteFlags.RefreshAsync(settings);
        };
        first.Start();
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateTimer.Tick += (_, _) =>
        {
            _ = CheckForUpdatesAsync(manual: false);
            _ = RemoteFlags.RefreshAsync(settings);
        };
        _updateTimer.Start();
    }

    /// <summary>
    /// Looks for a newer release on GitHub and offers it, unless the user skipped that one; a check
    /// by hand offers it anyway. Returns the newer release, or null when there is none.
    /// </summary>
    public async Task<ReleaseInfo?> CheckForUpdatesAsync(bool manual)
    {
        if (_updates == null || _settings == null || _exiting) return null;
        var settings = _settings.Current;
        if (!manual && !settings.CheckForUpdates) return null;
        var release = await _updates.CheckAsync(CancellationToken.None);
        if (release == null || _exiting) return release;
        if (!manual && release.Tag == settings.SkippedVersion) return release;
        if (_updateWindow != null)
        {
            _updateWindow.Activate();
            return release;
        }
        var accent = Overlay?.PreviewPalette()?.Primary ?? ColorUtil.Brand;
        _updateWindow = new UpdateWindow(_updates, release, settings, accent);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
        return release;
    }

    /// <param name="page">A page to open on (its place in the navigation list), or null to keep the one shown.</param>
    OnboardingWindow? _welcome;

    /// <summary>The welcome tour (the first run, Settings → About, or --welcome): one at a time.</summary>
    public void ShowWelcome()
    {
        if (_settings == null || _media == null || _exiting) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_welcome != null)
            {
                _welcome.Activate();
                return;
            }
            _welcome = new OnboardingWindow(_settings.Current, _media, () => ShowSettings());
            _welcome.Closed += (_, _) => _welcome = null;
            _welcome.Show();
            _welcome.Activate();
        });
    }

    /// <summary>The link a second copy left behind (it was started by the browser while this one ran).</summary>
    void OpenPendingLink()
    {
        try
        {
            var link = System.IO.File.ReadAllText(GalleryLinks.Pending);
            System.IO.File.Delete(GalleryLinks.Pending);
            OpenLink(link);
        }
        catch (System.IO.IOException)
        {
        }
    }

    /// <summary>musicbar://install/&lt;slug&gt;: the online gallery page opens and puts that item on the taskbar.</summary>
    // Explorer crashes seen while the taskbar helper was in it, for the failsafe below.
    readonly System.Collections.Generic.List<DateTime> _explorerCrashes = [];

    /// <summary>
    /// The failsafe for the helper MusicBar loads into Explorer, as tools like StartAllBack have one: when Windows'
    /// crash report names the helper, or Explorer crashes twice in ten minutes while it was loaded, MusicBar stops
    /// loading it and tints over the taskbar instead (the crashed Explorer comes back without it). Turning the
    /// see-through taskbar off and on again allows it again.
    /// </summary>
    void OnExplorerCrashed(string report)
    {
        var s = _settings?.Current;
        if (s == null || s.HelperQuarantined || XamlTaskbar.LoadedAt == DateTime.MinValue) return;
        bool ours = report.Contains("MusicBar.TaskbarTap", StringComparison.OrdinalIgnoreCase);
        _explorerCrashes.RemoveAll(at => DateTime.Now - at > TimeSpan.FromMinutes(10));
        _explorerCrashes.Add(DateTime.Now);
        if (!ours && _explorerCrashes.Count < 2) return;
        s.HelperQuarantined = true;
        Log.Info(ours
            ? "Explorer crashed in MusicBar's taskbar helper: the helper is no longer loaded, the bar tints over the taskbar"
            : "Explorer crashed twice in ten minutes with the taskbar helper loaded: the helper is no longer loaded, the bar tints over the taskbar");
    }

    void OpenLink(string link)
    {
        if (GalleryLinks.SlugOf(link) is not { } slug)
        {
            Log.Info($"Ignored a link MusicBar doesn't know: {link[..Math.Min(link.Length, 80)]}");
            return;
        }
        Log.Info($"Gallery link: {slug}");
        ShowSettings(SettingsWindow.GalleryPage);
        _ = _settingsWindow?.OpenFromLinkAsync(slug);
    }

    public void ShowSettings(int? page = null)
    {
        if (_settings == null || _media == null || _lyrics == null || _exiting) return;
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settings.Current, _media, _lyrics);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }
        if (page is int p) _settingsWindow.Page = p;
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Bound text follows the language at once. The settings window also builds text in code,
    /// so it is reopened in place, on the same page, once the picker that changed it has returned.
    /// </summary>
    void SwitchLanguage(AppLanguage language)
    {
        Loc.Instance.Language = language;
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindow is not { } old || _settings == null || _media == null || _lyrics == null) return;
            var (page, left, top, width, height) = (old.Page, old.Left, old.Top, old.Width, old.Height);
            old.Close();
            _settingsWindow = new SettingsWindow(_settings.Current, _media, _lyrics)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = left, Top = top, Width = width, Height = height,
            };
            _settingsWindow.Page = page;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    public void ExitApp()
    {
        _exiting = true;
        _settings?.SaveNow();
        _updateTimer?.Stop();
        _updateWindow?.Close();
        _recapTimer?.Stop();
        _recapBanner?.Close();
        _recapWindow?.Close();
        _stats?.Dispose();
        _settingsWindow?.Close();
        _welcome?.Close();
        _bars?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _showSignal?.Set();
        Shutdown();
    }

    /// <summary>
    /// Notes the memory in use every half hour, so a log sent in shows whether it keeps growing and
    /// where: inside .NET's heap or outside it. For a test, MUSICBAR_MEMLOG sets the interval in
    /// seconds, and MUSICBAR_MEMLOG_FULL=1 collects everything first, leaving only what is alive.
    /// </summary>
    void StartMemoryLog()
    {
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MUSICBAR_MEMLOG"), out var s) && s > 0 ? s : 1800;
        bool full = Environment.GetEnvironmentVariable("MUSICBAR_MEMLOG_FULL") == "1";
        _memoryTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _memoryTimer.Tick += (_, _) =>
        {
            if (full)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            // Windows drawn by the graphics card cost driver memory: only a skin should need it.
            var card = Windows.OfType<Window>()
                .Where(w => HwndSource.FromHwnd(new WindowInteropHelper(w).Handle) is { CompositionTarget.RenderMode: RenderMode.Default } && w.IsVisible)
                .Select(w => w.Title).ToList();
            Log.Info($"Memory: private {me.PrivateMemorySize64 >> 20} MB, .NET heap {GC.GetTotalMemory(false) >> 20} MB "
                     + $"(committed {gc.TotalCommittedBytes >> 20} MB), handles {me.HandleCount}, GCs {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"
                     + $", on the graphics card: {(card.Count == 0 ? "none" : string.Join(", ", card))}"
                     + $", taskbar scans {TaskbarTracker.TakeScanCount()}");
        };
        _memoryTimer.Start();
    }

    System.Windows.Threading.DispatcherTimer? _memoryTimer;

    /// <summary>
    /// Run by the uninstaller (--uninstall): a copy still running hands back what it holds and quits,
    /// a mode or a folded tray left by a run that ended badly is put back, then every Windows setting
    /// MusicBar changed goes back to how it was before MusicBar first ran (Explorer restarts if that
    /// needs it), and the snapshot is forgotten.
    /// </summary>
    static void PutWindowsBack()
    {
        Log.Info("Uninstalling: putting Windows back");
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ExitEventName);
            signal.Set();
        }
        catch (Exception)
        {
        }
        for (int i = 0; i < 60 && OthersRunning(); i++) Thread.Sleep(100);

        var service = new SettingsService();
        if (service.Current.WindowsModeBefore >= 0)
        {
            new WindowsModeSync(service.Current).Release();
            service.SaveNow();
        }
        TrayMerge.RecoverLeftovers(Native.FindWindow("Shell_TrayWnd", null));
        GalleryLinks.Unregister();
        var (putBack, restart) = SystemSnapshot.PutBack();
        if (restart) TaskbarTweaks.RestartExplorer();
        SystemSnapshot.Forget();
        Log.Info($"Uninstalling: {putBack.Count} Windows setting(s) put back{(restart ? ", Explorer restarted" : "")}");
    }

    static bool OthersRunning()
    {
        var all = System.Diagnostics.Process.GetProcessesByName("MusicBar");
        try
        {
            return all.Any(p => p.Id != Environment.ProcessId);
        }
        finally
        {
            foreach (var p in all) p.Dispose();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stats?.Dispose();
        _tray?.Dispose();
        _settings?.SaveNow();
        if (_ownsMutex) Log.Info("MusicBar closing");
        CrashReport.Closed();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        base.OnExit(e);
    }
}
