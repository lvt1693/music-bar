using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// Puts MusicBar on the Windows taskbar: a click-through progress layer that
/// spans the whole taskbar (just below its icons), a capsule widget with the
/// cover, transport buttons and expandable lyrics in the taskbar's empty
/// area, and a song card that pops up from the cover.
/// </summary>
public sealed class TaskbarOverlay : IDisposable, ILyricsHost
{
    const uint KeepPlace = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;

    readonly AppSettings _s;
    readonly MediaService _media;
    readonly LyricsService _lyricsService;
    readonly TrackInfoService _trackInfo;
    readonly TaskbarTracker _tracker;
    readonly TaskbarLayerWindow _layer;
    readonly WidgetWindow _widget;
    readonly InfoPopupWindow _popup;
    readonly LyricsBandWindow _band;
    readonly Face.FaceWindow _face;
    bool _faceShown;
    LyricsPanelWindow? _lyricsPanel;
    BubbleWindow? _bubble;
    readonly SolidColorBrush _accentBrush = new(ColorUtil.Brand);
    readonly SolidColorBrush _playGlyphBrush = new(Colors.White);
    readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly AudioMeter _audio;
    readonly DispatcherTimer _frameTimer = new(DispatcherPriority.Render);
    readonly System.Collections.Generic.List<(double Left, double Right)> _quietZones = [];
    (double Left, double Right)? _quietShown;
    DateTime _quietLostAt = DateTime.MinValue;
    // Kept clear of Windows' icons: the empty stretches the bar runs through, where they belong and where they are drawn.
    System.Collections.Generic.List<(double Left, double Right)> _piecesTarget = [];
    readonly System.Collections.Generic.List<(double Left, double Right)> _piecesShown = [];
    // Only on the main taskbar, where the tray is.
    readonly ControlCenter? _controlCenter;
    readonly WindowsModeSync? _windowsMode;

