using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// MusicBar's control center: notifications on top, volume and the playback
/// device below. The device list slides out to the left of the panel instead
/// of replacing it, so there is no going in and back out.
/// </summary>
public partial class ControlCenterWindow : Window
{
    const double Gap = 12;
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    readonly NotificationFeed _feed;
    readonly AudioOutputs _audio;
    readonly DispatcherTimer _volumePoll = new() { Interval = TimeSpan.FromMilliseconds(300) };
    int _token;
    bool _syncing;
    // Wheel travel over the volume row not yet a whole step (a touchpad scrolls in small bits).
    int _wheel;
    enum SideKind { None, Outputs, Project, Cast, WiFi, Bluetooth }

    SideKind _side;
    int _sideToken;
    string? _defaultId;
    TaskbarEdge _edge = TaskbarEdge.Bottom;
    Native.RECT _taskbar, _monitor;
    bool _mirrored;
    Color _accent = ColorUtil.Brand;
    readonly System.Collections.Generic.List<Action> _tilePainters = [];

    public ControlCenterWindow(NotificationFeed feed, AudioOutputs audio)
    {
        CpuRendering.Use(this);
        InitializeComponent();
        _feed = feed;
        _audio = audio;
        _feed.Changed += () =>
        {
            if (IsOpen) RenderNotifications();
        };
        _volumePoll.Tick += (_, _) => SyncVolume();
        Closed += (_, _) => _brightness.Dispose();
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW, Native.WS_EX_APPWINDOW);
        };
        Deactivated += (_, _) => Dismiss();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_side != SideKind.None) SetSide(SideKind.None);
            else Dismiss();
            e.Handled = true;
        };
        // Growing (a new notification, the tiles arriving) keeps the panel's edge where it is.
        SizeChanged += (_, _) =>
        {
            if (!IsOpen) return;
            FitToScreen();
            Place();
        };
    }

    public IntPtr Hwnd { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>When the panel last closed; a click on the tray button right after is the same click.</summary>
    public DateTime ClosedAt { get; private set; } = DateTime.MinValue;

    /// <summary>The playback device was switched.</summary>
    public event Action? OutputChanged;

    public void SetAccent(Color accent)
    {
        _accent = accent;
        Resources["Accent"] = new SolidColorBrush(accent);
        Resources["OnAccent"] = new SolidColorBrush(ColorUtil.Luminance(accent) > 0.42 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White);
        foreach (var paint in _tilePainters) paint();
        NotificationWash.Color = ColorUtil.WithAlpha(accent, 0.16);
        CountText.Foreground = new SolidColorBrush(ColorUtil.Luminance(accent) > 0.42 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White);
    }

    /// <summary>
    /// Opens the panel beside the far end of the taskbar, where the tray is: above or below a
    /// horizontal taskbar at the screen's right, beside a vertical one at the screen's bottom.
    /// </summary>
    internal void Open(TaskbarEdge edge, Native.RECT taskbar, Native.RECT monitorBounds)
    {
        _token++;
        _edge = edge;
        _taskbar = taskbar;
        _monitor = monitorBounds;
        Mirror(edge == TaskbarEdge.Left);
        // The cards hug the taskbar's side of the window: its bottom, or its top under a top taskbar.
        Main.VerticalAlignment = Side.VerticalAlignment = edge == TaskbarEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        IsOpen = true;
        NotificationScroll.MaxHeight = Math.Max(160, monitorBounds.Height / Scale() * 0.5);
        _fitted = false;
        SetSide(SideKind.None, animate: false);
        SyncVolume();
        _ = SyncBrightnessAsync();
        SyncOutput();
        RenderTiles();
        RenderNotifications();
        _feed.MarkAllSeen();
        _ = _feed.RefreshAsync();

        if (!IsVisible) Show();
        UpdateLayout();
        FitToScreen();
        Place();
        Activate();
        _volumePoll.Start();

        var d = TimeSpan.FromMilliseconds(260);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        var (axis, toward) = TaskbarSide.Slide(edge);
        MainShift.BeginAnimation(axis == TranslateTransform.XProperty ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
        MainShift.BeginAnimation(axis, new DoubleAnimation(toward * 18, 0, d) { EasingFunction = EaseOut });
    }

    public void Dismiss()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ClosedAt = DateTime.Now;
        _volumePoll.Stop();
        LeaveWireless();
        int token = ++_token;
        var d = TimeSpan.FromMilliseconds(140);
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) =>
        {
            if (token == _token) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        var (axis, toward) = TaskbarSide.Slide(_edge);
        MainShift.BeginAnimation(axis, new DoubleAnimation(toward * 8, d) { EasingFunction = EaseOut });
    }

    double Scale() => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11
                      ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>
    /// The window keeps a column free for the side lists. Beside a taskbar on the screen's left the
    /// panel is pinned by its left edge, so the controls take the left column (next to the taskbar)
    /// and the lists slide out to their right instead.
    /// </summary>
    void Mirror(bool mirrored)
    {
        if (mirrored == _mirrored) return;
        _mirrored = mirrored;
        var columns = Root.ColumnDefinitions;
        (columns[0].Width, columns[2].Width) = (columns[2].Width, columns[0].Width);
        Grid.SetColumn(Main, mirrored ? 0 : 2);
        Grid.SetColumn(Side, mirrored ? 2 : 0);
    }

    bool _fitted;

    /// <summary>
    /// Keeps the whole panel on the screen: on a short screen, or with many notifications, the
    /// notification list gives up height (it scrolls) instead of pushing the panel past the top.
    /// </summary>
    void FitToScreen()
    {
        double s = Scale();
        double screen = _monitor.Height / s;
        double taskbar = TaskbarSide.IsVertical(_edge) ? 0 : (_taskbar.Bottom - _taskbar.Top) / s;
        double room = screen - taskbar - 2 * Gap - Root.Margin.Top - Root.Margin.Bottom;
        // Everything in the column except the list itself.
        double rest = Main.ActualHeight - NotificationScroll.ActualHeight;
        double max = Math.Clamp(room - rest, 120, Math.Max(160, screen * 0.5));
        if (_fitted && Math.Abs(max - NotificationScroll.MaxHeight) < 1) return;
        _fitted = true;
        NotificationScroll.MaxHeight = max;
    }

    /// <summary>How far a closed side list sits tucked toward the controls.</summary>
    double SideTuck => _mirrored ? -28 : 28;

    /// <summary>Keeps the main card's outer corner pinned next to the taskbar's end.</summary>
    void Place()
    {
        if (Hwnd == IntPtr.Zero) return;
        double s = Scale();
        int w = (int)Math.Round(ActualWidth * s);
        int h = (int)Math.Round(ActualHeight * s);
        int gap = (int)Math.Round(Gap * s);
        var m = Root.Margin;
        // The window's margin is shadow room: the card itself sits the gap away.
        int left = (int)Math.Round(m.Left * s), top = (int)Math.Round(m.Top * s);
        int right = (int)Math.Round(m.Right * s), bottom = (int)Math.Round(m.Bottom * s);
        int x = _edge switch
        {
            TaskbarEdge.Left => _taskbar.Right + gap - left,
            TaskbarEdge.Right => _taskbar.Left - gap + right - w,
            _ => _monitor.Right - gap + right - w,
        };
        int y = _edge switch
        {
            TaskbarEdge.Top => _taskbar.Bottom + gap - top,
            TaskbarEdge.Bottom => _taskbar.Top - gap + bottom - h,
            _ => _monitor.Bottom - gap + bottom - h,
        };
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE);
    }

    // ------------------------------------------------------------------ volume and device

    void SyncVolume()
    {
        if (VolumeSlider.IsMouseCaptureWithin) return;
        float? level = _audio.Volume;
        bool muted = _audio.Muted;
        _syncing = true;
        VolumeSlider.IsEnabled = MuteButton.IsEnabled = level != null;
        VolumeSlider.Value = Math.Round((level ?? 0) * 100);
        _syncing = false;
        ShowVolume(VolumeSlider.Value, muted);

        if (IsOpen && _audio.Default()?.Id is { } id && id != _defaultId) SyncOutput();
    }

    void ShowVolume(double value, bool muted)
    {
        VolumeText.Text = ((int)Math.Round(value)).ToString();
        VolumeGlyph.Text = muted ? "" : value < 1 ? "" : value < 34 ? "" : value < 67 ? "" : "";
        VolumeSlider.Opacity = muted ? 0.45 : 1;
    }

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        // Moving the slider means "I want to hear it", as in the Windows flyout.
        if (_audio.Muted) _audio.Muted = false;
        _audio.Volume = (float)(e.NewValue / 100);
        ShowVolume(e.NewValue, false);
    }

    /// <summary>The wheel over the volume row: 2 a notch, as over Windows' own volume flyout.</summary>
    void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (!VolumeSlider.IsEnabled) return;
        _wheel += e.Delta;
        int notches = _wheel / 120;
        if (notches == 0) return;
        _wheel -= notches * 120;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + notches * 2, 0, 100);
    }

    // ------------------------------------------------------------------ brightness

    readonly ScreenBrightness _brightness = new();
    bool _brightnessSyncing;
    int _brightnessWheel;

    /// <summary>Reads the screen's brightness off the UI thread; the row shows once something can be dimmed.</summary>
    async System.Threading.Tasks.Task SyncBrightnessAsync()
    {
        int token = _token;
        var monitor = Monitors.All().FirstOrDefault(m => m.Bounds.Same(_monitor))?.Handle ?? IntPtr.Zero;
        int? level = await _brightness.ReadAsync(monitor);
        if (token != _token || !IsOpen || BrightnessSlider.IsMouseCaptureWithin) return;
        var shown = level != null ? Visibility.Visible : Visibility.Collapsed;
        if (level is int value)
        {
            _brightnessSyncing = true;
            BrightnessSlider.Value = value;
            _brightnessSyncing = false;
            ShowBrightness(value);
        }
        if (BrightnessRow.Visibility == shown) return;
        BrightnessRow.Visibility = shown;
        UpdateLayout();
        FitToScreen();
        Place();
    }

    void ShowBrightness(double value)
    {
        BrightnessText.Text = ((int)Math.Round(value)).ToString();
        // The sun's rays turn a little with the level.
        BrightnessTurn.Angle = value * 0.9;
    }

    void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_brightnessSyncing) return;
        _brightness.Set((int)Math.Round(e.NewValue));
        ShowBrightness(e.NewValue);
    }

    /// <summary>The wheel over the brightness row: 2 a notch, like the volume.</summary>
    void Brightness_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _brightnessWheel += e.Delta;
        int notches = _brightnessWheel / 120;
        if (notches == 0) return;
        _brightnessWheel -= notches * 120;
        BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + notches * 2, 0, 100);
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        _audio.Muted = !_audio.Muted;
        ShowVolume(VolumeSlider.Value, _audio.Muted);
    }

    void SyncOutput()
    {
        var current = _audio.Default();
        _defaultId = current?.Id;
        OutputName.Text = current?.Name ?? Loc.T("CC_NoOutput");
        OutputAdapter.Text = current?.Adapter ?? "";
        OutputAdapter.Visibility = OutputAdapter.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputGlyph.Text = Glyph(current?.Kind ?? AudioFormFactor.Speakers);
        if (_side == SideKind.Outputs) RenderOutputs();
    }

    static string Glyph(AudioFormFactor kind) => kind switch
    {
        AudioFormFactor.Headphones or AudioFormFactor.Headset or AudioFormFactor.Handset => "",
        AudioFormFactor.DigitalDisplay => "",
        _ => "",
    };

    void Output_Click(object sender, RoutedEventArgs e) => SetSide(_side == SideKind.Outputs ? SideKind.None : SideKind.Outputs);

    /// <summary>
    /// Slides a list out beside the panel (or back in): playback devices, display modes, wireless
    /// displays, Wi-Fi networks or Bluetooth devices.
    /// </summary>
    void SetSide(SideKind kind, bool animate = true)
    {
        if (kind != _side) LeaveWireless();
        _side = kind;
        _sideToken++;
        bool open = kind != SideKind.None;
        switch (kind)
        {
            case SideKind.Outputs: RenderOutputs(); break;
            case SideKind.Project: RenderProject(); break;
            case SideKind.Cast: RenderCast(); break;
            case SideKind.WiFi: ShowWifi(); break;
            case SideKind.Bluetooth: ShowBluetooth(); break;
        }
        Side.IsHitTestVisible = open;
        OutputChevron.Text = kind == SideKind.Outputs ? "" : "";
        var d = TimeSpan.FromMilliseconds(animate ? 220 : 0);
        Side.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, animate ? TimeSpan.FromMilliseconds(open ? 180 : 120) : d));
        SideShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(open ? 0 : SideTuck, d) { EasingFunction = EaseOut });
    }

    void SideHeader(string title)
    {
        SideTitle.Text = title;
        SideList.Children.Clear();
        SideTools.Children.Clear();
        SideFooter.Children.Clear();
    }

    TextBlock SideMessage(string text)
    {
        var message = new TextBlock { Text = text, FontSize = 12, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 2, 10, 8) };
        SideList.Children.Add(message);
        return message;
    }

    /// <summary>
    /// One entry of a side list: icon, name, optional detail, and a check on the current one.
    /// <paramref name="menu"/> builds its right-click menu.
    /// </summary>
    Button SideRow(string glyph, string title, string? detail, bool current, Action click, Func<ContextMenu>? menu = null)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Opacity = current ? 1 : 0.75,
        };
        if (current) icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(detail))
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.55, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        if (current)
        {
            var check = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            };
            check.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            Grid.SetColumn(check, 2);
            row.Children.Add(check);
        }

        var button = new Button { Style = (Style)FindResource("RowButton"), Content = row, Margin = new Thickness(0, 0, 0, 2) };
        button.Click += (_, _) => click();
        if (menu != null)
        {
            button.MouseRightButtonUp += (_, e) =>
            {
                var m = menu();
                m.PlacementTarget = button;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                m.IsOpen = true;
                e.Handled = true;
            };
        }
        SideList.Children.Add(button);
        return button;
    }

    void RenderOutputs()
    {
        SideHeader(Loc.T("CC_Outputs"));
        var outputs = _audio.List();
        if (outputs.Count == 0)
        {
            SideMessage(Loc.T("CC_NoOutputs"));
            return;
        }
        foreach (var output in outputs.OrderBy(o => o.Name))
        {
            var id = output.Id;
            SideRow(Glyph(output.Kind), output.Name, output.Adapter, id == _defaultId, () =>
            {
                if (id != _defaultId && _audio.SetDefault(id))
                {
                    SyncOutput();
                    SyncVolume();
                    OutputChanged?.Invoke();
                }
                SetSide(SideKind.None);
            });
        }
    }

    // Title and detail are string keys.
    static readonly (ProjectMode Mode, string Glyph, string Title, string Detail)[] ProjectModes =
    [
        (ProjectMode.PcScreenOnly, "", "CC_PcOnly", "CC_PcOnly_Desc"),
        (ProjectMode.Duplicate, "", "CC_Duplicate", "CC_Duplicate_Desc"),
        (ProjectMode.Extend, "", "CC_Extend", "CC_Extend_Desc"),
        (ProjectMode.SecondScreenOnly, "", "CC_SecondOnly", "CC_SecondOnly_Desc"),
    ];

    void RenderProject()
    {
        SideHeader(Loc.T("CC_Project"));
        var current = DisplayProjection.Current();
        foreach (var (mode, glyph, title, detail) in ProjectModes)
        {
            var m = mode;
            SideRow(glyph, Loc.T(title), Loc.T(detail), m == current, () =>
            {
                if (m != DisplayProjection.Current() && !DisplayProjection.Set(m))
                {
                    RenderProject();
                    SideMessage(Loc.T("CC_ProjectFailed"));
                    return;
                }
                SetSide(SideKind.None);
            });
        }
    }

    async void RenderCast(string? note = null)
    {
        int token = _sideToken;
        SideHeader(Loc.T("CC_Cast"));
        if (note != null) SideMessage(note);
        if (WirelessDisplays.Connected)
        {
            SideRow("", Loc.T("CC_Disconnect"), Loc.T("CC_Casting"), true, async () =>
            {
                await WirelessDisplays.DisconnectAsync();
                foreach (var paint in _tilePainters) paint();
                if (_side == SideKind.Cast) RenderCast();
            });
        }
        var searching = SideMessage(Loc.T("CC_Searching"));
        var displays = await WirelessDisplays.FindAsync();
        if (token != _sideToken) return;
        SideList.Children.Remove(searching);
        if (displays.Count == 0)
        {
            SideMessage(WirelessDisplays.HasWifi
                ? Loc.T("CC_NoDisplays")
                : Loc.T("CC_NoWifi"));
            return;
        }
        foreach (var display in displays)
        {
            var d = display;
            SideRow("", d.Name, null, false, async () =>
            {
                SideHeader(Loc.T("CC_Cast"));
                SideMessage(Loc.T("CC_Connecting", d.Name));
                bool ok = await WirelessDisplays.ConnectAsync(d);
                foreach (var paint in _tilePainters) paint();
                if (_side != SideKind.Cast) return;
                if (ok) SetSide(SideKind.None);
                else RenderCast(Loc.T("CC_ConnectFailed", d.Name));
            });
        }
    }

    // ------------------------------------------------------------------ quick settings

    async void RenderTiles()
    {
        var actions = await QuickActions.ListAsync();
        TileGrid.Children.Clear();
        _tilePainters.Clear();
        foreach (var action in actions) TileGrid.Children.Add(Tile(action));
    }

    static SideKind SideOf(QuickPanel panel) => panel switch
    {
        QuickPanel.Project => SideKind.Project,
        QuickPanel.Cast => SideKind.Cast,
        QuickPanel.WiFi => SideKind.WiFi,
        QuickPanel.Bluetooth => SideKind.Bluetooth,
        _ => SideKind.None,
    };

    FrameworkElement Tile(QuickAction action)
    {
        if (action.Panel != QuickPanel.None && action.IsOn != null && action.Run != null) return SplitTile(action);

        var glyph = new TextBlock
        {
            Text = action.Glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(glyph);
        // A tile with its own list carries a chevron, as in the Windows panel.
        if (action.Panel != QuickPanel.None)
        {
            var chevron = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 9, Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 0, 0),
            };
            chevron.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(TextBlock.Foreground)) { Source = glyph });
            face.Children.Add(chevron);
        }
        var button = new Button { Style = (Style)FindResource("TileButton"), Content = face, ToolTip = action.Label };
        var label = new TextBlock
        {
            Text = action.Label, FontSize = 11, Opacity = 0.85, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 30, Margin = new Thickness(0, 5, 0, 0),
        };

        void Paint()
        {
            bool on = action.IsOn?.Invoke() == true;
            if (on) button.SetResourceReference(BackgroundProperty, "Accent");
            else button.SetResourceReference(BackgroundProperty, "TileIdle");
            glyph.Foreground = on && ColorUtil.Luminance(_accent) > 0.42 ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White;
        }
        Paint();
        _tilePainters.Add(Paint);

        button.Click += async (_, _) =>
        {
            if (action.Panel != QuickPanel.None)
            {
                var kind = SideOf(action.Panel);
                SetSide(_side == kind ? SideKind.None : kind);
                return;
            }
            if (action.Run == null) return;
            // A shortcut hands over to a Windows page; a switch stays here and repaints.
            if (action.IsOn == null) Dismiss();
            try
            {
                await action.Run();
            }
            catch (Exception ex)
            {
                Log.Error($"Quick setting '{action.Label}' failed", ex);
            }
            Paint();
        };

        var cell = new StackPanel { Margin = new Thickness(4, 0, 4, 10) };
        cell.Children.Add(button);
        cell.Children.Add(label);
        return cell;
    }

    /// <summary>
    /// A switch with its own list (Wi-Fi, Bluetooth), split as in Windows: the larger part flips the
    /// switch, the arrow beside it opens the list of networks or devices.
    /// </summary>
    FrameworkElement SplitTile(QuickAction action)
    {
        var icons = (FontFamily)FindResource("IconFont");
        var glyph = new TextBlock { Text = action.Glyph, FontFamily = icons, FontSize = 16 };
        var arrow = new TextBlock { Text = "", FontFamily = icons, FontSize = 10 };
        var toggle = new Button { Style = (Style)FindResource("TilePart"), Tag = new CornerRadius(8, 0, 0, 8), Content = glyph, ToolTip = action.Label };
        var open = new Button { Style = (Style)FindResource("TilePart"), Tag = new CornerRadius(0, 8, 8, 0), Content = arrow, Width = 30, ToolTip = action.Label };
        var divider = new Border { Width = 1, Margin = new Thickness(0, 10, 0, 10), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)) };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(toggle);
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);
        Grid.SetColumn(open, 2);
        grid.Children.Add(open);
        var body = new Border { Height = 44, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = grid };
        body.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));

        void Paint()
        {
            bool on = action.IsOn!.Invoke();
            if (on) body.SetResourceReference(Border.BackgroundProperty, "Accent");
            else body.SetResourceReference(Border.BackgroundProperty, "TileIdle");
            Brush ink = on && ColorUtil.Luminance(_accent) > 0.42 ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White;
            glyph.Foreground = arrow.Foreground = ink;
            divider.Background = on ? new SolidColorBrush(ColorUtil.WithAlpha(((SolidColorBrush)ink).Color, 0.3)) : new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
        }
        Paint();
        _tilePainters.Add(Paint);

        var kind = SideOf(action.Panel);
        toggle.Click += async (_, _) =>
        {
            try
            {
                await action.Run!();
            }
            catch (Exception ex)
            {
                Log.Error($"Quick setting '{action.Label}' failed", ex);
            }
            Paint();
            // The open list follows the switch: off shows the "turn on" note, on starts looking.
            if (_side == kind) SetSide(kind, animate: false);
        };
        open.Click += (_, _) => SetSide(_side == kind ? SideKind.None : kind);

        var label = new TextBlock
        {
            Text = action.Label, FontSize = 11, Opacity = 0.85, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 30, Margin = new Thickness(0, 5, 0, 0),
        };
        var cell = new StackPanel { Margin = new Thickness(4, 0, 4, 10) };
        cell.Children.Add(body);
        cell.Children.Add(label);
        return cell;
    }

    // ------------------------------------------------------------------ notifications

    void ClearAll_Click(object sender, RoutedEventArgs e) => _feed.ClearAll();

    void RenderNotifications()
    {
        var items = _feed.Items;
        NotificationList.Children.Clear();
        foreach (var item in items) NotificationList.Children.Add(NotificationCard(item));

        bool any = items.Count > 0;
        NotificationScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = _feed.Available ? Loc.T("CC_NoNotifications") : Loc.T("CC_NotificationsDenied");
        CountChip.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = items.Count.ToString();
        ClearAllButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement NotificationCard(NotificationItem item)
    {
        var dismiss = new Button
        {
            Style = (Style)FindResource("TextButton"), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(4, -4, -6, -4),
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10 },
            Opacity = 0, ToolTip = Loc.T("CC_DismissOne"),
        };
        dismiss.Click += (_, e) =>
        {
            e.Handled = true;
            _feed.Dismiss(item.Id);
        };

        var head = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement logo = item.Logo != null
            ? new Image { Source = item.Logo, Width = 16, Height = 16 }
            : new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, Opacity = 0.7 };
        logo.Margin = new Thickness(0, 0, 8, 0);
        logo.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        head.Children.Add(logo);
        var app = new TextBlock { Text = item.AppName, FontSize = 11.5, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(app, 1);
        head.Children.Add(app);
        var time = new TextBlock { Text = When(item.Time), FontSize = 11, Opacity = 0.45, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(time, 2);
        head.Children.Add(time);
        Grid.SetColumn(dismiss, 3);
        head.Children.Add(dismiss);

        var body = new StackPanel();
        body.Children.Add(head);
        if (item.Title.Length > 0)
            body.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 38, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Body.Length > 0)
            body.Children.Add(new TextBlock { Text = item.Body, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Child = body,
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            dismiss.Opacity = 1;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255));
            dismiss.Opacity = 0;
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            Dismiss();
            _feed.Open(item);
        };
        return card;
    }

    static string When(DateTimeOffset time)
    {
        var ago = DateTimeOffset.Now - time;
        if (ago < TimeSpan.FromMinutes(1)) return Loc.T("CC_JustNow");
        if (ago < TimeSpan.FromHours(1)) return Loc.T("CC_MinutesAgo", (int)ago.TotalMinutes);
        var local = time.ToLocalTime();
        if (local.Date == DateTime.Today) return local.ToString("HH:mm");
        if (local.Date == DateTime.Today.AddDays(-1)) return Loc.T("CC_Yesterday");
        return local.ToString(Loc.T("CC_DayMonth"), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Re-renders the text code builds, after the language changes.</summary>
    public void RefreshText()
    {
        SyncOutput();
        RenderNotifications();
        RenderTiles();
        if (_side != SideKind.None) SetSide(_side, animate: false);
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
