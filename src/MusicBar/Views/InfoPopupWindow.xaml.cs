using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;

namespace MusicBar.Views;

public sealed record SongCard(
    string Title,
    string Artist,
    string Album,
    int? Year,
    IReadOnlyList<string> Genres,
    string? TrackLabel,
    ImageSource? SourceIcon,
    BitmapSource? Cover);

/// <summary>
/// The song card that rises above the taskbar when the pointer rests on the album cover. It
/// stays while the pointer travels up to it and rests on it, and its timeline seeks: click
/// or drag. It never takes focus.
/// </summary>
public partial class InfoPopupWindow : Window
{
    const double CardGap = 10;
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
    // Long enough to cross the gap from the cover to the card.
    static readonly TimeSpan DismissGrace = TimeSpan.FromMilliseconds(380);

    readonly SolidColorBrush _chipAccent = new(Color.FromArgb(0x47, 0xFF, 0x6B, 0x6B));
    readonly DispatcherTimer _dismissTimer = new() { Interval = DismissGrace };
    int _showToken;
    TaskbarEdge _edge = TaskbarEdge.Bottom;
    long _lastSecond = -1;
    TimeSpan _position, _duration;
    // The pointer holds the seek bar: the card shows where it would go, not where the song is.
    bool _dragging;

    public InfoPopupWindow(SolidColorBrush accentBrush)
    {
        CpuRendering.Use(this);
        InitializeComponent();
        ProgressFill.Background = accentBrush;
        Card.SizeChanged += (_, _) => Card.Clip = new RectangleGeometry(new Rect(Card.RenderSize), 14, 14);
        SeekArea.SizeChanged += (_, _) => PlaceThumb(ProgressScale.ScaleX);
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            if (!_dragging && !Root.IsMouseOver) Dismiss();
        };
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>Whether the player takes a new position; without it the timeline only shows.</summary>
    public bool CanSeek
    {
        get => _canSeek;
        set
        {
            _canSeek = value;
            SeekArea.Cursor = value ? System.Windows.Input.Cursors.Hand : null;
        }
    }

    bool _canSeek;

    /// <summary>The pointer let go of the seek bar here.</summary>
    public event Action<TimeSpan>? SeekRequested;

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    public void SetAccent(Color accent)
    {
        WashStop.Color = ColorUtil.WithAlpha(accent, 0.34);
        _chipAccent.Color = ColorUtil.WithAlpha(accent, 0.3);
    }

    public void SetContent(SongCard card)
    {
        TitleText.Text = card.Title;
        ArtistText.Text = card.Artist;
        ArtistText.Visibility = card.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var album = card.Album;
        if (card.Year is int year) album = album.Length > 0 ? $"{album}  ·  {year}" : year.ToString();
        AlbumText.Text = album;
        AlbumText.Visibility = album.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        CoverBrush.ImageSource = card.Cover;
        BackdropBrush.ImageSource = card.Cover;
        SourceIcon.Source = card.SourceIcon;
        SourceBadge.Visibility = card.SourceIcon != null ? Visibility.Visible : Visibility.Collapsed;

        Chips.Children.Clear();
        bool first = true;
        foreach (var genre in card.Genres)
        {
            Chips.Children.Add(Chip(genre, first));
            first = false;
        }
        if (card.TrackLabel != null) Chips.Children.Add(Chip(card.TrackLabel, accent: false));
        Chips.Visibility = Chips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lastSecond = -1;
    }

    Border Chip(string text, bool accent) => new()
    {
        CornerRadius = new CornerRadius(8),
        Background = accent ? _chipAccent : new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(7, 1, 7, 2),
        Margin = new Thickness(0, 0, 5, 4),
        Child = new TextBlock { Text = text, FontSize = 10.5, Foreground = Brushes.White },
    };

    public void SetProgress(TimeSpan position, TimeSpan duration)
    {
        _position = position;
        _duration = duration;
        if (_dragging) return;
        ShowTime(position, duration);
    }

    void ShowTime(TimeSpan position, TimeSpan duration)
    {
        bool known = duration > TimeSpan.Zero;
        double frac = known ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
        ProgressScale.ScaleX = frac;
        PlaceThumb(frac);
        long second = (long)position.TotalSeconds;
        if (second == _lastSecond) return;
        _lastSecond = second;
        ElapsedText.Text = Format(position);
        RemainingText.Text = known ? "-" + Format(duration - position) : "";
    }

    void PlaceThumb(double frac) => Canvas.SetLeft(Thumb, frac * Math.Max(0, SeekArea.ActualWidth - 4) - Thumb.Width / 2);

    // ------------------------------------------------------------------ staying open

    /// <summary>The pointer left the cover: closes after a moment, unless it reaches the card first.</summary>
    public void RequestDismiss()
    {
        if (!IsOpen) return;
        _dismissTimer.Stop();
        _dismissTimer.Start();
    }

    /// <summary>
    /// The player went from under the card (an auto-hiding taskbar slid away as the pointer left it
    /// for the card): the card stays while the pointer is on it and goes once it leaves.
    /// </summary>
    public void LetGo()
    {
        if (!IsOpen) return;
        if (Root.IsMouseOver || _dragging) _dismissTimer.Stop();
        else RequestDismiss();
    }

    void Root_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => _dismissTimer.Stop();

