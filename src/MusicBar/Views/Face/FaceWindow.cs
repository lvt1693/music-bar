using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views.Face;

/// <summary>
/// The face's own little window on the taskbar. It never takes focus, runs the face on a
/// paced clock (slower while it sleeps), tells it about the music, the clock, the pointer, the
/// computer and whether the user is around, keeps the focus timer, takes it for a stroll along
/// the empty taskbar now and then, and offers a menu on right-click.
/// </summary>
public sealed class FaceWindow : Window
{
    static readonly TimeSpan Lively = TimeSpan.FromMilliseconds(33);
    static readonly TimeSpan Sleeping = TimeSpan.FromMilliseconds(80);

    readonly AppSettings _s;
    readonly MediaService _media;
    readonly AudioMeter _audio;
    readonly FocusTimer _focus = FocusTimer.Current;
    readonly FaceView _view = new(Environment.TickCount);
    readonly DropShadowEffect _shadow = new() { ShadowDepth = 0, BlurRadius = 5, Opacity = 0.55, Color = Colors.Black };
    readonly DispatcherTimer _clock = new(DispatcherPriority.Render) { Interval = Lively };
    BubbleWindow? _bubble;

    // Where it lives on the taskbar, how far it may wander from there (screen pixels along the
    // taskbar) and where it has wandered to.
    Native.RECT _home;
    int _roamStart, _roamEnd;
    bool _horizontal = true;
    TaskbarEdge _edge = TaskbarEdge.Bottom;
    Native.RECT _taskbar, _monitor;
    double _offset, _strollTo, _pauseUntil, _lastTick = -1;
    Stroll _stroll;

    enum Stroll { None, Out, Look, Back }

