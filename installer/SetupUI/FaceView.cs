using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MusicBar.Setup;

public enum Mood { Normal, Sing, Happy, Sad, Oops }

/// <summary>
/// MusicBar's face, as the website draws it (site.css .face): two eyes that blink and glance about, a mouth, and a
/// mood. Drawn in a 44 x 32 box; put it in a Viewbox for its size.
/// </summary>
public sealed class FaceView : Canvas
{
    static readonly Brush White = Frozen(Color.FromRgb(0xFF, 0xFD, 0xF6));
    static readonly Brush Pupil = Frozen(Color.FromRgb(0x15, 0x17, 0x1E));
    static readonly Brush Inside = Frozen(Color.FromRgb(0x12, 0x0C, 0x10));
    static readonly Brush Tongue = Frozen(Color.FromRgb(0xFF, 0x7B, 0x8A));
    static readonly Brush Cheek = Frozen(Color.FromArgb(0x99, 0xFF, 0x7B, 0x8A));
    static readonly Brush Tear = Frozen(Color.FromRgb(0x7C, 0xC8, 0xFF));

    readonly TranslateTransform _body = new();
    Mood _mood = (Mood)(-1);

    public FaceView()
    {
        Width = 44;
        Height = 32;
        RenderTransform = _body;
        SetMood(Mood.Normal);
    }

    public Mood Mood
    {
        get => _mood;
        set => SetMood(value);
    }