    void Root_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) RequestDismiss();
    }

    // ------------------------------------------------------------------ seeking

    void Seek_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (CanSeek && _duration > TimeSpan.Zero) Lift(true);
    }

    void Seek_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) Lift(false);
    }

    void Seek_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!CanSeek || _duration <= TimeSpan.Zero) return;
        _dragging = true;
        SeekArea.CaptureMouse();
        Follow(e.GetPosition(SeekArea).X);
        e.Handled = true;
    }

    void Seek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging) Follow(e.GetPosition(SeekArea).X);
    }

    void Seek_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        var target = Follow(e.GetPosition(SeekArea).X);
        _dragging = false;
        SeekArea.ReleaseMouseCapture();
        _position = target;
        SeekRequested?.Invoke(target);
        if (!SeekArea.IsMouseOver) Lift(false);
        if (!Root.IsMouseOver) RequestDismiss();
        e.Handled = true;
    }

    void Seek_LostCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        // Taken away mid-drag (the card closed, another window): nothing is sought.
        _dragging = false;
        Lift(false);
        _lastSecond = -1;
        ShowTime(_position, _duration);
    }

    /// <summary>Moves the bar to the pointer and shows that moment; returns it.</summary>
    TimeSpan Follow(double x)
    {
        double frac = Math.Clamp((x - 2) / Math.Max(1, SeekArea.ActualWidth - 4), 0, 1);
        var target = TimeSpan.FromTicks((long)(_duration.Ticks * frac));
        _lastSecond = -1;
        ShowTime(target, _duration);
        return target;
    }

    /// <summary>The rail thickens and the thumb grows in while the pointer is on the bar.</summary>
    void Lift(bool up)
    {
        var d = TimeSpan.FromMilliseconds(up ? 160 : 220);
        Rail.BeginAnimation(HeightProperty, new DoubleAnimation(up ? 5 : 3, d) { EasingFunction = EaseOut });
        Thumb.BeginAnimation(OpacityProperty, new DoubleAnimation(up ? 1 : 0, d));
        ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(up ? 1 : 0.4, d) { EasingFunction = up ? Spring : EaseOut });
        ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(up ? 1 : 0.4, d) { EasingFunction = up ? Spring : EaseOut });
    }

    static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>
    /// Opens the card next to the taskbar, centred on <paramref name="anchor"/> (screen pixels)
    /// along the taskbar's length and kept on the monitor.
    /// </summary>
    internal void ShowAt(Point anchor, TaskbarEdge edge, Native.RECT taskbar, Native.RECT monitorBounds)
    {
        _dismissTimer.Stop();
        // Already up (the pointer came back to the cover): it stays where it is.
        if (IsOpen) return;
        _showToken++;
        _edge = edge;
        Root.RenderTransformOrigin = edge switch
        {
            TaskbarEdge.Top => new Point(0.5, 0),
            TaskbarEdge.Left => new Point(0, 0.5),
            TaskbarEdge.Right => new Point(1, 0.5),
            _ => new Point(0.5, 1),
        };
        if (!IsVisible) Show();
        UpdateLayout();

        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        int w = (int)Math.Round(ActualWidth * m.M11);
        int h = (int)Math.Round(ActualHeight * m.M22);
        int gapX = (int)Math.Round(CardGap * m.M11), gapY = (int)Math.Round(CardGap * m.M22);
        int left = (int)Math.Round(Root.Margin.Left * m.M11), right = (int)Math.Round(Root.Margin.Right * m.M11);
        int top = (int)Math.Round(Root.Margin.Top * m.M22), bottom = (int)Math.Round(Root.Margin.Bottom * m.M22);
        int x, y;
        if (TaskbarSide.IsVertical(edge))
        {
            x = edge == TaskbarEdge.Left ? taskbar.Right + gapX - left : taskbar.Left - gapX + right - w;
            y = Math.Clamp((int)Math.Round(anchor.Y - h / 2.0), monitorBounds.Top - top + 6,
                Math.Max(monitorBounds.Top, monitorBounds.Bottom - h + bottom - 6));
        }
        else
        {
            x = Math.Clamp((int)Math.Round(anchor.X - w / 2.0), monitorBounds.Left - left + 6,
                Math.Max(monitorBounds.Left, monitorBounds.Right - w + right - 6));
            y = edge == TaskbarEdge.Top ? taskbar.Bottom + gapY - top : taskbar.Top - gapY + bottom - h;
        }
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        IsOpen = true;
        var d = TimeSpan.FromMilliseconds(280);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.93, 1, d) { EasingFunction = Spring });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.93, 1, d) { EasingFunction = Spring });
        var (axis, toward) = TaskbarSide.Slide(edge);
        RootShift.BeginAnimation(axis == TranslateTransform.XProperty ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
        RootShift.BeginAnimation(axis, new DoubleAnimation(toward * 12, 0, d) { EasingFunction = EaseOut });
    }

    public void Dismiss()
    {
        _dismissTimer.Stop();
        if (!IsOpen) return;
        if (_dragging) SeekArea.ReleaseMouseCapture();
        IsOpen = false;
        int token = ++_showToken;
        var d = TimeSpan.FromMilliseconds(150);
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) =>
        {
            if (token == _showToken) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        var (axis, toward) = TaskbarSide.Slide(_edge);
        RootShift.BeginAnimation(axis, new DoubleAnimation(toward * 6, d) { EasingFunction = EaseOut });
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