    public FaceWindow(AppSettings settings, MediaService media, AudioMeter audio)
    {
        CpuRendering.Use(this);
        _s = settings;
        _media = media;
        _audio = audio;
        Title = "MusicBar Face";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Left = Top = -20000;
        Width = 64;
        Height = 48;
        UseLayoutRounding = true;
        _view.Effect = _shadow;
        Content = _view;

        _clock.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _clock.Start();
            else
            {
                _clock.Stop();
                _audio.Want(this, false);
                _bubble?.Hide();
            }
        };
        _media.TrackChanged += OnTrackChanged;
        _focus.Changed += OnFocusChanged;
        MouseRightButtonUp += (_, e) =>
        {
            ShowMenu();
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(Hwnd).AddHook(NoActivate);
        };
        Closed += (_, _) =>
        {
            _media.TrackChanged -= OnTrackChanged;
            _focus.Changed -= OnFocusChanged;
            _audio.Want(this, false);
            _bubble?.Close();
        };
    }

    public IntPtr Hwnd { get; private set; }

    void OnTrackChanged(object? sender, EventArgs e) => _view.Brain.TrackChanged(_view.Now);

    static IntPtr NoActivate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    /// <param name="lightBar">The taskbar behind is light: lines go dark and the halo light.</param>
    public void ApplyStyle(AppSettings s, bool lightBar)
    {
        _view.Look = s.FaceStyle;
        // The user's pictures, read again whenever they change them (FacePackStamp moves).
        if (s.FaceStyle != FaceStyle.Custom) _view.Pack = null;
        else if (_view.Pack == null || _packStamp != s.FacePackStamp) _view.Pack = new Services.FacePack();
        _packStamp = s.FacePackStamp;
        _view.Ink = lightBar ? LyricStyle.Ink : Color.FromRgb(0xFF, 0xFD, 0xF6);
        _shadow.Color = lightBar ? Colors.White : Colors.Black;
        _view.Brain.FollowsClock = s.FaceFollowsClock;
        _view.Brain.ReactsToMusic = s.FaceReactsToMusic;
        _view.Brain.HoldsMic = s.FaceHoldsMic;
    }

    long _packStamp;

    public void SetAccent(Color accent) => _view.Accent = accent;

    /// <summary>What the face wears today: the season's hat when it is that time of year (and allowed), otherwise the user's.</summary>
    public static FaceOutfit OutfitFor(AppSettings s, DateTime now) => new()
    {
        Hat = s.FaceSeasonal && FaceSeasons.HatOn(now) is { } seasonal ? seasonal : s.FaceHat,
        Glasses = s.FaceGlasses,
        Headphones = s.FaceHeadphones,
    };

    // ------------------------------------------------------------------ place and stroll

    /// <summary>
    /// Puts the face at its spot on the taskbar (screen pixels) and tells it how far along the
    /// taskbar it may wander: <paramref name="roamStart"/> to <paramref name="roamEnd"/>.
    /// </summary>
    internal void Place(Native.RECT home, int roamStart, int roamEnd, TaskbarEdge edge, Native.RECT taskbar, Native.RECT monitor)
    {
        _home = home;
        _roamStart = roamStart;
        _roamEnd = roamEnd;
        _horizontal = !TaskbarSide.IsVertical(edge);
        _edge = edge;
        _taskbar = taskbar;
        _monitor = monitor;
        var (min, max) = Room();
        _offset = Math.Clamp(_offset, min, max);
        _strollTo = Math.Clamp(_strollTo, min, max);
        Move();
    }

    void Move()
    {
        int d = (int)Math.Round(_offset);
        int x = _home.Left + (_horizontal ? d : 0), y = _home.Top + (_horizontal ? 0 : d);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, _home.Width, _home.Height, Native.SWP_NOACTIVATE);
    }

    double Length => _horizontal ? _home.Width : _home.Height;

    /// <summary>How far it may go each way from home, in pixels: the empty stretch around it, at most a few face lengths.</summary>
    (double Min, double Max) Room()
    {
        int start = _horizontal ? _home.Left : _home.Top, end = _horizontal ? _home.Right : _home.Bottom;
        double min = Math.Min(0, Math.Max(_roamStart - start, -6 * Length));
        double max = Math.Max(0, Math.Min(_roamEnd - end, 6 * Length));
        return (min, max);
    }

    /// <summary>Out a few face lengths, a look around, and back home; cut short by music or a hand.</summary>
    void Walk(double now, bool playing)
    {
        double dt = _lastTick < 0 ? 0 : Math.Min(now - _lastTick, 0.1);
        _lastTick = now;
        var brain = _view.Brain;
        var (min, max) = Room();
        bool interrupted = playing || _view.IsMouseOver || !_s.FaceStrolls;

        if (_stroll == Stroll.None)
        {
            if (interrupted || Length <= 0 || !brain.WantsStroll(now)) return;
            // The side with more room, at least a face and a half away.
            double far = Math.Max(-min, max);
            if (far < Length * 1.5) return;
            double side = max >= -min ? 1 : -1;
            _strollTo = side * (Length * 1.5 + Random.Shared.NextDouble() * (far - Length * 1.5));
            _stroll = Stroll.Out;
        }
        if (interrupted && _stroll != Stroll.Back)
        {
            _stroll = Stroll.Back;
            _strollTo = 0;
            brain.StrollOver(now);
        }
        if (_stroll == Stroll.Look)
        {
            if (now < _pauseUntil) return;
            _stroll = Stroll.Back;
            _strollTo = 0;
        }

        double speed = Length * (interrupted ? 2.4 : 1.1);
        double gap = _strollTo - _offset;
        double step = Math.Sign(gap) * Math.Min(Math.Abs(gap), speed * dt);
        _offset += step;
        brain.Walking(now, Math.Abs(gap) < 0.5 ? 0 : Math.Sign(gap));
        Move();
        if (Math.Abs(_strollTo - _offset) >= 0.5) return;
        _offset = _strollTo;
        brain.Walking(now, 0);
        if (_stroll == Stroll.Out)
        {
            _stroll = Stroll.Look;
            _pauseUntil = now + 2.5 + Random.Shared.NextDouble() * 3;
        }
        else
        {
            _stroll = Stroll.None;
        }
    }

    // ------------------------------------------------------------------ the clock

    void Tick()
    {
        bool playing = _media.HasSession && _media.IsPlaying;
        _audio.Want(this, _s.FaceReactsToMusic && playing);
        double level = _audio.IsCapturing ? _audio.Level : -1;

        Vector? pointer = null;
        if (_s.FaceWatchesPointer && Native.GetCursorPos(out var p) && PresentationSource.FromVisual(_view) != null)
        {
            var local = _view.PointFromScreen(new Point(p.X, p.Y));
            var from = local - new Point(_view.ActualWidth / 2, _view.ActualHeight * 0.45);
            var d = from / Math.Max(1, _view.ActualWidth);
            // Only a pointer within a few face widths is worth watching.
            if (d.Length < 5) pointer = d;
        }

        var phase = _focus.Phase;
        _view.Outfit = OutfitFor(_s, DateTime.Now) with
        {
            Timer = phase == FocusPhase.Off ? null : _focus.Left,
            TimerBreak = phase == FocusPhase.Break,
        };
        _view.Step(new FaceSenses(DateTime.Now, _media.HasSession, playing, level, Native.UserIdle(), pointer, _view.IsMouseOver)
        {
            System = _s.FaceReactsToSystem ? SystemSense.Read() : null,
            Focus = phase,
        });
        Walk(_view.Now, playing);
        bool calm = _view.Brain.Calm && !_view.IsMouseOver && _stroll == Stroll.None && phase == FocusPhase.Off;
        var pace = calm ? Sleeping : Lively;
        if (_clock.Interval != pace) _clock.Interval = pace;
    }

    // ------------------------------------------------------------------ focus timer

    void OnFocusChanged(FocusPhase from, FocusPhase to)
    {
        _view.Brain.FocusChanged(_view.Now, from, to);
        string? text = to switch
        {
            FocusPhase.Focus when from == FocusPhase.Break => Loc.T("Face_Focus_Back", _s.FocusMinutes),
            FocusPhase.Focus => Loc.T("Face_Focus_Go", _s.FocusMinutes),
            FocusPhase.Break when _focus.LongBreak => Loc.T("Face_Focus_LongBreak", (int)_focus.Length.TotalMinutes),
            FocusPhase.Break => Loc.T(_focus.Rounds % 2 == 0 ? "Face_Focus_BreakWater" : "Face_Focus_BreakStretch", (int)_focus.Length.TotalMinutes),
            _ => null,
        };
        if (text != null) Say(text, to == FocusPhase.Break ? "\uE76E" : "\uE916", to == FocusPhase.Break ? 9 : 5);
    }

    /// <summary>A speech bubble above the face (beside it on a vertical taskbar).</summary>
    void Say(string text, string glyph, double seconds)
    {
        if (!IsVisible || Hwnd == IntPtr.Zero || _taskbar.Width <= 0) return;
        _bubble ??= new BubbleWindow();
        int d = (int)Math.Round(_offset);
        var center = new Point(_home.Left + (_horizontal ? d : 0) + _home.Width / 2.0, _home.Top + (_horizontal ? 0 : d) + _home.Height / 2.0);
        _bubble.Say(text, glyph, ColorUtil.ForDarkSurface(_view.Accent), center, _edge, _taskbar, _monitor, seconds);
    }

    // ------------------------------------------------------------------ menu

    void ShowMenu()
    {
        var menu = new ContextMenu { Style = Application.Current?.TryFindResource("BarContextMenu") as Style };

        // The focus timer first: it is what a right-click on the face is most often for.
        if (_focus.Phase == FocusPhase.Off)
        {
            var start = new MenuItem { Header = Loc.T("Face_Focus_Start", _s.FocusMinutes) };
            start.Click += (_, _) => _focus.Start(_s);
            menu.Items.Add(start);
        }
        else
        {
            var left = _focus.Remaining;
            var key = _focus.Phase == FocusPhase.Focus ? "Face_Focus_Left" : "Face_Focus_BreakLeft";
            menu.Items.Add(new MenuItem { Header = Loc.T(key, $"{(int)left.TotalMinutes}:{left.Seconds:00}"), IsEnabled = false });
            var skip = new MenuItem { Header = Loc.T(_focus.Phase == FocusPhase.Focus ? "Face_Focus_SkipToBreak" : "Face_Focus_SkipBreak") };
            skip.Click += (_, _) => _focus.Skip();
            menu.Items.Add(skip);
            var stop = new MenuItem { Header = Loc.T("Face_Focus_Stop") };
            stop.Click += (_, _) => _focus.Stop();
            menu.Items.Add(stop);
        }
        menu.Items.Add(new Separator());

        var styles = new MenuItem { Header = Loc.T("Face_Menu_Style") };
        foreach (var option in SettingsOptions.FaceStyles)
        {
            var value = option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.FaceStyle == value };
            item.Click += (_, _) => _s.FaceStyle = value;
            styles.Items.Add(item);
        }
        menu.Items.Add(styles);

        var wear = new MenuItem { Header = Loc.T("Face_Menu_Wear") };
        foreach (var option in SettingsOptions.FaceHats)
        {
            var value = (FaceHat)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.FaceHat == value };
            item.Click += (_, _) => _s.FaceHat = value;
            wear.Items.Add(item);
        }
        wear.Items.Add(new Separator());
        foreach (var option in SettingsOptions.FaceGlasses)
        {
            var value = (FaceGlasses)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.FaceGlasses == value };
            item.Click += (_, _) => _s.FaceGlasses = value;
            wear.Items.Add(item);
        }
        wear.Items.Add(new Separator());
        wear.Items.Add(Toggle("Set_FaceHeadphones", _s.FaceHeadphones, v => _s.FaceHeadphones = v));
        wear.Items.Add(Toggle("Set_FaceMic", _s.FaceHoldsMic, v => _s.FaceHoldsMic = v));
        wear.Items.Add(Toggle("Set_FaceSeasonal", _s.FaceSeasonal, v => _s.FaceSeasonal = v));
        menu.Items.Add(wear);

        menu.Items.Add(new Separator());
        var hide = new MenuItem { Header = Loc.T("Face_Hide") };
        hide.Click += (_, _) => _s.FaceEnabled = false;
        menu.Items.Add(hide);
        var settings = new MenuItem { Header = Loc.T("Face_Settings") };
        settings.Click += (_, _) => (Application.Current as App)?.ShowSettings(SettingsWindow.FacePage);
        menu.Items.Add(settings);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            // Without foreground the menu would not close when clicking elsewhere.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Native.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    static MenuItem Toggle(string key, bool on, Action<bool> set)
    {
        var item = new MenuItem { Header = Loc.T(key), IsChecked = on };
        item.Click += (_, _) => set(!on);
        return item;
    }
}
