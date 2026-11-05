using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// Click-through window covering the whole taskbar. It paints the taskbar
/// background and the playback progress; it is kept just below the taskbar
/// in z-order so the icons stay on top of it.
/// </summary>
public partial class TaskbarLayerWindow : Window
{
    static readonly Color SurfaceColor = Color.FromRgb(0x0B, 0x0B, 0x0F);
    // Close to Windows 11's own light taskbar, so its black icons read the same.
    static readonly Color LightSurfaceColor = Color.FromRgb(0xF2, 0xF3, 0xF5);

    // Over the taskbar the layer must not hide the icons, so it is capped.
    const double OverSurfaceCap = 0.35;
    const double OverFillCap = 0.5;

    readonly SolidColorBrush _surfaceBrush = new();
    readonly LinearGradientBrush _tintBrush = new() { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
    readonly GradientStop _tint0 = new() { Offset = 0 };
    readonly GradientStop _tint1 = new() { Offset = 1 };

    double _surfaceOpacity;
    Color _base = SurfaceColor;
    Color _primary = ColorUtil.Brand;
    Color _secondary = ColorUtil.Brand;

    public TaskbarLayerWindow()
    {
        InitializeComponent();
        _tintBrush.GradientStops.Add(_tint0);
        _tintBrush.GradientStops.Add(_tint1);
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                Native.WS_EX_APPWINDOW);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public AudioMeter? Audio
    {
        get => Seek.Audio;
        set => Seek.Audio = value;
    }

    /// <param name="background">The background to paint, with Auto already resolved to Dark or Light.</param>
    internal void ApplyStyle(AppSettings s, bool over, TaskbarEdge edge, BarBackground background)
    {
        _surfaceOpacity = over ? Math.Min(s.BackgroundOpacity, OverSurfaceCap) : s.BackgroundOpacity;
        _base = background is BarBackground.Light or BarBackground.LightTinted ? LightSurfaceColor : SurfaceColor;
        switch (background)
        {
            case BarBackground.Dark or BarBackground.Light:
                _surfaceBrush.Color = ColorUtil.WithAlpha(_base, _surfaceOpacity);
                Surface.Background = _surfaceBrush;
                break;
            case BarBackground.Tinted or BarBackground.LightTinted:
                Surface.Background = _tintBrush;
                break;
            default:
                Surface.Background = null;
                break;
        }

        _s = s;
        Seek.Look = s.SeekStyle;
        Seek.Direction = s.SeekDirection;
        Seek.IdleMotion = s.SeekIdleMotion;
        Seek.Intensity = over ? Math.Min(s.FillOpacity, OverFillCap) : s.FillOpacity;

        // A skin that compiled paints instead of the style; one that didn't leaves the style on.
        var skin = s.Skin.Length > 0 ? SkinLibrary.Load(s.Skin) : null;
        bool skinned = skin is { Works: true };
        SkinHost.SetSkin(skinned ? skin : null);
        SkinHost.Visibility = skinned ? Visibility.Visible : Visibility.Collapsed;
        Seek.Visibility = skinned ? Visibility.Hidden : Visibility.Visible;
        Seek.Skinned = skinned;

        // The seekbar always draws along its own x axis with the desktop-facing edge at its top
        // or bottom. On a vertical taskbar it is turned a quarter left, so it runs bottom to top
        // and its top lies along the taskbar's left side.
        bool vertical = edge is TaskbarEdge.Left or TaskbarEdge.Right;
        if (vertical != Seek.LayoutTransform is RotateTransform)
        {
            Seek.LayoutTransform = vertical ? new RotateTransform(-90) : Transform.Identity;
            SkinHost.LayoutTransform = vertical ? new RotateTransform(-90) : Transform.Identity;
        }
        Seek.SetEdge(edge is TaskbarEdge.Bottom or TaskbarEdge.Right);
        _tintBrush.StartPoint = vertical ? new Point(0.5, 1) : new Point(0, 0.5);
        _tintBrush.EndPoint = vertical ? new Point(0.5, 0) : new Point(1, 0.5);
        SetColors(_primary, _secondary, animate: false);
    }

    /// <summary>The song's colors: the background's tint takes them, and the style too unless it keeps colors of its own.</summary>
    public void SetColors(Color primary, Color secondary, bool animate)
    {
        _primary = primary;
        _secondary = secondary;
        var d = animate ? TimeSpan.FromMilliseconds(900) : TimeSpan.Zero;
        AnimateColor(_tint0, ColorUtil.WithAlpha(ColorUtil.Lerp(_base, secondary, 0.24), _surfaceOpacity), d);
        AnimateColor(_tint1, ColorUtil.WithAlpha(ColorUtil.Lerp(_base, primary, 0.14), _surfaceOpacity), d);
        if (_s != null && StylePalettes.For(_s) is { } own) Seek.SetColors(own.A, own.B, animate, own.C);
        else Seek.SetColors(primary, secondary, animate);
    }

    AppSettings? _s;

    static void AnimateColor(GradientStop stop, Color to, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            stop.BeginAnimation(GradientStop.ColorProperty, null);
            stop.Color = to;
            return;
        }
        stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    public void SetArt(BitmapSource? art) => Seek.SetArt(art);

    public void SetTrack(string key) => Seek.SetTrack(key);

    /// <summary>Quiet zones in the seekbar's own units: along its run, which on a vertical taskbar starts at the bottom.</summary>
    public void SetQuietZones(System.Collections.Generic.IReadOnlyList<(double Left, double Right)> zones)
    {
        Seek.SetQuietZones(zones);
        SkinHost.OpacityMask = Seek.QuietMaskFor(Seek.ActualWidth);
    }

    /// <summary>The empty stretches the bar keeps to, in the seekbar's own units; empty for the whole taskbar.</summary>
    public void SetPieces(System.Collections.Generic.IReadOnlyList<(double Left, double Right)> pieces)
    {
        Seek.SetPieces(pieces);
        SkinHost.SetPieces(pieces);
    }

    /// <summary>Called every frame by the overlay.</summary>
    public void SetProgress(double fraction, bool hasTimeline, bool playing)
    {
        Seek.SetState(fraction, hasTimeline, playing);
        Seek.Tick();
        if (Seek.Skinned) SkinHost.Update(Seek);
    }

    /// <summary>A skin is on: it wants a frame for every display frame, like the visualizers.</summary>
    public bool Skinned => Seek.Skinned;
}
