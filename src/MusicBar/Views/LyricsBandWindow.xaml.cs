using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>
/// Frameless strip that shows the lyrics in the biggest empty stretch of the taskbar, wherever the
/// taskbar's icons happen to sit. Clicks go through it to the taskbar, except on the words themselves:
/// there a click opens the lyrics card and the wheel nudges their timing, as on the player's lyrics.
/// </summary>
public partial class LyricsBandWindow : Window
{
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly Brush AlmostClear = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    readonly DropShadowEffect _lyricStroke = new() { ShadowDepth = 0 };
    // Watches the pointer while the strip shows: over the words, the strip takes clicks; elsewhere they pass.
    readonly System.Windows.Threading.DispatcherTimer _pointer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    int _token;
    bool _vertical;
    bool _clickable;

    public LyricsBandWindow()
    {
        CpuRendering.Use(this);
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                Native.WS_EX_APPWINDOW);
        };
        _pointer.Tick += (_, _) => FollowPointer();
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            LyricsClicked?.Invoke();
        };
        MouseWheel += (_, e) =>
        {
            e.Handled = true;
            LyricsWheel?.Invoke(e.Delta);
        };
    }

    /// <summary>The words were clicked: open the lyrics card (and from it the lyrics search).</summary>
    public event Action? LyricsClicked;

    /// <summary>The wheel turned over the words: nudge their timing.</summary>
    public event Action<int>? LyricsWheel;

    /// <summary>
    /// The strip is click-through so the empty taskbar under it still works (its menu, dragging to it), which left
    /// the lyrics there with no way in: no lyrics card, no lyrics search. Now it takes clicks while the pointer is
    /// over the words, a few pixels around them, and lets them through again as soon as it leaves.
    /// </summary>
    void FollowPointer()
    {
        bool over = false;
        if (IsRevealed && Hwnd != IntPtr.Zero && Native.GetCursorPos(out var p) && Native.GetWindowRect(Hwnd, out var r)
            && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom)
        {
            var (start, end) = LyricsScreenSpan();
            double along = _vertical ? p.Y : p.X;
            over = end - start > 4 && along >= start - 6 && along <= end + 6;
        }
        if (over == _clickable) return;
        _clickable = over;
        Native.AddExStyle(Hwnd, over ? 0 : Native.WS_EX_TRANSPARENT, over ? Native.WS_EX_TRANSPARENT : 0);
        IsHitTestVisible = over;
        // A layered window lets clicks through wherever it is fully clear, the gaps between letters too: barely
        // there instead while the words take clicks.
        Background = over ? AlmostClear : Brushes.Transparent;
        Cursor = over ? System.Windows.Input.Cursors.Hand : null;
    }

    public IntPtr Hwnd { get; private set; }

    public bool IsRevealed { get; private set; }

    /// <summary>The lyrics display in use: lines across a horizontal taskbar, stacked words down a vertical one.</summary>
    public ILyricsView LyricsView => _vertical ? StackLyrics : LineLyrics;

    /// <summary>
    /// Screen pixels covered by the lyric text in the strip, along the taskbar's length; empty when hidden.
    /// </summary>
    public (double Start, double End) LyricsScreenSpan()
        => IsRevealed && IsVisible && Root.Opacity > 0.05 ? LyricsView.TextScreenSpan() : (0, 0);

    public void SetAccent(Color accent) => LineLyrics.Accent = StackLyrics.Accent = accent;

    /// <param name="thickness">The taskbar's short side, in DIPs.</param>
    /// <param name="lightBar">The taskbar behind is light: default white text turns dark.</param>
    public void ApplyStyle(AppSettings s, double thickness, bool vertical, bool lightBar)
    {
        _vertical = vertical;
        LineLyrics.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        StackLyrics.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        FontFamily = BundledFonts.Resolve(s.FontFamily);
        Root.Effect = LyricStyle.Apply(s, LyricsView, _lyricStroke, lightBar);
        double h = thickness;
        // Stacked words have room for the next line below them; a horizontal strip needs the height.
        // Romanization under each line takes the second row even when the next line is not wanted.
        bool twoLine = (s.ShowNextLine || s.Romanization == Romanization.Below) && (vertical || h >= 40);
        LineLyrics.ShowsNext = s.ShowNextLine;
        double main = (vertical ? Math.Clamp(h * 0.3, 11, 14) : twoLine ? Math.Clamp(h * 0.30, 12, 15) : Math.Clamp(h * 0.45, 12, 16)) * s.LyricScale;
        double sub = (vertical ? 11 : Math.Clamp(h * 0.22, 10, 12)) * s.LyricScale;
        LyricsView.Configure(s.LyricEffect, s.Karaoke, twoLine, FontFamily,
            Math.Round(main * 2) / 2, Math.Round(sub * 2) / 2, HorizontalAlignment.Center, s.AnimationSpeed);
    }

    /// <summary>
    /// Fades the strip in (sliding from the widget's side, <paramref name="from"/> DIPs along the
    /// taskbar) or out, then hides the window.
    /// </summary>
    public void Reveal(bool show, double from)
    {
        if (show == IsRevealed) return;
        IsRevealed = show;
        int token = ++_token;
        if (show)
        {
            if (!IsVisible) Show();
            _pointer.Start();
            var d = TimeSpan.FromMilliseconds(380);
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, d) { EasingFunction = EaseOut });
            var along = _vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;
            RootShift.BeginAnimation(_vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, null);
            RootShift.BeginAnimation(along, new DoubleAnimation(from, 0, d) { EasingFunction = EaseOut });
            return;
        }
        _pointer.Stop();
        FollowPointer();  // not revealed: back to click-through at once
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (token == _token) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