    void SetMood(Mood mood)
    {
        if (mood == _mood) return;
        _mood = mood;
        Children.Clear();
        _body.BeginAnimation(TranslateTransform.XProperty, null);
        _body.BeginAnimation(TranslateTransform.YProperty, null);

        if (mood == Mood.Happy)
        {
            Add(Arc("M9.3,12.5 A5.2,6.5 0 0 1 19.7,12.5", 2.6));
            Add(Arc("M25.3,12.5 A5.2,6.5 0 0 1 35.7,12.5", 2.6));
            Add(new Ellipse { Width = 8, Height = 4.5, Fill = Cheek }, 1, 16);
            Add(new Ellipse { Width = 8, Height = 4.5, Fill = Cheek }, 35, 16);
            Add(OpenMouth("M11,17 L33,17 A11,12 0 0 1 11,17 Z", 22, 17, animate: false));
            _body.BeginAnimation(TranslateTransform.YProperty, Loop(new DoubleAnimation(0, -1.7, TimeSpan.FromSeconds(0.55))
            {
                AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            }));
            return;
        }

        // Where the pupil sits in its eye (fractions of the eye, as site.css places ::before), and how big it is.
        (double x, double y, double size) pupil = mood switch
        {
            Mood.Sad => (0.30, 0.48, 0.46),
            Mood.Oops => (0.30, 0.30, 0.40),
            _ => (0.32, 0.34, 0.46),
        };
        bool glance = mood is Mood.Normal or Mood.Sing;
        Add(Eye(pupil, glance), 8, 0);
        Add(Eye(pupil, glance), 24, 0);

        switch (mood)
        {
            case Mood.Sing:
                Add(OpenMouth("M11,18 L33,18 L33,21 A11,10 0 0 1 11,21 Z", 22, 18, animate: true));
                break;
            case Mood.Sad:
                Add(Arc("M13,29 A9,6.5 0 0 1 31,29", 2.4));
                var tear = new Ellipse { Width = 4, Height = 6, Fill = Tear, Opacity = 0 };
                var fall = new TranslateTransform();
                tear.RenderTransform = fall;
                Add(tear, 10, 14);
                var t = TimeSpan.FromSeconds(2.6);
                var down = new DoubleAnimationUsingKeyFrames { Duration = t };
                down.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                down.KeyFrames.Add(new SplineDoubleKeyFrame(14, KeyTime.FromPercent(1), new KeySpline(0.42, 0, 1, 1)));
                var fade = new DoubleAnimationUsingKeyFrames { Duration = t };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                fall.BeginAnimation(TranslateTransform.YProperty, Loop(down));
                tear.BeginAnimation(OpacityProperty, Loop(fade));
                break;
            case Mood.Oops:
                Add(new Ellipse { Width = 8, Height = 8, Fill = Inside, Stroke = White, StrokeThickness = 2.2 }, 18, 21);
                var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.4) };
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.80)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1.6, KeyTime.FromPercent(0.84)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(1.6, KeyTime.FromPercent(0.88)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1, KeyTime.FromPercent(0.92)));
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.96)));
                _body.BeginAnimation(TranslateTransform.XProperty, Loop(shake));
                break;
            default:
                Add(Arc("M12.2,20.5 A9.8,8.5 0 0 0 31.8,20.5", 2.4));
                break;
        }
    }

    /// <summary>An open eye: white, a dark pupil that may glance left and right, a shine; it blinks.</summary>
    static UIElement Eye((double x, double y, double size) pupil, bool glance)
    {
        var eye = new Canvas
        {
            Width = 13, Height = 17,
            Clip = new EllipseGeometry(new Point(6.5, 8.5), 6.5, 8.5),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        eye.Children.Add(new Ellipse { Width = 13, Height = 17, Fill = White });
        var dot = new Ellipse { Width = 13 * pupil.size, Height = 17 * pupil.size, Fill = Pupil };
        SetLeft(dot, 13 * pupil.x);
        SetTop(dot, 17 * pupil.y);
        eye.Children.Add(dot);
        var shine = new Ellipse { Width = 2.86, Height = 3.06, Fill = Brushes.White };
        SetLeft(shine, 2.86);
        SetTop(shine, 2.04);
        eye.Children.Add(shine);

        var lid = new ScaleTransform();
        eye.RenderTransform = lid;
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.2) };
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.94)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromPercent(0.96)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        lid.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(blink));

        if (glance)
        {
            var look = new TranslateTransform();
            dot.RenderTransform = look;
            var move = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7) };
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            move.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            move.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.40)));
            move.KeyFrames.Add(new EasingDoubleKeyFrame(-1.08, KeyTime.FromPercent(0.50), ease));
            move.KeyFrames.Add(new LinearDoubleKeyFrame(-1.08, KeyTime.FromPercent(0.70)));
            move.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromPercent(0.80), ease));
            move.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromPercent(1)));
            look.BeginAnimation(TranslateTransform.XProperty, Loop(move));
        }
        return eye;
    }

    /// <summary>An open mouth (dark inside, a tongue at the bottom), singing when animated.</summary>
    static UIElement OpenMouth(string data, double pivotX, double pivotY, bool animate)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        var mouth = new Canvas { Width = 44, Height = 32 };
        var inner = new Canvas { Width = 44, Height = 32, Clip = geometry };
        inner.Children.Add(new Path { Data = geometry, Fill = Inside });
        var tongue = new Ellipse { Width = 11, Height = 6, Fill = Tongue };
        SetLeft(tongue, 16.5);
        SetTop(tongue, geometry.Bounds.Bottom - 4.2);
        inner.Children.Add(tongue);
        mouth.Children.Add(inner);
        mouth.Children.Add(new Path { Data = geometry, Stroke = White, StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round });
        if (animate)
        {
            var open = new ScaleTransform(1, 1, pivotX, pivotY);
            mouth.RenderTransform = open;
            open.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(0.25425))
            {
                AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            }));
        }
        return mouth;
    }

    static Path Arc(string data, double thickness) => new()
    {
        Data = Geometry.Parse(data), Stroke = White, StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
    };

    void Add(UIElement element, double left = 0, double top = 0)
    {
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
    }

    static T Loop<T>(T timeline) where T : Timeline
    {
        timeline.RepeatBehavior = RepeatBehavior.Forever;
        return timeline;
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