    Palette? _artPalette;
    CancellationTokenSource? _lyricsCts;
    LyricsResult? _lyricsResult;
    LyricsStatus _lyricsStatus = LyricsStatus.None;
    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3)];
    CancellationTokenSource? _detailsCts;
    TrackDetails? _details;

    DateTime _noSessionSince = DateTime.MinValue;
    bool _fullscreen;
    bool _idle;
    bool _layerShown;
    bool _widgetShown;
    bool _rendering;
    bool _disposed;

    bool? _translucentTb;
    DateTime _translucentCheckedAt;
    TaskbarKind _kind;
    DateTime _kindCheckedAt = DateTime.MinValue;
    IntPtr _clearedTaskbar;
    Color _currentPrimary = ColorUtil.Brand;
    Color _currentSecondary = ColorUtil.Brand;
    Native.RECT _lastWidgetRect;
    Native.RECT _loggedWidgetPlace;
    Native.RECT? _bandRect;
    bool _usingBand;
    bool _lyricsShownLogged;
    // Each mismatch between a window's DPI and its screen's, corrected once (see EnsureDpi).
    readonly System.Collections.Generic.HashSet<string> _dpiTried = [];

    // What the lyrics area shows, so it can be re-staged when it moves between band and widget.
    SyncedLyrics? _stagedLyrics;
    string _stagedTitle = "";
    string _stagedArtist = "";

    // The taskbar's short side in DIPs, and the edge it is docked to, as last styled for.
    double _styledThickness = -1;
    TaskbarEdge _styledEdge = TaskbarEdge.Bottom;
    bool _styledOver;

    bool Vertical => TaskbarSide.IsVertical(_styledEdge);

    /// <param name="monitorDevice">The monitor whose secondary taskbar to use; null for the main taskbar,
    /// which also gets the control center.</param>
    public TaskbarOverlay(AppSettings settings, MediaService media, LyricsService lyrics, AudioMeter audio,
        TrackInfoService trackInfo, string? monitorDevice)
    {
        _s = settings;
        _media = media;
        _lyricsService = lyrics;
        _audio = audio;
        _trackInfo = trackInfo;
        _tracker = new TaskbarTracker(monitorDevice);
        _layer = new TaskbarLayerWindow { Audio = _audio };
        _widget = new WidgetWindow(media, _accentBrush, _playGlyphBrush);
        _popup = new InfoPopupWindow(_accentBrush);
        _band = new LyricsBandWindow();
        _face = new Face.FaceWindow(settings, media, audio);
        _widget.MenuRequested += ShowMenu;
        _widget.InfoRequested += ShowInfo;
        // The card stays while the pointer travels up to it, so its timeline can be reached.
        _widget.InfoDismissed += _popup.RequestDismiss;
        _popup.SeekRequested += target => _ = _media.SeekAsync(target);
        _widget.ExpandToggled += () => _s.LyricsExpanded = !_s.LyricsExpanded;
        _widget.LyricsClicked += ShowLyricsPanel;
        _widget.LyricsWheel += delta =>
        {
            if (_lyricsResult?.Lyrics == null) return;
            NudgeOffset(delta > 0 ? 250 : -250);
            int ms = (int)_lyricsResult.Lyrics.Offset.TotalMilliseconds;
            Say(LyricsService.DescribeTiming(ms), "", 1.6);
        };

        if (monitorDevice == null)
        {
            _controlCenter = new ControlCenter(settings, _tracker, _accentBrush);
            _controlCenter.TrayRearranged += _tracker.Rescan;
            // A new playback device: start listening to it instead of the old one.
            _controlCenter.OutputChanged += _audio.Restart;
            _windowsMode = new WindowsModeSync(settings);
        }

        _tracker.LayoutChanged += Reposition;
        _tracker.ZOrderCheck += () =>
        {
            UpdateTransparency();
            EnsureZOrder();
            _controlCenter?.Tick();
        };
        _s.PropertyChanged += OnSettingChanged;
        Loc.Changed += OnLanguageChanged;
        _media.PropertyChanged += OnMediaPropertyChanged;
        _media.TrackChanged += OnMediaTrackChanged;
        _media.ThumbnailChanged += OnMediaThumbnailChanged;
        _watch.Tick += (_, _) => Watch();
        _frameTimer.Tick += OnFrame;
        _backdrop.Changed += ApplyAppearance;
    }

    void OnMediaTrackChanged(object? sender, EventArgs e) => OnTrackChanged();

    void OnMediaThumbnailChanged(object? sender, EventArgs e) => OnThumbnailChanged();

    /// <summary>
    /// Whether this overlay draws the progress bar on its taskbar. The main taskbar's overlay always
    /// exists (it owns the control center and the menu) but steps aside when other screens are picked.
    /// </summary>
    public bool ShowsBar
    {
        get => _showsBar;
        set
        {
            if (value == _showsBar) return;
            _showsBar = value;
            // The tray icons only need Windows' mode changed while the bar is behind them.
            if (_windowsMode != null) ApplyAppearance();
            Reposition();
        }
    }

    /// <summary>Whether this overlay puts the player and the lyrics on its taskbar (they can sit on another screen than the bar).</summary>
    public bool ShowsPlayer
    {
        get => _showsPlayer;
        set
        {
            if (value == _showsPlayer) return;
            _showsPlayer = value;
            Reposition();
        }
    }

    bool _showsBar = true;
    bool _showsPlayer = true;
    // The player and lyrics are up on this taskbar right now.
    bool _playerOn;

    /// <summary>True when the layer is drawn over the taskbar instead of under it.</summary>
    public bool IsOverMode
    {
        get
        {
            if (_s.LayerMode == LayerMode.Over) return true;
            if (_s.LayerMode == LayerMode.Under) return false;
            return !TranslucentTaskbarDetected && (!OwnsTransparency || _clearingFailed);
        }
    }

    /// <summary>
    /// Under the taskbar, the bar only shows while the taskbar really is see-through. Asked to
    /// clear it, if the helper in Explorer still hasn't after a few seconds, the bar goes over the
    /// taskbar (tinted) instead of sitting out of sight behind a solid one, and back under once
    /// the helper says the taskbar is clear.
    /// </summary>
    void CheckClearing()
    {
        bool wanted = _s.LayerMode == LayerMode.Auto && _clearedTaskbar != IntPtr.Zero && CurrentTaskbarKind == TaskbarKind.Windows11;
        // No answer at all means no helper in that Explorer: it failed to load, or another tool using the
        // same XAML diagnostics (Windhawk's Taskbar Styler) blocked it. Loading takes a moment, so wait longer.
        bool? cleared = wanted ? XamlTaskbar.Cleared(_clearedTaskbar) : null;
        if (cleared == true)
        {
            _notClearedSince = DateTime.MinValue;
            if (!_clearingFailed) return;
            _clearingFailed = false;
            Log.Info("The taskbar is see-through again: the bar goes back under its icons");
        }
        else if (wanted)
        {
            if (_notClearedSince == DateTime.MinValue) _notClearedSince = DateTime.Now;
            else if (!_clearingFailed && DateTime.Now - _notClearedSince > TimeSpan.FromSeconds(cleared == false ? 3 : 10))
            {
                _clearingFailed = true;
                Log.Info(cleared == false
                    ? "The taskbar didn't go see-through: the bar goes over it instead of hiding behind it"
                    : "No taskbar helper answers in Explorer (blocked, or it didn't load): the bar goes over the taskbar");
            }
        }
        else
        {
            _notClearedSince = DateTime.MinValue;
        }
    }

    bool _clearingFailed;
    DateTime _notClearedSince = DateTime.MinValue;

    /// <summary>
    /// MusicBar clears the taskbar background itself: allowed by the user and
    /// TranslucentTB is not already doing it. Windows 10 style taskbars take a
    /// DWM accent; the Windows 11 taskbar needs the helper inside Explorer.
    /// A taskbar that hides itself keeps its background (unless the bar was put under it on
    /// purpose): windows reach under it, so cleared it would show them behind the icons and lyrics.
    /// </summary>
    public bool OwnsTransparency => _s.TransparentTaskbar && !TranslucentTaskbarDetected
        && !(CurrentTaskbarKind == TaskbarKind.Windows11 && XamlTaskbar.Blocked)
        && !(_tracker.AutoHides && _s.LayerMode == LayerMode.Auto);

    /// <summary>The taskbar is set to hide itself.</summary>
    public bool TaskbarAutoHides => _tracker.AutoHides;

    TaskbarKind CurrentTaskbarKind
    {
        get
        {
            if (DateTime.Now - _kindCheckedAt > TimeSpan.FromSeconds(5))
            {
                _kind = TaskbarTweaks.Kind;
                _kindCheckedAt = DateTime.Now;
            }
            return _kind;
        }
    }

    /// <summary>
    /// Only this overlay's taskbar goes see-through, and only while the layer is behind it: a
    /// clear taskbar with nothing of MusicBar's there (another monitor, or MusicBar hidden while
    /// nothing plays) would show the wallpaper smeared through Windows' blur.
    /// Explorer resets the accent every now and then (Start menu, theme), so this runs on every tick.
    /// </summary>
    void UpdateTransparency()
    {
        if (_disposed) return;
        if (_s.TransparentTaskbar && CurrentTaskbarKind == TaskbarKind.Windows11) XamlTaskbar.CheckHelper(_tracker.Handle);
        var want = OwnsTransparency && _layerShown ? _tracker.Handle : IntPtr.Zero;
        if (_clearedTaskbar != IntPtr.Zero && _clearedTaskbar != want) RestoreTransparency();
        _clearedTaskbar = want;
        if (want == IntPtr.Zero) return;
        if (CurrentTaskbarKind == TaskbarKind.Windows11) XamlTaskbar.Clear(this, want);
        else TaskbarTransparency.Apply(want);
    }

    void RestoreTransparency()
    {
        XamlTaskbar.Restore(this);
        // The Windows 11 taskbar gets its background back from the helper; only the accent needs undoing.
        if (CurrentTaskbarKind != TaskbarKind.Windows11) TaskbarTransparency.Restore(_clearedTaskbar);
    }

    /// <summary>A tool that makes the taskbar see-through (TranslucentTB) is running.</summary>
    public bool TranslucentTaskbarDetected
    {
        get
        {
            if (_translucentTb == null || DateTime.Now - _translucentCheckedAt > TimeSpan.FromSeconds(10))
            {
                var found = Process.GetProcessesByName("TranslucentTB");
                _translucentTb = found.Length > 0;
                foreach (var p in found) p.Dispose();
                _translucentCheckedAt = DateTime.Now;
            }
            return _translucentTb.Value;
        }
    }

    public void Start()
    {
        new WindowInteropHelper(_layer).EnsureHandle();
        new WindowInteropHelper(_widget).EnsureHandle();
        new WindowInteropHelper(_popup).EnsureHandle();
        new WindowInteropHelper(_band).EnsureHandle();
        new WindowInteropHelper(_face).EnsureHandle();
        ApplyAppearance();
        UpdateLyricsSurface(animate: false);
        _widget.SetPlaying(_media.IsPlaying, animate: false);
        OnSessionStateChanged();
        // An overlay made while a song plays (a second screen picked or plugged in) never hears
        // that song begin: it takes the song, its lyrics and cover now, or its player stays blank.
        if (_media.HasSession)
        {
            OnTrackChanged();
            if (_media.Thumbnail != null) OnThumbnailChanged();
        }
        _controlCenter?.Start();
        _tracker.Start();
        _watch.Start();
    }

    /// <summary>The taskbar's tray can be folded into the control center (Windows 10 style tray).</summary>
    public bool ControlCenterSupported => _controlCenter?.Supported ?? false;

    public void PreviewLyrics() => ActiveLyrics.Preview();

    // ------------------------------------------------------------------ the lyrics panel

    MediaService ILyricsHost.Media => _media;

    LyricsService ILyricsHost.LyricsSource => _lyricsService;

    LyricsStatus ILyricsHost.LyricsState => _lyricsStatus;

    LyricsResult? ILyricsHost.Lyrics => _lyricsResult;

    TimeSpan ILyricsHost.LyricsLead => TimeSpan.FromMilliseconds(_s.LyricsOffsetMs) + (_lyricsResult?.Lyrics?.Offset ?? TimeSpan.Zero);

    TrackQuery ILyricsHost.CurrentQuery() => new(_media.Title, _media.Artist, _media.Album, _media.Duration, _media.AppId);

    void ILyricsHost.NudgeLyrics(int ms) => NudgeOffset(ms);

    void ILyricsHost.UseLyrics(LyricsResult result)
    {
        _lyricsCts?.Cancel();
        _lyricsResult = result;
        _lyricsStatus = result.Status;
        if (result.Status == LyricsStatus.Instrumental) ShowLyricsMessage(_media.Title, Loc.T("Msg_Instrumental"));
        else if (result.Lyrics == null) ShowTrackMessage();
        PresentLyrics();
    }

    public event Action? LyricsChanged;

    /// <summary>The whole song's lyrics in a card beside the taskbar, near the pointer.</summary>
    public void ShowLyricsPanel()
    {
        if (_tracker.Monitor == null) return;
        _popup.Dismiss();
        if (_lyricsPanel == null)
        {
            _lyricsPanel = new LyricsPanelWindow(this, _s);
            _lyricsPanel.SetAccent(_currentPrimary);
        }
        var at = Native.GetCursorPos(out var p) ? new Point(p.X, p.Y) : _widget.CoverCenterOnScreen();
        _lyricsPanel.ShowAt(at, _tracker.Edge, _tracker.Rect, _tracker.Monitor.Bounds);
    }

    /// <summary>A short note in a bubble beside the taskbar, by the pointer.</summary>
    void Say(string text, string? glyph, double seconds)
    {
        if (_tracker.Monitor == null || !Native.GetCursorPos(out var p)) return;
        _bubble ??= new BubbleWindow();
        _bubble.Say(text, glyph, ColorUtil.ForDarkSurface(_currentPrimary), new Point(p.X, p.Y), _tracker.Edge, _tracker.Rect, _tracker.Monitor.Bounds, seconds);
    }

    /// <summary>
    /// Puts the song's synced lyrics on the bar, romanized as the settings ask. Romanizing a whole
    /// song takes a moment the first time, so it happens off the UI thread.
    /// </summary>
    async void PresentLyrics()
    {
        var result = _lyricsResult;
        LyricsChanged?.Invoke();
        if (result?.Status != LyricsStatus.Synced || result.Lyrics is not { } lyrics) return;
        var mode = _s.Romanization;
        bool vertical = Vertical;
        var shown = mode == Romanization.Off ? lyrics : await Task.Run(() => LyricRomanization.ForBar(lyrics, mode, vertical));
        if (!ReferenceEquals(result, _lyricsResult)) return;
        ShowSyncedLyrics(shown);
    }

    /// <summary>The presenter currently on screen: the free-space band, or the panel beside the cover.</summary>
    ILyricsView ActiveLyrics => _usingBand ? _band.LyricsView : _widget.LyricsView;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loc.Changed -= OnLanguageChanged;
        _s.PropertyChanged -= OnSettingChanged;
        _media.PropertyChanged -= OnMediaPropertyChanged;
        _media.TrackChanged -= OnMediaTrackChanged;
        _media.ThumbnailChanged -= OnMediaThumbnailChanged;
        _controlCenter?.Dispose();
        _windowsMode?.Release();
        if (_clearedTaskbar != IntPtr.Zero) RestoreTransparency();
        SetRendering(false);
        _watch.Stop();
        _tracker.Dispose();
        _audio.Want(this, false);
        _lyricsCts?.Cancel();
        _detailsCts?.Cancel();
        _layer.Close();
        _widget.Close();
        _popup.Close();
        _band.Close();
        _face.Close();
        _lyricsPanel?.Close();
        _bubble?.Close();
    }

    // ------------------------------------------------------------------ placement

    void Reposition()
    {
        if (_disposed) return;
        bool show = (_showsBar || _showsPlayer) && _tracker.Shown && !_fullscreen && !_idle;
        LogVisibility(show);
        // With nothing playing MusicBar steps aside, but the face may stay and nap on the plain taskbar.
        bool faceAlone = !show && _idle && _showsPlayer && _tracker.Shown && !_fullscreen && _s.FaceEnabled && _s.FaceStaysWhenIdle;
        if (!show)
        {
            bool keepFace = faceAlone && _tracker.HasScanned;
            HideAll(keepFace, _tracker.SlidAway);
            if (keepFace) PlaceFace(FaceRect(_tracker.Rect, _tracker.Scale, null, true, FaceLength(), 0));
            return;
        }

        var rc = _tracker.Rect;
        double scale = _tracker.Scale;
        bool over = IsOverMode;
        var edge = _tracker.Edge;
        double thickness = _tracker.Thickness / scale;
        if (Math.Abs(thickness - _styledThickness) > 0.1 || edge != _styledEdge || over != _styledOver)
        {
            bool turned = TaskbarSide.IsVertical(edge) != Vertical;
            _styledThickness = thickness;
            _styledEdge = edge;
            _styledOver = over;
            ApplyAppearance();
            if (turned)
            {
                // The other lyrics display takes over: give it what was on screen.
                Log.Info($"Taskbar docked {edge}");
                Restage();
                if (_s.Romanization == Romanization.Below) PresentLyrics();
            }
        }

        if (_showsBar)
        {
            _piecesTarget = BarPieces();
            if (!_layerShown)
            {
                _layer.Show();
                _layerShown = true;
            }
            Native.SetWindowPos(_layer.Hwnd, Native.HWND_TOPMOST, rc.Left, rc.Top, rc.Width, rc.Height, Native.SWP_NOACTIVATE);
            if (!over) Native.SetWindowPos(_layer.Hwnd, _tracker.Handle, 0, 0, 0, 0, KeepPlace);
            EnsureDpi(_layer, _layer.Hwnd, rc, scale, "layer");
        }
        else if (_layerShown)
        {
            _layer.Hide();
            _layerShown = false;
        }

        // A minimal taskbar has no player: only the progress, and the lyrics in a gap of their own if wanted.
        _playerOn = _showsPlayer && _s.PlayerMode != PlayerMode.Off;
        // The face goes with the player; beside it, the widget leaves it room at its anchored end.
        bool faceOn = _showsPlayer && _s.FaceEnabled && _tracker.HasScanned;
        int faceLength = faceOn ? FaceLength() : 0;
        int faceGap = (int)Math.Round(4 * scale);
        int reserve = faceOn && _s.FacePlacement == FacePlacement.BesidePlayer ? faceLength + faceGap : 0;
        bool anchorEnd = true;
        var widgetRect = _playerOn && _s.PlayerMode == PlayerMode.Full ? WidgetRect(rc, scale, out anchorEnd, reserve) : null;
        if (widgetRect is { } w)
        {
            _widget.SetAnchor(anchorEnd);
            if (!_widgetShown)
            {
                _widget.Show();
                _widgetShown = true;
            }
            Native.SetWindowPos(_widget.Hwnd, Native.HWND_TOPMOST, w.Left, w.Top, w.Width, w.Height, Native.SWP_NOACTIVATE);
            EnsureDpi(_widget, _widget.Hwnd, w, scale, "player");
            if (!w.Same(_lastWidgetRect))
            {
                _lastWidgetRect = w;
                // Said when the player moves along the taskbar, not at every step of an auto-hiding taskbar's slide.
                var place = new Native.RECT(w.Left - rc.Left, w.Top - rc.Top, w.Right - rc.Left, w.Bottom - rc.Top);
                if (!place.Same(_loggedWidgetPlace))
                {
                    _loggedWidgetPlace = place;
                    Log.Info($"Taskbar {rc}, occupied [{TaskbarTracker.Describe(_tracker.Occupied)}], widget {w}");
                }
            }
        }
        else if (_widgetShown)
        {
            _widget.Hide();
            _widgetShown = false;
            _popup.Dismiss();
        }

        var faceRect = faceOn ? FaceRect(rc, scale, widgetRect, anchorEnd, faceLength, faceGap) : null;
        _bandRect = _playerOn ? BandRect(rc, scale, widgetRect, faceRect) : null;
        // It may stroll in its own stretch of empty taskbar, never over the player or the lyrics.
        PlaceFace(faceRect, widgetRect, _bandRect);

        UpdateLyricsSurface(animate: true);
        SetRendering(true);
    }

    /// <summary>
    /// Draws everything anew after sleep, unlocking or a change of displays. A see-through window
    /// can come back from those with its surface lost, drawing nothing while it still counts as
    /// shown: the bar would be gone until MusicBar restarts. A size change makes WPF give it a new
    /// surface, and placing everything again (which puts the size back) follows straight away.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        _tracker.Rescan();
        _dpiTried.Clear();
        foreach (var (shown, hwnd) in new[] { (_layerShown, _layer.Hwnd), (_widgetShown, _widget.Hwnd), (_faceShown, _face.Hwnd) })
        {
            if (!shown || hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) continue;
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Width + 1, r.Height,
                Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        UpdateTransparency();
        Reposition();
        EnsureZOrder();
    }

    /// <summary>
    /// Says in the log each time the bar goes away or comes back, and why, so a report of it
    /// "disappearing" can be traced: no music, a full-screen window (which one), or no taskbar to sit on.
    /// </summary>
    void LogVisibility(bool show)
    {
        string why = show ? "shown"
            : !_showsBar && !_showsPlayer ? "not on this screen"
            // A taskbar that hides itself comes and goes all day: not a line each time.
            : _tracker.SlidAway ? ""
            : !_tracker.Shown ? "taskbar hidden or not found"
            : _fullscreen ? $"a full-screen window covers the taskbar: {_coveredBy}"
            : _idle ? "no music (Hide when no music is playing)"
            : "hidden";
        if (why.Length == 0 || why == _visibilityLogged) return;
        _visibilityLogged = why;
        Log.Info($"Bar on {_tracker.Monitor?.DeviceName ?? "?"}: {why}");
    }

    string? _visibilityLogged;

    /// <summary>
    /// A window should draw at its screen's DPI. Moved onto a screen of another scale, Windows tells
    /// it so (WM_DPICHANGED); when that went missing, it keeps drawing at the old scale and Windows
    /// stretches it, which looks soft. Told again here, with where it belongs, it redraws sharp.
    /// </summary>
    void EnsureDpi(Window window, IntPtr hwnd, Native.RECT rect, double scale, string name)
    {
        double drawn = VisualTreeHelper.GetDpi(window).DpiScaleX;
        if (Math.Abs(drawn - scale) <= 0.01 || !_dpiTried.Add($"{name} {drawn:0.##} {scale:0.##}")) return;
        int dpi = (int)Math.Round(96 * scale);
        var at = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(rect, at, false);
            Native.SendMessage(hwnd, 0x02E0 /* WM_DPICHANGED */, (IntPtr)(dpi | (dpi << 16)), at);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(at);
        }
        double now = VisualTreeHelper.GetDpi(window).DpiScaleX;
        Log.Info($"The {name} on {_tracker.Monitor?.DeviceName} drew at {drawn:0.##}x on a {scale:0.##}x screen: {(Math.Abs(now - scale) <= 0.01 ? "corrected" : $"still {now:0.##}x")}");
    }
    // What covered the taskbar last, for the log: the window's class and its program.
    string _coveredBy = "";
    IntPtr _coveredWindow;

    /// <param name="slidAway">The taskbar hid itself: the pointer has just left it, maybe for the song card.</param>
    void HideAll(bool keepFace = false, bool slidAway = false)
    {
        if (_layerShown) _layer.Hide();
        if (_widgetShown) _widget.Hide();
        if (_faceShown && !keepFace)
        {
            _face.Hide();
            _faceShown = false;
        }
        _layerShown = _widgetShown = _playerOn = false;
        if (slidAway) _popup.LetGo();
        else _popup.Dismiss();
        _band.Reveal(false, 0);
        SetRendering(false);
    }

    /// <param name="taken">What else is on the taskbar: the face may wander up to it, not over it.</param>
    void PlaceFace(Native.RECT? rect, params Native.RECT?[] taken)
    {
        if (rect is not { } r || _tracker.Monitor == null)
        {
            if (_faceShown) _face.Hide();
            _faceShown = false;
            return;
        }
        // Alone on the plain taskbar, its lines follow Windows' own light or dark mode.
        _face.ApplyStyle(_s, _layerShown ? LightBar : _windowsLight);
        if (!_faceShown)
        {
            _face.Show();
            _faceShown = true;
        }
        var face = AlongBar(r);
        var (roamStart, roamEnd) = (face.Start, face.End);
        int margin = (int)Math.Round(6 * _tracker.Scale);
        foreach (var p in FreePieces(taken))
        {
            if (p.Start > face.Start + 2 || p.End < face.End - 2) continue;
            (roamStart, roamEnd) = (Math.Min(face.Start, p.Start + margin), Math.Max(face.End, p.End - margin));
            break;
        }
        _face.Place(r, roamStart, roamEnd, _tracker.Edge, _tracker.Rect, _tracker.Monitor.Bounds);
    }

    /// <summary>The face's length along the taskbar in physical pixels: its box (116 x 88 design units) across the taskbar's thickness.</summary>
    int FaceLength()
    {
        int t = _tracker.Thickness;
        return (int)Math.Round(_tracker.IsHorizontal ? t * 116.0 / 88 * _s.FaceScale : t * 0.8 * _s.FaceScale);
    }

    /// <summary>
    /// Where the face sits: in the room the widget left beside its cover, or at the start of the
    /// taskbar, or just before or after the app icons. With no player it goes to the far end,
    /// beside the tray.
    /// </summary>
    Native.RECT? FaceRect(Native.RECT rc, double scale, Native.RECT? widget, bool anchorEnd, int length, int gap)
    {
        if (_s.FacePlacement == FacePlacement.BesidePlayer && widget is { } wr)
        {
            var w = AlongBar(wr);
            int at = anchorEnd ? w.End + gap : w.Start - gap - length;
            return AcrossBar(rc, at, at + length);
        }
        int margin = (int)Math.Round(6 * scale);
        var pieces = FreePieces(widget).Where(p => p.Width >= length + 2 * margin).ToList();
        if (pieces.Count == 0) return null;
        var icons = IconGroup();
        Span piece;
        bool atEnd;
        switch (_s.FacePlacement)
        {
            case FacePlacement.Start:
                (piece, atEnd) = (pieces[0], false);
                break;
            case FacePlacement.BeforeIcons when icons is { } i && pieces.LastOrDefault(p => p.End <= i.Start + 2) is { Width: > 0 } before:
                (piece, atEnd) = (before, true);
                break;
            case FacePlacement.AfterIcons when icons is { } i && pieces.FirstOrDefault(p => p.Start >= i.End - 2) is { Width: > 0 } after:
                (piece, atEnd) = (after, false);
                break;
            default:
                (piece, atEnd) = (pieces[^1], true);
                break;
        }
        int start = atEnd ? piece.End - margin - length : piece.Start + margin;
        return AcrossBar(rc, start, start + length);
    }

    /// <summary>The app icons with the Start button: the widest occupied stretch before the tray.</summary>
    Span? IconGroup()
    {
        var spans = _tracker.Occupied.Where(s => s.End < _tracker.End - _tracker.Length / 20).ToList();
        return spans.Count == 0 ? null : spans.MaxBy(s => s.Width);
    }

    /// <summary>The taskbar's empty stretches, less what the given windows take.</summary>
    System.Collections.Generic.List<Span> FreePieces(params Native.RECT?[] taken)
    {
        var pieces = _tracker.FreeSpans();
        foreach (var t in taken)
        {
            if (t is not { } r) continue;
            var w = AlongBar(r);
            var cut = new System.Collections.Generic.List<Span>();
            foreach (var f in pieces)
            {
                if (w.End <= f.Start || w.Start >= f.End)
                {
                    cut.Add(f);
                    continue;
                }
                if (w.Start > f.Start) cut.Add(new Span(f.Start, w.Start));
                if (w.End < f.End) cut.Add(new Span(w.End, f.End));
            }
            pieces = cut;
        }
        return pieces;
    }

    /// <summary>
    /// The biggest empty stretch of taskbar not taken by the widget or the face, whatever
    /// the icon alignment: the left side when icons are centred, the middle
    /// when they sit on the left. Lyrics are centred inside it.
    /// </summary>
    Native.RECT? BandRect(Native.RECT rc, double scale, Native.RECT? widget, Native.RECT? face)
    {
        if (!_tracker.HasScanned || _tracker.Occupied.Count == 0) return null;
        int margin = (int)Math.Round(16 * scale);
        int min = (int)Math.Round(220 * scale);
        int max = (int)Math.Round(820 * scale);

        var pieces = FreePieces(widget, face);
        var best = pieces.Where(p => p.Width - 2 * margin >= min).OrderByDescending(p => p.Width).FirstOrDefault();
        if (best.Width == 0) return null;
        int length = Math.Min(best.Width - 2 * margin, max);
        int at = best.Start + (best.Width - length) / 2;
        return AcrossBar(rc, at, at + length);
    }

    /// <summary>
    /// Where the bar runs when it keeps clear of Windows' icons: the taskbar's empty stretches, a
    /// little way off the icons and none too short, in the seekbar's own units (from the taskbar's
    /// left, or up from its bottom when vertical). Empty for the whole taskbar; one empty stretch
    /// when there is no room at all, so nothing is drawn.
    /// </summary>
    System.Collections.Generic.List<(double Left, double Right)> BarPieces()
    {
        var pieces = new System.Collections.Generic.List<(double Left, double Right)>();
        if (!_s.SeekAvoidIcons || !_tracker.HasScanned || _tracker.Occupied.Count == 0) return pieces;
        var rc = _tracker.Rect;
        double scale = _tracker.Scale;
        int clearance = (int)Math.Round(6 * scale);
        foreach (var f in _tracker.FreeSpans())
        {
            int start = f.Start > _tracker.Begin ? f.Start + clearance : f.Start;
            int end = f.End < _tracker.End ? f.End - clearance : f.End;
            if ((end - start) / scale < 40) continue;
            pieces.Add(_tracker.IsHorizontal
                ? ((start - rc.Left) / scale, (end - rc.Left) / scale)
                : ((rc.Bottom - end) / scale, (rc.Bottom - start) / scale));
        }
        if (pieces.Count == 0) pieces.Add((0, 0));
        pieces.Sort((p, q) => p.Left.CompareTo(q.Left));
        return pieces;
    }

    /// <summary>Eases the bar's stretches toward where they belong, so it slides aside as icons come and go.</summary>
    void UpdatePieces()
    {
        bool changed = false;
        if (_piecesShown.Count != _piecesTarget.Count)
        {
            _piecesShown.Clear();
            _piecesShown.AddRange(_piecesTarget);
            changed = true;
        }
        else
        {
            for (int i = 0; i < _piecesTarget.Count; i++)
            {
                var (from, to) = (_piecesShown[i], _piecesTarget[i]);
                var next = (Glide(from.Left, to.Left), Glide(from.Right, to.Right));
                if (next == from) continue;
                _piecesShown[i] = next;
                changed = true;
            }
        }
        if (changed) _layer.SetPieces(_piecesShown);
    }

    /// <summary>A screen rectangle's stretch along the taskbar.</summary>
    Span AlongBar(Native.RECT r) => _tracker.IsHorizontal ? new Span(r.Left, r.Right) : new Span(r.Top, r.Bottom);

    /// <summary>The whole thickness of the taskbar between two points along its length.</summary>
    Native.RECT AcrossBar(Native.RECT rc, int start, int end)
        => _tracker.IsHorizontal ? new Native.RECT(start, rc.Top, end, rc.Bottom) : new Native.RECT(rc.Left, start, rc.Right, end);

    /// <summary>
    /// Shows or hides the lyrics wherever they currently belong: in the
    /// free-space band, or in the panel that slides out beside the cover.
    /// </summary>
    void UpdateLyricsSurface(bool animate)
    {
        // Without the player there is no "beside the cover": the lyrics take the free space.
        bool useBand = (_s.LyricsPlacement == LyricsPlacement.LargestGap || _s.PlayerMode == PlayerMode.LyricsOnly) && _bandRect != null;
        if (useBand != _usingBand)
        {
            _usingBand = useBand;
            Restage();
        }

        bool show = _s.LyricsExpanded && _playerOn;
        if (show != _lyricsShownLogged)
        {
            _lyricsShownLogged = show;
            Log.Info($"Lyrics surface: show={show} band={useBand} bandRect={_bandRect?.ToString() ?? "none"}");
        }
        _widget.SetExpanded(show && !useBand, animate);
        _widget.SetLyricsElsewhere(useBand, show);

        if (useBand && show && _bandRect is { } r)
        {
            // Slide in from the widget's side.
            var (band, widget) = (AlongBar(r), AlongBar(_lastWidgetRect));
            double from = band.Start + band.End < widget.Start + widget.End ? 24 : -24;
            _band.Reveal(true, animate ? from : 0);
            Native.SetWindowPos(_band.Hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE);
        }
        else
        {
            _band.Reveal(false, 0);
        }
    }

    void Restage()
    {
        var target = ActiveLyrics;
        target.ShowMessage(_stagedTitle, _stagedArtist);
        if (_stagedLyrics != null) target.ShowLyrics(_stagedLyrics);
    }

    void ShowLyricsMessage(string title, string artist)
    {
        _stagedLyrics = null;
        _stagedTitle = title;
        _stagedArtist = artist;
        ActiveLyrics.ShowMessage(title, artist);
    }

    void ShowSyncedLyrics(SyncedLyrics lyrics)
    {
        _stagedLyrics = lyrics;
        ActiveLyrics.ShowLyrics(lyrics);
    }

    /// <summary>
    /// Picks the empty stretch of taskbar the widget lives in: by default the
    /// gap just left of the notification area, anchored to its right end.
    /// </summary>
    /// <remarks>
    /// On a vertical taskbar the same rules run top to bottom: "right" is the bottom end, where
    /// the tray is, and "left" the top.
    /// </remarks>
    /// <param name="reserve">Room to leave at the anchored end of the stretch (for the face beside the cover).</param>
    Native.RECT? WidgetRect(Native.RECT rc, double scale, out bool anchorEnd, int reserve = 0)
    {
        anchorEnd = _s.WidgetPlacement != WidgetPlacement.Left;
        if (!_tracker.HasScanned) return null;

        int margin = (int)Math.Round(10 * scale);
        int min = (int)Math.Round(140 * scale);
        int max = (int)Math.Round(_s.WidgetMaxWidth * scale);

        Span span;
        if (_tracker.Occupied.Count == 0)
        {
            // UI Automation gave nothing: fall back to the first third, free on a default Windows 11 taskbar.
            span = new Span(_tracker.Begin, _tracker.Begin + _tracker.Length * 3 / 10);
            anchorEnd = false;
        }
        else
        {
            var free = _tracker.FreeSpans().Where(f => f.Width - 2 * margin - reserve >= min).ToList();
            if (free.Count == 0) return null;
            span = anchorEnd ? free[^1] : free[0];
        }
        span = anchorEnd ? span with { End = span.End - reserve } : span with { Start = span.Start + reserve };

        int length = Math.Min(span.Width - 2 * margin, max);
        if (length < min) return null;
        int at = anchorEnd ? span.End - margin - length : span.Start + margin;
        return AcrossBar(rc, at, at + length);
    }

    /// <summary>
    /// The taskbar jumps to the top of the topmost band whenever it is
    /// clicked; put the layer back under it and the widget back over it.
    /// </summary>
    void EnsureZOrder()
    {
        if (_disposed || !(_layerShown || _playerOn || _faceShown) || _tracker.Handle == IntPtr.Zero) return;
        var tray = _tracker.Handle;
        if (_faceShown && !Native.IsAbove(_face.Hwnd, tray))
            Native.SetWindowPos(_face.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        if (!_layerShown)
        {
            // The player on its own, on a taskbar without the bar: it only has to stay above the taskbar.
        }
        else if (_styledOver)
        {
            if (!Native.IsAbove(_layer.Hwnd, tray)) Native.SetWindowPos(_layer.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        }
        else if (Native.GetWindow(tray, Native.GW_HWNDNEXT) != _layer.Hwnd)
        {
            Native.SetWindowPos(_layer.Hwnd, tray, 0, 0, 0, 0, KeepPlace);
        }

        if (_widgetShown && (!Native.IsAbove(_widget.Hwnd, tray) || (_layerShown && !Native.IsAbove(_widget.Hwnd, _layer.Hwnd))))
        {
            Native.SetWindowPos(_widget.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        }
        if (_popup.IsOpen && !Native.IsAbove(_popup.Hwnd, tray))
            Native.SetWindowPos(_popup.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        if (_band.IsRevealed && !Native.IsAbove(_band.Hwnd, tray))
            Native.SetWindowPos(_band.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
    }

    void Watch()
    {
        if (_media.HasSession) _noSessionSince = DateTime.MinValue;
        else if (_noSessionSince == DateTime.MinValue) _noSessionSince = DateTime.Now;

        CheckTheme();
        CheckClearing();
        bool idle = _s.HideWhenIdle && _noSessionSince != DateTime.MinValue
                    && (DateTime.Now - _noSessionSince).TotalSeconds > 2;
        bool fullscreen = TaskbarCovered();
        if (_controlCenter != null && _controlCenter.Covered != fullscreen)
        {
            _controlCenter.Covered = fullscreen;
            _controlCenter.Tick();
        }
        // Listen to the speakers only while a music-driven style is actually on screen and playing.
        _audio.Want(this, _s.AudioReactive && _layerShown && _media.IsPlaying && (SeekRenderer.UsesAudio(_s.SeekStyle) || _layer.Skinned));
        UpdateFramePace();
        if (idle != _idle || fullscreen != _fullscreen || (_layerShown && IsOverMode != _styledOver))
        {
            _idle = idle;
            _fullscreen = fullscreen;
            Reposition();
        }
    }

    /// <summary>
    /// A window fills this taskbar's screen and sits over the taskbar (a game, a video, a slide show
    /// in full screen): MusicBar steps aside exactly when the taskbar itself is out of sight. The
    /// shell's "busy" state is not asked: it also reads busy for a full-screen window on another
    /// screen or behind others, which hid MusicBar for good on some PCs.
    /// </summary>
    bool TaskbarCovered()
    {
        var monitor = _tracker.Monitor;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || monitor == null) return false;
        Native.GetWindowThreadProcessId(fg, out var pid);
        if (pid == Environment.ProcessId) return false;
        // Start, Search and other shell surfaces are screen-sized but are not fullscreen apps.
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow") return false;
        if (Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONULL) != monitor.Handle) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var b = monitor.Bounds;
        if (r.Left > b.Left || r.Top > b.Top || r.Right < b.Right || r.Bottom < b.Bottom) return false;
        // Windows lowers the taskbar under a full-screen window; while the taskbar stays on top
        // (a screen-sized window that isn't full screen to Windows), it is still in sight.
        bool covered = _tracker.Handle == IntPtr.Zero || !Native.IsAbove(_tracker.Handle, fg);
        if (covered && fg != _coveredWindow)
        {
            _coveredWindow = fg;
            string program;
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                program = p.ProcessName;
            }
            catch (Exception)
            {
                program = "?";
            }
            _coveredBy = $"{Native.ClassName(fg)} ({program})";
        }
        return covered;
    }

    // ------------------------------------------------------------------ frame loop

    /// <summary>
    /// A paced timer instead of CompositionTarget.Rendering: subscribing to the
    /// latter keeps WPF composing at the display's refresh rate even when
    /// nothing changes. 30 Hz while something moves, 4 Hz otherwise; on every
    /// display frame only while spectrum bars dance to the music.
    /// </summary>
    void SetRendering(bool on)
    {
        if (on == _rendering) return;
        _rendering = on;
        if (on) _frameTimer.Start();
        else _frameTimer.Stop();
        UpdateFramePace();
    }

    // Ticking on every display frame (CompositionTarget.Rendering) instead of on the timer.
    bool _everyFrame;

    void UpdateFramePace()
    {
        bool moving = _media.IsPlaying && (_s.SeekStyle != SeekStyle.Classic || _s.LyricsExpanded);
        // Paused, an animated style drifts slowly in place: half the frame rate is smooth enough.
        bool drifting = !_media.IsPlaying && _media.HasSession && _layerShown && _s.SeekIdleMotion && _s.SeekStyle != SeekStyle.Classic;
        // Bars jumping with the beat stutter at the timer's pace, which also drifts against the display's.
        // That costs about twice the CPU, so a laptop on its battery keeps the timer's 30 fps.
        bool beatDriven = SeekRenderer.WantsEveryFrame(_s.SeekStyle) || _layer.Skinned;
        bool everyFrame = _rendering && moving && _layerShown && beatDriven && _s.AudioReactive && !SystemSense.OnBattery;
        if (everyFrame != _everyFrame)
        {
            _everyFrame = everyFrame;
            if (everyFrame)
            {
                _frameTimer.Stop();
                CompositionTarget.Rendering += OnFrame;
            }
            else
            {
                CompositionTarget.Rendering -= OnFrame;
                if (_rendering) _frameTimer.Start();
            }
        }
        // The timer fires on Windows' 15.6 ms ticks: 33 ms comes out as 47 (21 fps), which suits the slow
        // styles; spectrum bars left on the timer ask for 30 and get 31 (32 fps).
        bool bars = moving && beatDriven;
        var interval = TimeSpan.FromMilliseconds(bars ? 30 : moving ? 33 : drifting ? 66 : 250);
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs frame && !FrameDue(frame.RenderingTime)) return;
        var pos = _media.GetPosition();
        var dur = _media.Duration;
        bool timeline = _media.HasSession && dur > TimeSpan.Zero;
        double progress = timeline ? Math.Clamp((double)pos.Ticks / dur.Ticks, 0, 1) : 0;
        // A live stream (or radio) has no length to run along: the bar shows full instead of empty.
        bool live = _media.HasSession && !timeline;
        _layer.SetProgress(live ? 1 : progress, timeline || live, _media.IsPlaying);
        _widget.SetProgress(progress);
        if (_popup.IsOpen) _popup.SetProgress(pos, dur);

        UpdatePieces();
        UpdateQuietZones();
        // Hidden lyrics need no karaoke updates; they catch up from the position when shown.
        if (!_s.LyricsExpanded) return;
        var lyricPos = pos + TimeSpan.FromMilliseconds(_s.LyricsOffsetMs) + (_lyricsResult?.Lyrics?.Offset ?? TimeSpan.Zero);
        ActiveLyrics.Update(lyricPos, _media.IsPlaying);
    }

    // The display's frame time as measured, the last frame seen, and frames since one was drawn.
    double _displayFrameMs = 1000.0 / 60;
    TimeSpan _lastDisplayFrame;
    int _framesSkipped;

    /// <summary>
    /// Whether this display frame gets drawn. WPF sometimes raises Rendering twice for one frame, and
    /// on a fast display (120, 144, 240 Hz) drawing every frame costs two to four times a 60 Hz one for
    /// no smoother bars: every second, third or fourth frame is drawn instead, evenly, near 60 a second.
    /// </summary>
    bool FrameDue(TimeSpan at)
    {
        if (at == _lastDisplayFrame) return false;
        double ms = (at - _lastDisplayFrame).TotalMilliseconds;
        _lastDisplayFrame = at;
        if (ms > 2 && ms < 40) _displayFrameMs += (ms - _displayFrameMs) * 0.05;
        int every = Math.Max(1, (int)Math.Round(1000.0 / 60 / _displayFrameMs));
        if (++_framesSkipped < every) return false;
        _framesSkipped = 0;
        return true;
    }

    /// <summary>
    /// Tells the progress layer where lyrics are on screen so it can fade itself
    /// away underneath them; otherwise bright fills would wash the text out.
    /// </summary>
    void UpdateQuietZones()
    {
        _quietZones.Clear();
        (double Left, double Right)? target = null;
        if (_s.LyricsExpanded && _layerShown)
        {
            // Just the text, so the quiet zone is as long as the line being sung.
            var span = _usingBand ? _band.LyricsScreenSpan() : _widgetShown ? _widget.LyricsScreenSpan() : (0, 0);
            if (span.End - span.Start > 4)
            {
                const double pad = 10;
                var rc = _tracker.Rect;
                double scale = _tracker.Scale;
                // In the seekbar's own units: from the taskbar's left, or up from its bottom when vertical.
                target = _tracker.IsHorizontal
                    ? ((span.Start - rc.Left) / scale - pad, (span.End - rc.Left) / scale + pad)
                    : ((rc.Bottom - span.End) / scale - pad, (rc.Bottom - span.Start) / scale + pad);
            }
        }

        if (target is { } t)
        {
            // Glide to each new line's width instead of jumping.
            _quietShown = _quietShown is { } s ? (Glide(s.Left, t.Left), Glide(s.Right, t.Right)) : t;
            _quietLostAt = DateTime.MinValue;
        }
        else if (_quietShown != null && _s.LyricsExpanded && _layerShown)
        {
            // Between two lines nothing may be drawn for a moment; keep the zone briefly.
            if (_quietLostAt == DateTime.MinValue) _quietLostAt = DateTime.Now;
            if (DateTime.Now - _quietLostAt > TimeSpan.FromSeconds(0.8)) _quietShown = null;
        }
        else
        {
            _quietShown = null;
        }
        if (_quietShown is { } shown) _quietZones.Add(shown);
        _layer.SetQuietZones(_quietZones);
    }

    static double Glide(double from, double to) => Math.Abs(to - from) < 0.5 ? to : from + (to - from) * 0.3;

    // ------------------------------------------------------------------ appearance

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.LayerMode) or nameof(AppSettings.WidgetPlacement) or nameof(AppSettings.WidgetMaxWidth)
                or nameof(AppSettings.PlayerMode)
                or nameof(AppSettings.FaceEnabled) or nameof(AppSettings.FacePlacement) or nameof(AppSettings.FaceScale)
                or nameof(AppSettings.FaceStaysWhenIdle) or nameof(AppSettings.SeekAvoidIcons):
                Reposition();
                break;
            case nameof(AppSettings.TransparentTaskbar):
                UpdateTransparency();
                Reposition();
                break;
            case nameof(AppSettings.ControlCenter) or nameof(AppSettings.WinAOpensControlCenter) or nameof(AppSettings.NotificationDot):
                _controlCenter?.Tick();
                break;
            case nameof(AppSettings.LyricsExpanded) or nameof(AppSettings.LyricsPlacement):
                UpdateLyricsSurface(animate: true);
                break;
            case nameof(AppSettings.ShowInfoPopup):
                if (!_s.ShowInfoPopup) _popup.Dismiss();
                break;
            case nameof(AppSettings.OnlineTrackInfo):
                LoadDetails();
                break;
            case nameof(AppSettings.SeekStyle):
                ApplyAppearance();
                Watch();
                break;
            case nameof(AppSettings.HideWhenIdle) or nameof(AppSettings.AudioReactive):
                Watch();
                break;
            case nameof(AppSettings.LyricsEnabled) or nameof(AppSettings.FetchOnline) or nameof(AppSettings.LyricsFolder):
                if (_media.HasSession)
                {
                    ShowTrackMessage();
                    LoadLyrics(force: false);
                }
                break;
            case nameof(AppSettings.Romanization):
                ApplyAppearance();
                PresentLyrics();
                break;
            case nameof(AppSettings.PinnedAppId) or nameof(AppSettings.StartWithWindows)
                or nameof(AppSettings.ProxyUrl) or nameof(AppSettings.LyricsOffsetMs) or nameof(AppSettings.LyricsDelayMs)
                or nameof(AppSettings.LyricsPanelRomanized) or nameof(AppSettings.KeepListeningStats)
                or nameof(AppSettings.RecapDoneYear) or nameof(AppSettings.RecapAskedOn)
                or nameof(AppSettings.CheckForUpdates) or nameof(AppSettings.SkippedVersion) or nameof(AppSettings.WindowsModeBefore)
                or nameof(AppSettings.FocusMinutes) or nameof(AppSettings.BreakMinutes) or nameof(AppSettings.LyricsFromNetEase):
                break;
            default:
                ApplyAppearance();
                break;
        }
    }

    // Windows' taskbar mode as last read: Auto paints to match it, and a see-through taskbar follows it.
    bool _windowsLight = ColorUtil.WindowsTaskbarIsLight();
    DateTime _themeCheckedAt = DateTime.Now;

    /// <summary>The background actually painted: Auto becomes Dark or Light with Windows' taskbar mode.</summary>
    BarBackground EffectiveBackground => BackgroundFor(_windowsLight);

    BarBackground BackgroundFor(bool windowsLight) => _s.Background switch
    {
        BarBackground.Auto => windowsLight ? BarBackground.Light : BarBackground.Dark,
        BarBackground.AutoTinted => windowsLight ? BarBackground.LightTinted : BarBackground.Tinted,
        _ => _s.Background,
    };

    /// <summary>
    /// The wallpaper shows through behind the bar: the taskbar is see-through, the layer is under it,
    /// and the bar's own background is clear or faint.
    /// </summary>
    bool WallpaperShows => !IsOverMode && (OwnsTransparency || TranslucentTaskbarDetected)
        && (EffectiveBackground == BarBackground.Transparent || _s.BackgroundOpacity < 0.6);

    /// <summary>
    /// Text and lines drawn straight on the taskbar go dark: on a light background, or where a light
    /// wallpaper shows through a see-through taskbar (a faint background mixed in), or, until the
    /// wallpaper is known, on a clear taskbar while Windows is in light mode.
    /// </summary>
    bool LightBar => LightBarFor(_windowsLight, remember: true);

    /// <summary>Whether the bar would look light with Windows' taskbar in the given mode.</summary>
    bool LightBarFor(bool windowsLight, bool remember = false)
    {
        var background = BackgroundFor(windowsLight);
        bool lightBase = background is BarBackground.Light or BarBackground.LightTinted;
        if (WallpaperShows && _backdrop.Lightness is double behind)
        {
            // What the eye sees: the bar's own color over the picture, as solid as its opacity.
            double cover = background == BarBackground.Transparent ? 0 : _s.BackgroundOpacity;
            double seen = (lightBase ? 96 : 4) * cover + behind * (1 - cover);
            // Some give either way, so a moving wallpaper near the middle doesn't flip the text back and forth.
            bool light = _wallpaperLight ? seen > 52 : seen > 62;
            if (remember) _wallpaperLight = light;
            return light;
        }
        return background == BarBackground.Transparent ? windowsLight : lightBase;
    }

    /// <summary>
    /// Windows' own icons on the taskbar (tray, clock, Wi-Fi, volume) follow Windows' mode, not the bar:
    /// the main taskbar's overlay sets the mode to match the bar behind them, judged against the mode
    /// the user chose, so the Auto background still follows the user's Windows.
    /// </summary>
    void SyncWindowsMode()
    {
        if (_windowsMode == null) return;
        if (_disposed || !_showsBar || !_s.MatchWindowsMode) _windowsMode.Release(wait: _disposed);
        else _windowsMode.Want(LightBarFor(_windowsMode.UserLight));
        _windowsLight = ColorUtil.WindowsTaskbarIsLight();
    }

    readonly WallpaperTone _backdrop = new();
    DateTime _backdropCheckedAt = DateTime.MinValue;
    bool _wallpaperLight;

    /// <summary>Picks up a switch between Windows' light and dark mode, and a new wallpaper behind a see-through taskbar.</summary>
    void CheckTheme()
    {
        if (DateTime.Now - _themeCheckedAt < TimeSpan.FromSeconds(2)) return;
        _themeCheckedAt = DateTime.Now;
        if (WallpaperShows && _tracker.Monitor != null && DateTime.Now - _backdropCheckedAt > TimeSpan.FromSeconds(8))
        {
            _backdropCheckedAt = DateTime.Now;
            _backdrop.Check(_tracker.Rect, _tracker.Monitor.Bounds);
        }
        bool light = ColorUtil.WindowsTaskbarIsLight();
        if (light == _windowsLight) return;
        _windowsLight = light;
        Log.Info($"Windows taskbar mode: {(light ? "light" : "dark")}");
        ApplyAppearance();
    }

    void ApplyAppearance()
    {
        SyncWindowsMode();
        double h = _styledThickness > 0 ? _styledThickness : 30;
        bool over = _styledThickness > 0 ? _styledOver : IsOverMode;
        var background = EffectiveBackground;
        _layer.ApplyStyle(_s, over, _styledEdge, background);
        bool shadow = over || background == BarBackground.Transparent || _s.BackgroundOpacity < 0.45;
        _widget.ApplyStyle(_s, h, shadow, Vertical, LightBar);
        _band.ApplyStyle(_s, h, Vertical, LightBar);
        _face.ApplyStyle(_s, _layerShown || !_faceShown ? LightBar : _windowsLight);
        UpdateBadge();
        UpdateAccent(animate: false);
    }

    /// <summary>Colours and cover the settings previews should use, so they look like the real bar.</summary>
    public (Color Primary, Color Secondary, System.Windows.Media.Imaging.BitmapSource? Art)? PreviewPalette()
        => _media.HasSession && _media.Thumbnail != null ? (_currentPrimary, _currentSecondary, _media.Thumbnail) : null;

    void UpdateAccent(bool animate)
    {
        Color? app = _media.HasSession && _media.AppColor is { } c ? ColorUtil.ForDarkSurface(c) : null;
        Palette? art = _media.HasSession ? _artPalette : null;
        Color primary;
        Color secondary;
        switch (_s.AccentSource)
        {
            case AccentSource.AlbumArt when art is { } a:
                (primary, secondary) = (a.Primary, a.Secondary);
                break;
            case AccentSource.AlbumArt when app is { } ac:
                (primary, secondary) = (ac, ColorUtil.ShiftHue(ac, 28, -0.04));
                break;
            case AccentSource.App when app is { } ac:
                (primary, secondary) = (ac, ColorUtil.ShiftHue(ac, 28, -0.04));
                break;
            case AccentSource.App when art is { } a:
                (primary, secondary) = (a.Primary, a.Secondary);
                break;
            case AccentSource.Custom:
                primary = ColorUtil.TryParse(_s.CustomColor, out var custom) ? custom : ColorUtil.Brand;
                secondary = ColorUtil.ShiftHue(primary, 28, -0.04);
                break;
            default:
                primary = ColorUtil.SystemAccent();
                secondary = ColorUtil.ShiftHue(primary, 28, -0.04);
                break;
        }

        var d = animate ? TimeSpan.FromMilliseconds(900) : TimeSpan.Zero;
        if (d > TimeSpan.Zero)
        {
            _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(primary, d)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        else
        {
            _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _accentBrush.Color = primary;
        }
        _layer.SetColors(primary, secondary, animate);
        _currentPrimary = primary;
        _currentSecondary = secondary;
        _playGlyphBrush.Color = ColorUtil.Luminance(primary) > 0.42 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White;
        _widget.SetAccent(primary);
        _popup.SetAccent(primary);
        _controlCenter?.SetAccent(primary);
        _band.SetAccent(primary);
        _face.SetAccent(primary);
        _lyricsPanel?.SetAccent(primary);
    }

    // ------------------------------------------------------------------ media state

    void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaService.IsPlaying):
                _widget.SetPlaying(_media.IsPlaying, animate: true);
                break;
            case nameof(MediaService.CanPlayPause) or nameof(MediaService.CanNext) or nameof(MediaService.CanPrevious):
                UpdateButtons();
                break;
            case nameof(MediaService.HasSession):
                OnSessionStateChanged();
                break;
            case nameof(MediaService.AppIcon) or nameof(MediaService.AppColor):
                UpdateBadge();
                UpdateAccent(animate: true);
                break;
        }
    }

    void OnSessionStateChanged()
    {
        UpdateButtons();
        UpdateBadge();
        _widget.SetPlaying(_media.IsPlaying, animate: false);
        if (!_media.HasSession) ShowIdle();
        Watch();
    }

    void UpdateButtons()
        => _widget.SetButtons(_media.HasSession && _media.CanPrevious, _media.HasSession && _media.CanPlayPause,
            _media.HasSession && _media.CanNext);

    void UpdateBadge() => _widget.SetBadge(_media.HasSession ? _media.AppIcon : null, _s.ShowAppIcon);

    void ShowIdle()
    {
        _lyricsCts?.Cancel();
        _detailsCts?.Cancel();
        _lyricsResult = null;
        _details = null;
        _lyricsStatus = LyricsStatus.None;
        _popup.Dismiss();
        ShowLyricsMessage(Loc.T("Msg_NothingPlaying"), "");
        _widget.SetArt(null);
        UpdateBadge();
        UpdateAccent(animate: true);
        LyricsChanged?.Invoke();
    }

    void OnTrackChanged()
    {
        if (!_media.HasSession)
        {
            ShowIdle();
            return;
        }
        ShowTrackMessage();
        LoadLyrics(force: false);
        LoadDetails();
        _layer.SetTrack($"{_media.AppId}|{_media.Artist}|{_media.Title}");
        if (_popup.IsOpen) _popup.SetContent(BuildCard());
    }

    /// <summary>Messages already on the bar are re-said in the new language; menus pick it up when next built.</summary>
    void OnLanguageChanged()
    {
        if (!_media.HasSession) ShowLyricsMessage(Loc.T("Msg_NothingPlaying"), "");
        else if (_lyricsStatus == LyricsStatus.Instrumental) ShowLyricsMessage(_media.Title, Loc.T("Msg_Instrumental"));
        if (_popup.IsOpen) _popup.SetContent(BuildCard());
        _controlCenter?.RefreshText();
    }

    void ShowTrackMessage()
    {
        var title = _media.Title.Length > 0 ? _media.Title : _media.AppName;
        var artist = _media.Artist.Length > 0 ? _media.Artist : _media.Title.Length > 0 ? _media.AppName : "";
        ShowLyricsMessage(title, artist);
    }

    async void LoadLyrics(bool force, int attempt = 0)
    {
        _lyricsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _lyricsResult = null;
        _lyricsStatus = LyricsStatus.None;
        if (!_s.LyricsEnabled || !_media.HasSession || _media.Title.Length == 0) return;

        _lyricsStatus = LyricsStatus.Loading;
        LyricsChanged?.Invoke();
        if (force) ShowTrackMessage();
        try
        {
            // Let the player finish publishing duration/album before matching.
            await Task.Delay(force ? 0 : 450, cts.Token);
            var query = new TrackQuery(_media.Title, _media.Artist, _media.Album, _media.Duration, _media.AppId);
            var result = await _lyricsService.GetAsync(query, force, cts.Token);
            if (cts.IsCancellationRequested) return;

            _lyricsResult = result;
            _lyricsStatus = result.Status;
            if (result.Status == LyricsStatus.Instrumental)
                ShowLyricsMessage(_media.Title, Loc.T("Msg_Instrumental"));
            PresentLyrics();
            Log.Info($"Lyrics for '{_media.Artist} - {_media.Title}': {result.Status} ({result.Source})");

            // Network trouble or LRCLIB briefly down: try again while the same song keeps playing.
            if (result.Status == LyricsStatus.Error && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cts.Token);
                LoadLyrics(force: false, attempt + 1);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _lyricsStatus = LyricsStatus.Error;
            Log.Error("Loading lyrics failed", ex);
        }
    }

    /// <summary>Genre, year and album from the iTunes catalogue, for the song card.</summary>
    async void LoadDetails()
    {
        _detailsCts?.Cancel();
        _details = null;
        if (!_media.HasSession || _media.IsDemo || _media.Title.Length == 0) return;
        var cts = new CancellationTokenSource();
        _detailsCts = cts;
        try
        {
            await Task.Delay(800, cts.Token);
            var details = await _trackInfo.LookupAsync(_media.Artist, _media.Title, _media.Duration, cts.Token);
            if (cts.IsCancellationRequested) return;
            _details = details;
            if (_popup.IsOpen) _popup.SetContent(BuildCard());
        }
        catch (OperationCanceledException)
        {
        }
    }

    async void OnThumbnailChanged()
    {
        var bmp = _media.Thumbnail;
        _widget.SetArt(bmp);
        _layer.SetArt(bmp);
        UpdateBadge();
        if (_popup.IsOpen) _popup.SetContent(BuildCard());
        _artPalette = null;
        if (bmp != null)
        {
            var palette = await Task.Run(() => ColorExtractor.FromBitmap(bmp));
            if (!ReferenceEquals(bmp, _media.Thumbnail)) return;
            _artPalette = palette;
        }
        UpdateAccent(animate: true);
    }

    // ------------------------------------------------------------------ song card

    void ShowInfo()
    {
        if (!_s.ShowInfoPopup || !_media.HasSession || !_widgetShown || _tracker.Monitor == null) return;
        _popup.SetContent(BuildCard());
        _popup.CanSeek = _media.CanSeek;
        _popup.SetProgress(_media.GetPosition(), _media.Duration);
        _popup.ShowAt(_widget.CoverCenterOnScreen(), _tracker.Edge, _tracker.Rect, _tracker.Monitor.Bounds);
    }

    SongCard BuildCard()
    {
        var d = _details;
        var genres = (_media.Genres.Length > 0 ? _media.Genres : d?.Genre ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        string? track = _media.TrackNumber > 0
            ? _media.AlbumTrackCount > 0 ? Loc.T("Card_TrackOf", _media.TrackNumber, _media.AlbumTrackCount) : Loc.T("Card_Track", _media.TrackNumber)
            : null;
        var album = _media.Album.Length > 0 ? _media.Album : d?.Album ?? "";
        var artist = _media.Artist.Length > 0 ? _media.Artist : d?.Artist ?? "";
        return new SongCard(
            _media.Title.Length > 0 ? _media.Title : _media.AppName,
            artist, album, d?.Year, genres, track, _media.AppIcon, _media.Thumbnail);
    }

    // ------------------------------------------------------------------ context menu

    public void ShowMenu()
    {
        _popup.Dismiss();
        var menu = BuildMenu();
        menu.Placement = PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            // Without foreground the menu would not close when clicking elsewhere.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Native.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("BarContextMenu") };

        if (_media.HasSession)
        {
            var artist = _media.Artist.Length > 0 ? " — " + _media.Artist : "";
            menu.Items.Add(new MenuItem { Header = Shorten(_media.Title + artist, 52), IsEnabled = false, Icon = Glyph("") });
            if (!_media.IsDemo)
            {
                var open = new MenuItem { Header = Loc.T("Menu_OpenApp", _media.AppName), Icon = Glyph("") };
                open.Click += (_, _) => AppVisualResolver.Activate(_media.AppId);
                menu.Items.Add(open);
            }
            menu.Items.Add(new Separator());
        }

        var showLyrics = new MenuItem { Header = Loc.T("Menu_ShowLyrics"), Icon = Glyph(""), IsChecked = _s.LyricsExpanded };
        showLyrics.Click += (_, _) => _s.LyricsExpanded = !_s.LyricsExpanded;
        menu.Items.Add(showLyrics);

        // Hiding the player leaves the bar (and maybe the lyrics); this menu, also on the tray icon, brings it back.
        var player = new MenuItem { Header = Loc.T("Menu_Player"), Icon = Glyph("") };
        foreach (var option in SettingsOptions.PlayerModes)
        {
            var value = (PlayerMode)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.PlayerMode == value };
            item.Click += (_, _) => _s.PlayerMode = value;
            player.Items.Add(item);
        }
        menu.Items.Add(player);

        var face = new MenuItem { Header = Loc.T("Menu_Face"), Icon = Glyph(""), IsChecked = _s.FaceEnabled };
        face.Click += (_, _) => _s.FaceEnabled = !_s.FaceEnabled;
        menu.Items.Add(face);

        var sources = new MenuItem { Header = Loc.T("Menu_Source"), Icon = Glyph("") };
        var auto = new MenuItem { Header = Loc.T("Menu_SourceAuto"), IsChecked = _s.PinnedAppId.Length == 0 };
        auto.Click += (_, _) => _s.PinnedAppId = "";
        sources.Items.Add(auto);
        var sessions = _media.GetSessions();
        if (sessions.Count > 0) sources.Items.Add(new Separator());
        foreach (var session in sessions)
        {
            var label = session.Title.Length > 0 ? $"{session.AppName} — {Shorten(session.Title, 36)}" : session.AppName;
            var item = new MenuItem
            {
                Header = label + (session.IsPlaying ? "   ▶" : ""),
                IsChecked = _s.PinnedAppId == session.AppId,
            };
            var id = session.AppId;
            item.Click += (_, _) => _s.PinnedAppId = id;
            sources.Items.Add(item);
        }
        menu.Items.Add(sources);

        var lyrics = new MenuItem { Header = Loc.T("Menu_Lyrics"), Icon = Glyph("") };
        lyrics.Items.Add(new MenuItem { Header = Loc.T("Menu_LyricsStatus", LyricsStatusText()), IsEnabled = false });
        lyrics.Items.Add(new Separator());
        var reload = new MenuItem { Header = Loc.T("Menu_Reload"), Icon = Glyph(""), IsEnabled = _media.HasSession && !_media.IsDemo };
        reload.Click += (_, _) => LoadLyrics(force: true);
        var panel = new MenuItem { Header = Loc.T("Menu_LyricsPanel"), Icon = Glyph(""), IsEnabled = _media.HasSession };
        panel.Click += (_, _) => ShowLyricsPanel();
        lyrics.Items.Insert(2, panel);
        lyrics.Items.Add(reload);

        bool synced = _lyricsResult?.Lyrics != null;
        int offset = synced ? (int)_lyricsResult!.Lyrics!.Offset.TotalMilliseconds : 0;
        var earlier = new MenuItem { Header = Loc.T("Menu_Earlier"), Icon = Glyph(""), IsEnabled = synced };
        earlier.Click += (_, _) => NudgeOffset(+250);
        var later = new MenuItem { Header = Loc.T("Menu_Later"), Icon = Glyph(""), IsEnabled = synced };
        later.Click += (_, _) => NudgeOffset(-250);
        var reset = new MenuItem { Header = Loc.T("Menu_ResetOffset", offset.ToString("+0;-0;0")), IsEnabled = synced && offset != 0 };
        reset.Click += (_, _) => NudgeOffset(-offset);
        lyrics.Items.Add(earlier);
        lyrics.Items.Add(later);
        lyrics.Items.Add(reset);
        lyrics.Items.Add(new Separator());
        var folder = new MenuItem { Header = Loc.T("Menu_LyricsFolder"), Icon = Glyph("") };
        folder.Click += (_, _) => OpenLyricsFolder();
        lyrics.Items.Add(folder);
        menu.Items.Add(lyrics);

        var effects = new MenuItem { Header = Loc.T("Menu_LyricEffect"), Icon = Glyph("") };
        foreach (var option in SettingsOptions.LyricEffects)
        {
            var value = (LyricEffect)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.LyricEffect == value };
            item.Click += (_, _) =>
            {
                _s.LyricEffect = value;
                _widget.Dispatcher.BeginInvoke(PreviewLyrics, DispatcherPriority.Background);
            };
            effects.Items.Add(item);
        }
        menu.Items.Add(effects);

        var seek = new MenuItem { Header = Loc.T("Menu_SeekStyle"), Icon = Glyph("") };
        // One submenu per group, the group holding the look that's on ticked.
        foreach (var (name, options) in new[]
                 {
                     ("Set_Progress_TabProgress", SettingsOptions.SeekStyles),
                     ("Set_Progress_GroupVisualizer", SettingsOptions.Visualizers),
                     ("Set_Progress_GroupLed", SettingsOptions.LedStrips),
                 })
        {
            var group = new MenuItem { Header = Loc.T(name), IsChecked = _s.Skin.Length == 0 && options.Any(o => o.Value == _s.SeekStyle) };
            foreach (var option in options)
            {
                var item = new MenuItem { Header = option.Label, IsChecked = _s.Skin.Length == 0 && _s.SeekStyle == option.Value };
                var value = option.Value;
                item.Click += (_, _) =>
                {
                    _s.SeekStyle = value;
                    _s.Skin = "";
                };
                group.Items.Add(item);
            }
            seek.Items.Add(group);
        }
        seek.Items.Add(new Separator());
        foreach (var option in SettingsOptions.SeekDirections)
        {
            var value = (SeekDirection)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.SeekDirection == value };
            item.Click += (_, _) => _s.SeekDirection = value;
            seek.Items.Add(item);
        }
        menu.Items.Add(seek);

        var controls = new MenuItem { Header = Loc.T("Menu_ControlStyle"), Icon = Glyph("") };
        foreach (var option in SettingsOptions.ControlStyles)
        {
            var value = (ControlStyle)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.ControlStyle == value };
            item.Click += (_, _) => _s.ControlStyle = value;
            controls.Items.Add(item);
        }
        menu.Items.Add(controls);

        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = Loc.T("Menu_Settings"), Icon = Glyph("") };
        settings.Click += (_, _) => App.Instance.ShowSettings();
        var recap = new MenuItem { Header = Loc.T("Menu_Recap"), Icon = Glyph("") };
        recap.Click += (_, _) => App.Instance.ShowRecap();
        menu.Items.Add(recap);
        menu.Items.Add(settings);
        var exit = new MenuItem { Header = Loc.T("Menu_Exit"), Icon = Glyph("") };
        exit.Click += (_, _) => App.Instance.ExitApp();
        menu.Items.Add(exit);
        return menu;
    }

    static TextBlock Glyph(string glyph) => new() { Text = glyph };

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    string LyricsStatusText() => _lyricsStatus switch
    {
        LyricsStatus.Loading => Loc.T("LyrSt_Loading"),
        LyricsStatus.Synced => _lyricsResult?.Source switch
        {
            "LRCLIB" => Loc.T("LyrSt_Downloaded"),
            "NetEase" => Loc.T("LyrSt_NetEase"),
            "demo" => Loc.T("LyrSt_Demo"),
            _ => Loc.T("LyrSt_Cached"),
        },
        LyricsStatus.PlainOnly => Loc.T("LyrSt_PlainOnly"),
        LyricsStatus.Instrumental => Loc.T("LyrSt_Instrumental"),
        LyricsStatus.NotFound => Loc.T("LyrSt_NotFound"),
        LyricsStatus.Error => Loc.T("LyrSt_Error"),
        _ => _s.LyricsEnabled ? Loc.T("LyrSt_None") : Loc.T("LyrSt_Off"),
    };

    void NudgeOffset(int deltaMs)
    {
        if (_lyricsResult?.Lyrics is not { } lyrics) return;
        _lyricsService.SaveOffset(_lyricsResult, (int)lyrics.Offset.TotalMilliseconds + deltaMs);
    }

    void OpenLyricsFolder()
    {
        try
        {
            Directory.CreateDirectory(_lyricsService.Folder);
            var file = _lyricsResult?.FilePath;
            var args = file != null && File.Exists(file) ? $"/select,\"{file}\"" : $"\"{_lyricsService.Folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Opening lyrics folder failed", ex);
        }
    }
}
