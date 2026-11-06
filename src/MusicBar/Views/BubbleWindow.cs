using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MusicBar.Interop;

namespace MusicBar.Views;

/// <summary>
/// A small speech bubble beside the taskbar that never takes focus: the face saying it's time for a
/// break, or how far the lyrics were just moved. It fades out by itself; a click dismisses it.
/// </summary>
public sealed class BubbleWindow : Window
{
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    readonly Border _card;
    readonly TextBlock _glyph = new()
    {
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 13,
        Margin = new Thickness(0, 1, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    readonly TextBlock _text = new()
    {
        FontSize = 12.5,
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 260,
        VerticalAlignment = VerticalAlignment.Center,
    };
    readonly TranslateTransform _shift = new();
    readonly DispatcherTimer _timer = new();
    IntPtr _hwnd;
    TaskbarEdge _edge;

    public BubbleWindow()
    {
        CpuRendering.Use(this);
        Title = "MusicBar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = Top = -20000;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_glyph);
        row.Children.Add(_text);
        _card = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(12, 8, 13, 9),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1A, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = row,
            Opacity = 0,
            RenderTransform = _shift,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.45, Color = Colors.Black },
            Cursor = Cursors.Hand,
        };
        Content = _card;
        _card.MouseLeftButtonUp += (_, _) => Fade();
        _timer.Tick += (_, _) => Fade();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
        };
    }

    /// <summary>
    /// Shows <paramref name="text"/> beside the taskbar near <paramref name="anchor"/> (screen pixels)
    /// for <paramref name="seconds"/>, with an optional icon glyph in <paramref name="accent"/>.
    /// </summary>
    internal void Say(string text, string? glyph, Color accent, Point anchor, TaskbarEdge edge, Native.RECT taskbar, Native.RECT monitor, double seconds = 5)
    {
        _edge = edge;
        _text.Text = text;
        _glyph.Text = glyph ?? "";
        _glyph.Visibility = glyph == null ? Visibility.Collapsed : Visibility.Visible;
        _glyph.Foreground = new SolidColorBrush(accent);
        if (!IsVisible) Show();
        UpdateLayout();
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var margin = new Thickness(_card.Margin.Left * m.M11, _card.Margin.Top * m.M22, _card.Margin.Right * m.M11, _card.Margin.Bottom * m.M22);
        var (x, y) = TaskbarSide.CardSpot((int)Math.Round(ActualWidth * m.M11), (int)Math.Round(ActualHeight * m.M22), margin,
            (int)Math.Round(6 * m.M11), anchor, edge, taskbar, monitor);
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        var d = TimeSpan.FromMilliseconds(260);
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        var (axis, toward) = TaskbarSide.Slide(edge);
        _shift.BeginAnimation(axis == TranslateTransform.XProperty ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
        _shift.BeginAnimation(axis, new DoubleAnimation(toward * 10, 0, d) { EasingFunction = EaseOut });
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    void Fade()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (_card.Opacity < 0.01 && !_timer.IsEnabled) Hide();
        };
        _card.BeginAnimation(OpacityProperty, fade);
        var (axis, toward) = TaskbarSide.Slide(_edge);
        _shift.BeginAnimation(axis, new DoubleAnimation(toward * 6, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }
}
