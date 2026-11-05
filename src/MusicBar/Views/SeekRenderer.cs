using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// Draws the playback progress across the whole taskbar in one of several
/// styles. Everything is drawn with DrawingContext (GPU), redrawn 20-30 times a
/// second (spectrum bars 60) only while an animated style is playing, otherwise
/// only when the playhead moves a fraction of a pixel.
/// </summary>
public sealed class SeekRenderer : FrameworkElement
{
    const double FrameInterval = 1 / 30.0;
    const double ColorFade = 0.9;

    static readonly Color White = Colors.White;
    static readonly Color Black = Colors.Black;

    /// <summary>Hairline grain of holographic foil: a faint vertical line every 3 px.</summary>
    static readonly LinearGradientBrush Lenticular = Gradient(new Point(0, 0), new Point(3, 0),
        new[] { (Colors.White, 0.0, 0.0), (Colors.White, 0.05, 0.5), (Colors.White, 0.0, 1.0) }, GradientSpreadMethod.Repeat);

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly float[] _bands = new float[AudioMeter.BandCount];
    readonly float[] _wave = new float[AudioMeter.WaveCount];
    float _beat;

    SeekStyle _style = SeekStyle.Aurora;
    SeekDirection _direction = SeekDirection.FromStart;
    double _progress;
    bool _timeline;
    bool _playing;
    bool _edgeAtTop = true;
    double _intensity = 0.34;

    Color _fromA = ColorUtil.Brand, _fromB = ColorUtil.Brand, _toA = ColorUtil.Brand, _toB = ColorUtil.Brand;
    double _colorStart = -10;

    Color[]? _strip;
    BitmapSource? _blurArt;

    float _level;
    bool _liveAudio;
    float[] _peaks = [];

    double[] _pseudoWave = [];
    float[] _recorded = [];
    int _waveSeed = 1;

    WriteableBitmap? _silk;
    float[] _silkLight = [];
    byte[] _silkPixels = [];
    // The render pass the threads were last spun in: the two halves of a split bar share one spin.
    int _renderPass, _silkPass = -1, _peaksPass = -1;

    readonly List<(double Left, double Right)> _quietZones = [];

    // When the last redraw was asked for. Measured from the ask, not from the render, which comes a
    // little later: measured from the render, a tick right on time fell just short and was skipped.
    double _askedAt = -1;
    const double FrameSlack = 0.006;
    double _peaksAt = -1;
    double _lastPx = -1;
    bool _dirty = true;

    // The styles' own clock: the wall clock less the time it ran slow (or stood) while paused.
    const double IdleSpeed = 0.3;
    double _lastTick = -1, _pace = 1, _lag;

    public SeekRenderer()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        SizeChanged += (_, _) => _dirty = true;
        IsHitTestVisible = false;
    }

    /// <summary>Source of live audio; null makes the music-driven styles move on their own.</summary>
    public AudioMeter? Audio { get; set; }

    /// <summary>
    /// Paused, the animated styles keep drifting in place, slower and calmer, instead of standing
    /// still. The owner should keep ticking (at a lower rate is fine).
    /// </summary>
    public bool IdleMotion { get; set; } = true;

    public SeekStyle Look
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            if (value != SeekStyle.Silk)
            {
                _silk = null;
                _silkLight = [];
                _silkPixels = [];
            }
            _dirty = true;
        }
    }

    public SeekDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value) return;
            _direction = value;
            _dirty = true;
        }
    }

    bool Split => _direction is SeekDirection.FromCenter or SeekDirection.FromEnds;

    /// <summary>How far the playhead travels: the whole length, or half of it when the bar is split.</summary>
    double Run => Split ? Full / 2 : Full;

    /// <summary>The length the style is drawn along: the whole width, or the empty stretches joined end to end.</summary>
    double Full
    {
        get
        {
            if (_pieces.Count == 0) return ActualWidth;
            double sum = 0;
            foreach (var (left, right) in _pieces) sum += right - left;
            return sum;
        }
    }

    public static bool UsesAudio(SeekStyle s) => s is not (SeekStyle.Classic or SeekStyle.Ribbon or SeekStyle.Cinema);

    /// <summary>The styles that only dance to the music, showing no progress.</summary>
    public static bool IsVisualizer(SeekStyle s) => s >= SeekStyle.Equalizer;

    /// <summary>Styles drawn on every display frame while playing: anything jumping with the beat.</summary>
    public static bool WantsEveryFrame(SeekStyle s) => s == SeekStyle.Spectrum || IsVisualizer(s);

    static bool IsAnimated(SeekStyle s) => s != SeekStyle.Classic;

    /// <summary>
    /// Slow, drifting styles look the same at 20 fps and cost a third less. Spectrum bars jump with
    /// every beat and look stuttery below 60 (its owner then ticks on every display frame).
    /// </summary>
    public static double FrameIntervalFor(SeekStyle s) => s switch
    {
        SeekStyle.Cinema or SeekStyle.Ribbon or SeekStyle.Mesh or SeekStyle.Ink => 1 / 20.0,
        _ when WantsEveryFrame(s) => 1 / 60.0,
        _ => FrameInterval,
    };

    public void SetState(double progress, bool timeline, bool playing)
    {
        _progress = Math.Clamp(progress, 0, 1);
        if (timeline != _timeline || playing != _playing) _dirty = true;
        _timeline = timeline;
        _playing = playing;
    }

    public void SetEdge(bool edgeAtTop)
    {
        if (_edgeAtTop == edgeAtTop) return;
        _edgeAtTop = edgeAtTop;
        _dirty = true;
    }

    public double Intensity
    {
        get => _intensity;
        set
        {
            _intensity = Math.Clamp(value, 0.05, 1);
            _dirty = true;
        }
    }

    /// <param name="third">A palette's third color; without one it is drawn from the other two.</param>
    public void SetColors(Color primary, Color secondary, bool animate, Color? third = null)
    {
        double now = Now;
        var (a, b) = CurrentColors(now);
        var c = CurrentThird(now, a, b);
        _fromA = animate ? a : primary;
        _fromB = animate ? b : secondary;
        _fromC = animate ? c : third ?? DerivedThird(primary, secondary);
        _toA = primary;
        _toB = secondary;
        _toC = third ?? DerivedThird(primary, secondary);
        _colorStart = animate ? now : -10;
        _dirty = true;
    }

    Color _fromC = ColorUtil.Brand, _toC = ColorUtil.Brand;

    static Color DerivedThird(Color a, Color b) => ColorUtil.ShiftHue(ColorUtil.Lerp(a, b, 0.5), 48, 0.04);

    Color CurrentThird(double now, Color a, Color b)
    {
        double k = Math.Clamp((now - _colorStart) / ColorFade, 0, 1);
        k = k * k * (3 - 2 * k);
        return ColorUtil.Lerp(_fromC, _toC, k);
    }

    /// <summary>Album art for the Ribbon and Cinema styles.</summary>
    public void SetArt(BitmapSource? art)
    {
        _strip = null;
        _blurArt = null;
        _dirty = true;
        if (art == null) return;
        try
        {
            _strip = BuildStrip(art, 28);
            _blurArt = BuildBlur(art, 40, 10);
        }
        catch (Exception ex)
        {
            Log.Error("Preparing art for the seekbar failed", ex);
        }
    }

    /// <summary>
    /// Horizontal ranges (in this element's units) where text sits on top of the
    /// taskbar; the progress visuals fade almost away there so the text stays readable.
    /// </summary>
    public void SetQuietZones(IReadOnlyList<(double Left, double Right)> zones)
    {
        bool same = zones.Count == _quietZones.Count;
        for (int i = 0; same && i < zones.Count; i++)
            same = Math.Abs(zones[i].Left - _quietZones[i].Left) < 1 && Math.Abs(zones[i].Right - _quietZones[i].Right) < 1;
        if (same) return;
        _quietZones.Clear();
        _quietZones.AddRange(zones);
        _dirty = true;
    }

    /// <summary>
    /// The stretches (in this element's units) the bar keeps to when it stays clear of the taskbar's
    /// icons; empty for the whole width. The style is drawn once along them joined end to end and
    /// laid across them in order, so the progress runs on from one stretch into the next.
    /// </summary>
    public void SetPieces(IReadOnlyList<(double Left, double Right)> pieces)
    {
        bool same = pieces.Count == _pieces.Count;
        for (int i = 0; same && i < pieces.Count; i++)
            same = Math.Abs(pieces[i].Left - _pieces[i].Left) < 0.5 && Math.Abs(pieces[i].Right - _pieces[i].Right) < 0.5;
        if (same) return;
        _pieces.Clear();
        _pieces.AddRange(pieces);
        _fades = null;
        _dirty = true;
    }

    readonly List<(double Left, double Right)> _pieces = [];
    Brush?[]? _fades;
    double _fadesWidth;

    /// <summary>
    /// A stretch's soft ends where icons sit beside it (none at the taskbar's own ends), as an opacity
    /// mask in this element's units, or across the stretch's own box when <paramref name="relative"/>.
    /// </summary>
    internal static Brush? PieceFade(double left, double right, double width, bool relative = false)
    {
        const double feather = 14;
        bool fadeIn = left > 1, fadeOut = right < width - 1;
        double length = right - left;
        if ((!fadeIn && !fadeOut) || length <= 0) return null;
        double k = Math.Min(feather, length / 3) / length;
        var stops = new GradientStopCollection
        {
            new(fadeIn ? Colors.Transparent : Colors.Black, 0),
            new(Colors.Black, fadeIn ? k : 0),
            new(Colors.Black, fadeOut ? 1 - k : 1),
            new(fadeOut ? Colors.Transparent : Colors.Black, 1),
        };
        var brush = relative
            ? new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5))
            : new LinearGradientBrush(stops, new Point(left, 0), new Point(right, 0)) { MappingMode = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    LinearGradientBrush QuietMask(double width)
    {
        // The zone hugs the text now, so the seekbar can stay a little visible under it.
        const double feather = 28, quiet = 0.3;
        var stops = new GradientStopCollection { new(Colors.Black, 0), new(Colors.Black, 1) };
        foreach (var (left, right) in _quietZones)
        {
            stops.Add(new GradientStop(Colors.Black, (left - feather) / width));
            stops.Add(new GradientStop(ColorUtil.WithAlpha(Colors.Black, quiet), left / width));
            stops.Add(new GradientStop(ColorUtil.WithAlpha(Colors.Black, quiet), right / width));
            stops.Add(new GradientStop(Colors.Black, (right + feather) / width));
        }
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(width, 0)) { MappingMode = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    /// <summary>A new song: fresh pseudo-waveform, nothing recorded yet.</summary>
    public void SetTrack(string key)
    {
        _waveSeed = key.GetHashCode();
        _pseudoWave = [];
        _recorded = [];
        _dirty = true;
    }

    /// <summary>Overrides the wall clock, so frames can be rendered at exact times (video capture).</summary>
    public Func<double>? Clock { get; set; }

    double Now => Clock?.Invoke() ?? _clock.Elapsed.TotalSeconds;

    (Color A, Color B) CurrentColors(double now)
    {
        double k = Math.Clamp((now - _colorStart) / ColorFade, 0, 1);
        k = k * k * (3 - 2 * k);
        return (ColorUtil.Lerp(_fromA, _toA, k), ColorUtil.Lerp(_fromB, _toB, k));
    }

    // ------------------------------------------------------------------ for a skin
    // With a skin on, this keeps the clock, the audio and the colors and draws nothing itself.

    /// <summary>A skin draws instead: no redraws are asked for.</summary>
    public bool Skinned { get; set; }

    internal (Color A, Color B, Color C) ColorsNow()
    {
        double now = Now;
        var (a, b) = CurrentColors(now);
        return (a, b, CurrentThird(now, a, b));
    }

    /// <summary>The styles' own clock: seconds, running slow while paused.</summary>
    internal double StyleTime => Now - _lag;
    internal double ProgressNow => _timeline ? _progress : 0;
    internal float LevelNow => _level;
    internal float BeatNow => _beat;
    internal bool PlayingNow => _playing;
    internal bool EdgeAtTop => _edgeAtTop;
    internal float[] WaveNow => _wave;

    /// <summary>The spectrum at a point from 0 (the lowest notes) to 1 (the highest).</summary>
    internal double Band(double frac) => SampleBand(frac);

    /// <summary>The mask that keeps text readable where lyrics sit, for a skin to wear too; null without lyrics on the bar.</summary>
    internal Brush? QuietMaskFor(double width) => _quietZones.Count > 0 && width > 0 ? QuietMask(width) : null;

    /// <summary>Called every frame by the owner; decides whether a redraw is due.</summary>
    public void Tick()
    {
        double now = Now;
        double dt = _lastTick < 0 ? 0 : Math.Clamp(now - _lastTick, 0, 0.5);
        _lastTick = now;
        // Playing, the styles move with the wall clock; paused, they ease down to a slow drift (or
        // to a stop without idle motion) over about half a second, and back up when it plays again.
        double pace = _playing ? 1 : IdleMotion && IsAnimated(_style) ? IdleSpeed : 0;
        _pace += (pace - _pace) * (1 - Math.Exp(-dt * 5));
        _lag += dt * (1 - _pace);

        var audio = Audio;
        _liveAudio = audio is { IsCapturing: true };
        if (_liveAudio)
        {
            audio!.CopyBands(_bands);
            audio.CopyWave(_wave);
            _level = audio.Level;
            _beat = audio.Beat;
        }
        else if (_playing)
        {
            Synthesize(now);
        }
        else
        {
            Breathe(now - _lag, dt);
        }
        if (IsVisualizer(_style)) StepVisualizer(now - _lag, dt);

        if (_style == SeekStyle.Waveform) RecordWaveform();
        if (Skinned) return;

        bool fading = now - _colorStart < ColorFade;
        bool animating = (IsAnimated(_style) && (_playing || IdleMotion || _pace > 0.01)) || fading;
        double px = _progress * Run;
        if (animating)
        {
            if (now - _askedAt >= FrameIntervalFor(_style) - FrameSlack)
            {
                _askedAt = now;
                InvalidateVisual();
            }
        }
        else if (_dirty || Math.Abs(px - _lastPx) >= 1)
        {
            // Static styles: one redraw per pixel of progress is plenty (a few per second).
            InvalidateVisual();
        }
    }

    /// <summary>Music-like motion for when no live audio is available (previews, capture off).</summary>
    void Synthesize(double t)
    {
        double beat = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.9)), 6);
        for (int b = 0; b < _bands.Length; b++)
        {
            double tilt = 1 - b / (double)_bands.Length * 0.55;
            double v = 0.32 + 0.28 * Noise(t * 2.3 + b * 0.37) + 0.22 * Math.Sin(t * 3.1 + b * 0.5) * Noise(t * 0.8 + b) + beat * 0.25 * tilt;
            _bands[b] = (float)Math.Clamp(v * tilt + 0.05, 0, 1);
        }
        _level = (float)(0.45 + 0.25 * Noise(t * 1.7) + beat * 0.3);
        // A beat at 114 bpm, and a voice-like trace: a few partials drifting against each other.
        _beat = (float)Math.Pow(Math.Max(0, Math.Cos(t * Math.PI * 1.9)), 24);
        for (int i = 0; i < _wave.Length; i++)
        {
            double x = i / (double)_wave.Length * Math.PI * 2;
            _wave[i] = (float)(0.55 * Math.Sin(x * 3 + t * 2.1) + 0.25 * Math.Sin(x * 7.3 - t * 3.7) + 0.12 * Math.Sin(x * 13 + t * 5.3)) * (0.6f + _level * 0.4f);
        }
    }

    /// <summary>
    /// Paused: the music-driven styles settle from wherever the music left them to a low, slow
    /// breath, so they idle in place instead of dancing to nothing.
    /// </summary>
    void Breathe(double t, double dt)
    {
        double k = 1 - Math.Exp(-dt * 2.5);
        for (int b = 0; b < _bands.Length; b++)
        {
            double calm = 0.1 + 0.07 * Noise(t * 0.45 + b * 0.37) + 0.03 * Math.Sin(t * 0.9 + b * 0.5);
            _bands[b] += (float)((calm - _bands[b]) * k);
        }
        double level = 0.16 + 0.05 * Math.Sin(t * 0.8) + 0.04 * Noise(t * 0.5);
        _level += (float)((level - _level) * k);
        _beat *= (float)Math.Exp(-dt * 6);
        // The trace settles to a slow, shallow swell.
        for (int i = 0; i < _wave.Length; i++)
        {
            double calm = 0.18 * Math.Sin(i / (double)_wave.Length * Math.PI * 4 + t * 0.9);
            _wave[i] += (float)((calm - _wave[i]) * k);
        }
    }

    // ------------------------------------------------------------------ rendering

    protected override void OnRender(DrawingContext dc)
    {
        double now = Now;
        _dirty = false;
        double w = ActualWidth, h = ActualHeight;
        double full = Full;
        if (w < 4 || h < 4 || full < 4) return;
        _renderPass++;

        double run = IsVisualizer(_style) ? full : Run;
        double x = _timeline ? _progress * run : 0;
        _lastPx = x;
        var (a, b) = CurrentColors(now);
        var c = CurrentThird(now, a, b);
        double t = now - _lag;
        // Kept clear of the icons, the style is drawn once along the stretches joined end to end.
        var joined = _pieces.Count > 0 ? new DrawingGroup() : null;
        var target = joined?.Open() ?? dc;
        var f = new Frame(target, run, h, x, a, b, c, _intensity, _edgeAtTop, t, _level);

        // Quiet zones are in this element's units, so the mask goes on before any mirroring.
        bool quiet = _quietZones.Count > 0;
        if (quiet) dc.PushOpacityMask(QuietMask(w));
        // Every style draws from x = 0 rightwards; the other directions are that drawing
        // mirrored, or two half-width copies of it back to back. A visualizer has no progress
        // to run from anywhere: it takes the whole width, once.
        switch (IsVisualizer(_style) ? SeekDirection.FromStart : _direction)
        {
            case SeekDirection.FromStart:
                DrawStyle(f);
                break;
            case SeekDirection.FromEnd:
                DrawStyle(f, new Matrix(-1, 0, 0, 1, full, 0));
                break;
            case SeekDirection.FromCenter:
                DrawStyle(f, new Matrix(1, 0, 0, 1, run, 0));
                DrawStyle(f, new Matrix(-1, 0, 0, 1, run, 0));
                break;
            case SeekDirection.FromEnds:
                DrawStyle(f, Matrix.Identity);
                DrawStyle(f, new Matrix(-1, 0, 0, 1, full, 0));
                break;
        }
        if (joined != null)
        {
            target.Close();
            LayAcross(dc, joined, w, h);
        }
        if (quiet) dc.Pop();
    }

    /// <summary>The joined drawing cut into the stretches, in order, each fading out beside the icons.</summary>
    void LayAcross(DrawingContext dc, Drawing joined, double w, double h)
    {
        if (_fades == null || _fades.Length != _pieces.Count || Math.Abs(_fadesWidth - w) > 0.5)
        {
            _fades = new Brush?[_pieces.Count];
            for (int i = 0; i < _pieces.Count; i++) _fades[i] = PieceFade(_pieces[i].Left, _pieces[i].Right, w);
            _fadesWidth = w;
        }
        double at = 0;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var (left, right) = _pieces[i];
            double length = right - left;
            if (length <= 0) continue;
            dc.PushClip(new RectangleGeometry(new Rect(left, 0, length, h)));
            if (_fades[i] is { } fade) dc.PushOpacityMask(fade);
            dc.PushTransform(new TranslateTransform(left - at, 0));
            dc.DrawDrawing(joined);
            dc.Pop();
            if (_fades[i] != null) dc.Pop();
            dc.Pop();
            at += length;
        }
    }

    /// <summary>One copy of the style, placed by <paramref name="place"/> and kept inside its own run.</summary>
    void DrawStyle(Frame f, Matrix place)
    {
        f.Dc.PushTransform(new MatrixTransform(place));
        f.Dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.W, f.H)));
        DrawStyle(f);
        f.Dc.Pop();
        f.Dc.Pop();
    }

    void DrawStyle(Frame f)
    {
        switch (_style)
        {
            case SeekStyle.Classic: DrawClassic(f); break;
            case SeekStyle.Aurora: DrawAurora(f); break;
            case SeekStyle.Liquid: DrawLiquid(f); break;
            case SeekStyle.Neon: DrawNeon(f); break;
            case SeekStyle.Spectrum: DrawSpectrum(f); break;
            case SeekStyle.Waveform: DrawWaveform(f); break;
            case SeekStyle.Ribbon: DrawRibbon(f); break;
            case SeekStyle.Cinema: DrawCinema(f); break;
            case SeekStyle.Mosaic: DrawMosaic(f); break;
            case SeekStyle.Mesh: DrawMesh(f); break;
            case SeekStyle.Silk: DrawSilk(f); break;
            case SeekStyle.Holo: DrawHolo(f); break;
            case SeekStyle.Bokeh: DrawBokeh(f); break;
            case SeekStyle.Ink: DrawInk(f); break;
            case SeekStyle.Equalizer: DrawEqualizer(f); break;
            case SeekStyle.Oscilloscope: DrawOscilloscope(f); break;
            case SeekStyle.Horizon: DrawHorizon(f); break;
            case SeekStyle.Aura: DrawAura(f); break;
            case SeekStyle.Fireflies: DrawFireflies(f); break;
            case SeekStyle.Led: DrawLed(f); break;
        }
    }

    readonly record struct Frame(
        DrawingContext Dc, double W, double H, double X, Color A, Color B, Color C,
        double F, bool Top, double T, double Level)
    {
        /// <summary>The taskbar edge that faces the desktop.</summary>
        public double EdgeY => Top ? 0 : H;
        public double Inward => Top ? 1 : -1;
    }

    // 1. Classic: tinted fill, soft glow at the head, thin line along the edge.
    static void DrawClassic(Frame f)
    {
        var dc = f.Dc;
        if (f.X > 0.5)
        {
            dc.DrawRectangle(Horizontal(0, f.X, (f.B, f.F * 0.12, 0), (ColorUtil.Lerp(f.B, f.A, 0.6), f.F * 0.55, 0.72), (f.A, f.F, 1)),
                null, new Rect(0, 0, f.X, f.H));
            dc.DrawRectangle(Radial(f.X, f.H / 2, f.H * 2.3, f.H, (ColorUtil.Lerp(f.A, White, 0.15), Math.Min(0.85, 0.3 + f.F), 0), (f.A, 0, 1)),
                null, new Rect(f.X - f.H * 2.3, -f.H, f.H * 4.6, f.H * 3));
            dc.DrawRectangle(Vertical(f.EdgeY, f.EdgeY + f.Inward * f.H, (f.A, 1, 0), (f.A, 0.33, 0.22), (f.A, 0, 0.5)),
                null, new Rect(Math.Round(f.X) - 1, 0, 2, f.H));
        }
        double ey = f.Top ? 0 : f.H - 2;
        dc.DrawRectangle(Solid(White, 0.09), null, new Rect(0, ey, f.W, 2));
        if (f.X > 0) dc.DrawRectangle(Solid(f.A, 0.95), null, new Rect(0, ey, f.X, 2));
    }

    // 2. Aurora: three translucent ribbons drifting like northern lights.
    static void DrawAurora(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double fadeEnd = Math.Max(0, (f.X - 18) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.25, 0), (Black, 1, 0.35), (Black, 1, fadeEnd), (Black, 0.3, 1)));

        var colors = new[] { f.B, f.A, f.C };
        double amp = f.H * 0.2 * (1 + f.Level * 0.9);
        for (int i = 0; i < 3; i++)
        {
            double k = 2 * Math.PI / (f.W * (0.16 + 0.07 * i));
            double speed = 0.35 + 0.17 * i;
            double phase = i * 1.9;
            double thickness = f.H * (0.42 + 0.08 * i);
            var band = Vertical(0, f.H, (colors[i], 0, 0), (colors[i], 1, 0.5), (colors[i], 0, 1));
            for (int layer = 0; layer < 2; layer++)
            {
                double widen = layer == 0 ? 1.7 : 0.75;
                double alpha = layer == 0 ? f.F * 0.45 : f.F * 0.85;
                var geo = Ribbon(0, f.X + 8, 6,
                    xp => f.H * 0.5 + amp * (0.8 + 0.25 * i) * Math.Sin(k * xp + f.T * speed + phase)
                          + amp * 0.35 * Math.Sin(k * 2.3 * xp - f.T * speed * 1.4 + phase * 2),
                    xp => thickness * widen * (0.75 + 0.35 * Math.Sin(k * 0.6 * xp - f.T * speed * 0.7 + i)));
                dc.PushOpacity(Math.Min(1, alpha));
                dc.DrawGeometry(band, null, geo);
                dc.Pop();
            }
        }
        dc.Pop();
        dc.Pop();

        dc.DrawRectangle(Horizontal(f.X - 46, f.X, (f.A, 0, 0), (ColorUtil.Lerp(f.A, White, 0.4), 0.2 + f.F * 0.3, 1)),
            null, new Rect(Math.Max(0, f.X - 46), 0, Math.Min(46, f.X), f.H));
        dc.DrawRectangle(Vertical(0, f.H, (White, 0, 0), (White, 0.75, 0.5), (White, 0, 1)), null, new Rect(f.X - 0.75, 0, 1.5, f.H));
    }

    // 3. Liquid: wobbling meniscus at the head, bubbles rising through the fill.
    static void DrawLiquid(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        // One long swell plus a gentle ripple travelling along the surface: a smooth meniscus, not a zigzag.
        double amp = 2.4 * (1 + f.Level * 1.4);
        double Edge(double y) => f.X + amp * Math.Sin(y * 0.16 + f.T * 2.4) + amp * 0.3 * Math.Sin(y * 0.34 - f.T * 1.6)
                                 + amp * 0.35 * Math.Sin(f.T * 1.1);

        var body = new StreamGeometry();
        var surface = new StreamGeometry();
        using (var g = body.Open())
        using (var s = surface.Open())
        {
            g.BeginFigure(new Point(0, 0), true, true);
            s.BeginFigure(new Point(Edge(0), 0), false, false);
            for (double y = 0; y <= f.H + 0.01; y += 1.5)
            {
                var p = new Point(Edge(y), y);
                g.LineTo(p, false, false);
                s.LineTo(p, true, true);
            }
            g.LineTo(new Point(0, f.H), false, false);
        }
        body.Freeze();
        surface.Freeze();

        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.35, 0), (Black, 1, 0.3), (Black, 1, 1)));
        dc.DrawGeometry(Vertical(0, f.H, (ColorUtil.Lerp(f.A, White, 0.18), f.F * 1.15, 0), (f.A, f.F, 0.45), (f.B, f.F * 0.95, 1)), null, body);

        // Light caustics drifting inside the liquid.
        for (int i = 0; i < 2; i++)
        {
            double cx = (f.T * (18 + i * 11) + i * 300) % (f.X + 120) - 60;
            dc.DrawRectangle(Horizontal(cx - 50, cx + 50, (White, 0, 0), (White, 0.07, 0.5), (White, 0, 1)), null,
                new Rect(Math.Max(0, cx - 50), 0, Math.Max(0, Math.Min(100, f.X - cx + 50)), f.H));
        }

        for (int j = 0; j < 16; j++)
        {
            double speed = 5 + Hash(j * 3 + 2) * 9;
            double bx = Hash(j * 7 + 1) * Math.Max(0, f.X - 10) + 3 + Math.Sin(f.T * 1.7 + j) * 1.5;
            double by = f.H + 2 - ((f.T * speed / f.H + Hash(j * 11 + 5)) % 1.0) * (f.H + 4);
            double r = 0.7 + Hash(j * 5 + 9) * 1.3;
            if (bx > Edge(by) - 3) continue;
            dc.DrawEllipse(null, new Pen(Solid(White, 0.38), 0.6), new Point(bx, by), r, r);
            dc.DrawEllipse(Solid(White, 0.5), null, new Point(bx - r * 0.35, by - r * 0.35), r * 0.25, r * 0.25);
        }
        dc.Pop();

        dc.DrawGeometry(null, MakePen(ColorUtil.Lerp(f.A, White, 0.5), 0.25, 4), surface);
        dc.DrawGeometry(null, MakePen(White, 0.55, 1.1), surface);
    }

    // 4. Neon: a thin neon tube along the edge that hums and spills light.
    static void DrawNeon(Frame f)
    {
        var dc = f.Dc;
        double y = f.Top ? 3.5 : f.H - 3.5;
        double flicker = 0.9 + 0.1 * Noise(f.T * 9);
        if (Noise(f.T * 0.5 + 3) > 0.9) flicker *= 0.55 + 0.45 * Noise(f.T * 40);
        double glow = Math.Clamp(0.55 + f.F, 0.6, 1.2) * flicker * (0.9 + f.Level * 0.35);

        dc.DrawLine(MakePen(White, 0.08, 3), new Point(6, y), new Point(f.W - 6, y));
        if (f.X <= 6) return;

        var head = new Point(f.X, y);
        dc.DrawRectangle(Vertical(y, y + f.Inward * f.H * 0.95, (f.A, 0.42 * f.F * glow, 0), (f.A, 0, 1)), null, new Rect(0, 0, f.X, f.H));
        dc.DrawLine(MakePen(f.A, 0.07 * glow, 14), new Point(6, y), head);
        dc.DrawLine(MakePen(f.A, 0.16 * glow, 7), new Point(6, y), head);
        dc.DrawLine(MakePen(f.A, 0.45 * glow, 3.2), new Point(6, y), head);
        dc.DrawLine(MakePen(ColorUtil.Lerp(f.A, White, 0.65), 0.95, 1.4), new Point(6, y), head);

        dc.DrawEllipse(Radial(f.X, y, 12, 12, (White, 0.9 * flicker, 0), (f.A, 0.5, 0.3), (f.A, 0, 1)), null, head, 12, 12);
        for (int i = 0; i < 4; i++)
        {
            double sx = f.X + (Noise(f.T * 13 + i * 7) - 0.5) * 12;
            double sy = y + (Noise(f.T * 11 + i * 3) - 0.5) * 8;
            dc.DrawEllipse(Solid(White, 0.6 * Noise(f.T * 17 + i)), null, new Point(sx, sy), 0.7, 0.7);
        }
    }

    // 5. Spectrum: live frequency bars standing on the screen edge.
    void DrawSpectrum(Frame f)
    {
        var dc = f.Dc;
        const double barWidth = 4, gap = 2.2;
        int n = (int)((f.W + gap) / (barWidth + gap));
        if (n < 2) return;
        if (_peaks.Length != n) _peaks = new float[n];
        // The second half of a split bar draws the same peaks; they fall once per frame.
        bool fall = _peaksPass != _renderPass;
        _peaksPass = _renderPass;
        double offset = (f.W - (n * (barWidth + gap) - gap)) / 2;
        double baseY = f.Top ? f.H : 0;
        double maxH = f.H * 0.86;

        var played = Vertical(baseY, baseY - (f.Top ? 1 : -1) * maxH,
            (f.B, 0.5 + f.F * 0.5, 0), (f.A, 0.6 + f.F * 0.5, 0.55), (ColorUtil.Lerp(f.A, White, 0.35), 0.75 + f.F * 0.3, 1));
        // Caps fall at a steady speed whatever the frame rate.
        double now = Now;
        double fallen = fall && _peaksAt >= 0 && (_playing || IdleMotion) ? 0.36 * Math.Clamp(now - _peaksAt, 0, 0.1) : 0;
        if (fall) _peaksAt = now;

        // Every bar of a kind goes into one geometry: four draw calls a frame instead of two per bar.
        var playedBars = new StreamGeometry();
        var restBars = new StreamGeometry();
        var playedCaps = new StreamGeometry();
        var restCaps = new StreamGeometry();
        using (var pb = playedBars.Open())
        using (var rb = restBars.Open())
        using (var pc = playedCaps.Open())
        using (var rc = restCaps.Open())
        {
            for (int i = 0; i < n; i++)
            {
                double v = SampleBand(i / (double)(n - 1));
                if (fall) _peaks[i] = (float)Math.Max(v, _peaks[i] - fallen);
                double bh = Math.Max(1.5, maxH * v);
                double bx = offset + i * (barWidth + gap);
                bool isPlayed = bx + barWidth / 2 <= f.X;
                AddBar(isPlayed ? pb : rb, bx, barWidth, baseY, f.Top ? f.H - bh : bh, 1.6);

                double ph = Math.Max(2, maxH * _peaks[i]) + 2;
                double capY = f.Top ? f.H - ph - 1.5 : ph;
                AddRect(isPlayed ? pc : rc, bx, capY, barWidth, 1.5);
            }
        }
        dc.DrawGeometry(played, null, Frozen(playedBars));
        dc.DrawGeometry(Solid(White, 0.1), null, Frozen(restBars));
        dc.DrawGeometry(Solid(White, 0.65), null, Frozen(playedCaps));
        dc.DrawGeometry(Solid(White, 0.14), null, Frozen(restCaps));
    }

    static StreamGeometry Frozen(StreamGeometry g)
    {
        g.Freeze();
        return g;
    }

    /// <summary>A bar standing on the screen edge at <paramref name="baseY"/>, its free end rounded.</summary>
    static void AddBar(StreamGeometryContext g, double x, double width, double baseY, double endY, double radius)
    {
        double r = Math.Min(radius, Math.Min(width / 2, Math.Abs(baseY - endY) / 2));
        // Up from a bottom edge or down from a top one: the corners turn the other way.
        double toward = endY < baseY ? 1 : -1;
        var sweep = toward > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        g.BeginFigure(new Point(x, baseY), true, true);
        g.LineTo(new Point(x, endY + toward * r), false, false);
        g.ArcTo(new Point(x + r, endY), new Size(r, r), 0, false, sweep, false, false);
        g.LineTo(new Point(x + width - r, endY), false, false);
        g.ArcTo(new Point(x + width, endY + toward * r), new Size(r, r), 0, false, sweep, false, false);
        g.LineTo(new Point(x + width, baseY), false, false);
    }

    static void AddRect(StreamGeometryContext g, double x, double y, double width, double height)
    {
        g.BeginFigure(new Point(x, y), true, true);
        g.LineTo(new Point(x + width, y), false, false);
        g.LineTo(new Point(x + width, y + height), false, false);
        g.LineTo(new Point(x, y + height), false, false);
    }

    double SampleBand(double frac)
    {
        double pos = Math.Clamp(frac, 0, 1) * (_bands.Length - 1);
        int i = (int)pos;
        double k = pos - i;
        double v = i + 1 < _bands.Length ? _bands[i] * (1 - k) + _bands[i + 1] * k : _bands[i];
        return Math.Clamp(v, 0, 1);
    }

    // 6. Waveform: a SoundCloud-style waveform that records the real loudness as it plays.
    void DrawWaveform(Frame f)
    {
        var dc = f.Dc;
        const double barWidth = 2, gap = 1.3;
        int n = WaveBars(f.W);
        if (n < 2) return;
        EnsureWave(n);
        double offset = (f.W - (n * (barWidth + gap) - gap)) / 2;
        double mid = f.H / 2, maxH = f.H * 0.8;

        var played = Horizontal(0, Math.Max(1, f.X), (f.B, 0.55 + f.F * 0.45, 0), (f.A, 0.75 + f.F * 0.25, 1));
        var rest = Solid(White, 0.16);
        var recordedRest = Solid(White, 0.24);
        var head = Solid(White, 0.95);

        for (int i = 0; i < n; i++)
        {
            bool recorded = _recorded[i] > 0;
            double amp = recorded ? _recorded[i] : _pseudoWave[i];
            double bh = Math.Max(1.5, maxH * amp);
            double bx = offset + i * (barWidth + gap);
            Brush brush = Math.Abs(bx - f.X) < 2.5 && f.X > 0 ? head
                : bx < f.X ? played
                : recorded ? recordedRest : rest;
            dc.DrawRoundedRectangle(brush, null, new Rect(bx, mid - bh / 2, barWidth, bh), 1, 1);
        }
    }

    static int WaveBars(double width) => (int)((width + 1.3) / 3.3);

    void EnsureWave(int n)
    {
        if (_pseudoWave.Length == n) return;
        _pseudoWave = new double[n];
        _recorded = new float[n];
        var rng = new Random(_waveSeed);
        double seedOffset = rng.NextDouble() * 1000;
        for (int i = 0; i < n; i++)
        {
            double u = i / (double)(n - 1);
            double env = 0.35 + 0.25 * Smooth(Math.Clamp((u - 0.05) / 0.08, 0, 1));
            env += 0.25 * Bump(u, 0.3, 0.09) + 0.3 * Bump(u, 0.62, 0.1) + 0.25 * Bump(u, 0.85, 0.07);
            env *= 1 - 0.6 * Smooth(Math.Clamp((u - 0.94) / 0.06, 0, 1));
            double detail = 0.55 + 0.45 * Noise(i * 0.08 + seedOffset);
            double jitter = 0.82 + 0.3 * rng.NextDouble();
            _pseudoWave[i] = Math.Clamp(env * detail * jitter, 0.08, 1);
        }
    }

    void RecordWaveform()
    {
        if (!_playing || !_timeline || !_liveAudio || _recorded.Length == 0) return;
        int i = Math.Clamp((int)(_progress * _recorded.Length), 0, _recorded.Length - 1);
        float v = Math.Clamp(_level * 1.05f + 0.04f, 0.06f, 1f);
        _recorded[i] = Math.Max(_recorded[i], v);
        if (i > 0 && _recorded[i - 1] == 0) _recorded[i - 1] = v;
    }

    // 7. Ribbon: the cover's colours, column by column, revealed as the song plays.
    void DrawRibbon(Frame f)
    {
        var dc = f.Dc;
        Color[] strip = _strip ?? new[] { f.B, f.A, f.C };
        var stops = new GradientStopCollection();
        for (int i = 0; i < strip.Length; i++)
            stops.Add(new GradientStop(ColorUtil.WithAlpha(strip[i], 1), i / (double)(strip.Length - 1)));
        var full = new LinearGradientBrush(stops, new Point(0, 0), new Point(f.W, 0)) { MappingMode = BrushMappingMode.Absolute };
        full.Freeze();

        dc.PushOpacity(0.06);
        dc.DrawRectangle(full, null, new Rect(f.X, 0, Math.Max(0, f.W - f.X), f.H));
        dc.Pop();
        if (f.X < 1) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        dc.PushOpacity(Math.Min(1, 0.3 + f.F * 0.8));
        dc.DrawRectangle(full, null, new Rect(0, 0, f.W, f.H));
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.12, 0), (White, 0, 0.45), (Black, 0, 0.55), (Black, 0.16, 1)), null, new Rect(0, 0, f.X, f.H));
        double s = (f.T % 6) / 6;
        double sx = -160 + s * (f.X + 320);
        dc.DrawRectangle(Horizontal(sx - 80, sx + 80, (White, 0, 0), (White, 0.13, 0.5), (White, 0, 1)), null, new Rect(sx - 80, 0, 160, f.H));
        dc.Pop();

        var headColor = strip[Math.Clamp((int)(f.X / f.W * (strip.Length - 1)), 0, strip.Length - 1)];
        dc.DrawRectangle(Radial(f.X, f.H / 2, f.H * 1.4, f.H, (ColorUtil.Lerp(headColor, White, 0.5), 0.45, 0), (headColor, 0, 1)), null,
            new Rect(f.X - f.H * 1.4, 0, f.H * 2.8, f.H));
        dc.DrawRectangle(Solid(ColorUtil.Lerp(headColor, White, 0.6), 0.9), null, new Rect(f.X - 0.75, 0, 1.5, f.H));
    }

    // 8. Cinema: the cover, deeply blurred, drifting slowly behind the taskbar.
    void DrawCinema(Frame f)
    {
        if (_blurArt == null)
        {
            DrawRibbon(f);
            return;
        }
        var dc = f.Dc;
        double drift = Math.Sin(f.T * 0.06) * f.W * 0.03;
        var dest = new Rect(-f.W * 0.04 + drift, -f.H * 0.2, f.W * 1.08, f.H * 1.4);

        if (f.X < f.W)
        {
            dc.PushClip(new RectangleGeometry(new Rect(f.X, 0, f.W - f.X, f.H)));
            dc.PushOpacity(0.07);
            dc.DrawImage(_blurArt, dest);
            dc.Pop();
            dc.Pop();
        }
        if (f.X < 1) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double soft = Math.Max(0, (f.X - 30) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 1, 0), (Black, 1, soft), (Black, 0.35, 1)));
        dc.PushOpacity(Math.Min(1, 0.35 + f.F * 0.9));
        dc.DrawImage(_blurArt, dest);
        dc.Pop();
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (Black, 0.3, 0), (Black, 0, 0.35), (Black, 0, 0.65), (Black, 0.3, 1)), null, new Rect(0, 0, f.X, f.H));
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0, 0), (White, 0.8, 0.5), (White, 0, 1)), null, new Rect(f.X - 0.5, 0, 1, f.H));
    }

    // 9. Mosaic: an LED matrix that twinkles in the song's palette.
    static void DrawMosaic(Frame f)
    {
        var dc = f.Dc;
        int rows = f.H >= 40 ? 4 : 3;
        const double gap = 2;
        double size = (f.H - gap * (rows + 1)) / rows;
        if (size < 2) return;
        double step = size + gap;
        int cols = (int)((f.W - gap) / step);
        double offset = (f.W - (cols * step - gap)) / 2;
        double headCol = (f.X - offset) / step;
        var dim = Solid(White, 0.05);
        int lit = (int)Math.Ceiling(f.Level * rows);

        for (int col = 0; col < cols; col++)
        {
            double cx = offset + col * step;
            bool played = cx + size <= f.X;
            bool partial = !played && cx < f.X;
            double near = Math.Max(0, 1 - (headCol - col) / 7);
            var tint = ColorUtil.Lerp(ColorUtil.Lerp(f.B, f.A, col / (double)cols), White, near * 0.45);
            for (int row = 0; row < rows; row++)
            {
                double cy = gap + row * step;
                var rect = new Rect(cx, cy, size, size);
                if (played)
                {
                    double twinkle = 0.5 + 0.5 * Math.Sin(f.T * (0.7 + Hash(col * 31 + row) * 1.8) + Hash(col * 7 + row * 13) * 6.283);
                    double alpha = (0.28 + f.F * 0.9) * (0.55 + 0.45 * twinkle) + near * 0.25;
                    // Near the head the column doubles as a level meter.
                    if (near > 0 && rows - row > lit) alpha *= 0.55;
                    dc.DrawRoundedRectangle(Solid(tint, Math.Min(1, alpha)), null, rect, 1.6, 1.6);
                }
                else if (partial)
                {
                    dc.DrawRoundedRectangle(Solid(White, 0.15 + 0.6 * (f.X - cx) / size), null, rect, 1.6, 1.6);
                }
                else
                {
                    dc.DrawRoundedRectangle(dim, null, rect, 1.6, 1.6);
                }
            }
        }
    }

    // 10. Mesh: big soft pools of the cover's colours drifting into one another, breathing with the music.
    void DrawMesh(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        Color[] palette = _strip is { Length: > 0 } strip
            ? new[] { f.A, Vivid(strip[strip.Length / 6]), f.C, ColorUtil.ShiftHue(f.A, -35, 0.06), f.B, Vivid(strip[strip.Length * 2 / 3]) }
            : new[] { f.A, ColorUtil.ShiftHue(f.A, -35, 0.06), f.C, f.B, ColorUtil.ShiftHue(f.B, 30, 0.08), ColorUtil.Lerp(f.A, White, 0.35) };

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double soft = Math.Max(0, (f.X - 60) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.3, 0), (Black, 1, 0.25), (Black, 1, soft), (Black, 0.2, 1)));
        dc.PushOpacity(Math.Min(1, 0.3 + f.F * 0.85));
        dc.DrawRectangle(Horizontal(0, f.X, (f.B, 0.3, 0), (f.A, 0.4, 1)), null, new Rect(0, 0, f.X, f.H));

        int n = Math.Max(6, (int)(f.W / 140));
        double spacing = f.W / n;
        for (int j = 0; j < n; j++)
        {
            double cx = (j + 0.5) * spacing + (Noise(f.T * 0.09 + j * 3.7) - 0.5) * spacing * 1.6;
            double swell = 1 + 0.3 * f.Level * SampleBand(Hash(j * 5 + 2));
            double rx = spacing * (0.9 + 0.7 * Hash(j * 3 + 1)) * swell;
            if (cx - rx > f.X) continue;
            // Shorter than the bar, wandering up and down, so the colours also shift vertically.
            double ry = f.H * (0.5 + 0.35 * Hash(j * 7 + 4)) * swell;
            double cy = f.H * (0.5 + (Noise(f.T * 0.13 + j * 5.3) - 0.5) * 1.2);
            var c = palette[j % palette.Length];
            dc.DrawEllipse(Radial(cx, cy, rx, ry, (c, 0.95, 0), (c, 0.55, 0.45), (c, 0, 1)), null, new Point(cx, cy), rx, ry);
        }
        dc.Pop();
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.1, 0), (White, 0, 0.35), (Black, 0, 0.7), (Black, 0.14, 1)), null, new Rect(0, 0, f.X, f.H));
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0, 0), (White, 0.55, 0.5), (White, 0, 1)), null, new Rect(f.X - 0.5, 0, 1, f.H));
    }

    // 11. Silk: fine threads twisting around each other, spun into a single strand at the playhead.
    void DrawSilk(Frame f)
    {
        var dc = f.Dc;
        double mid = f.H / 2;
        if (f.X < f.W)
            dc.DrawRectangle(Horizontal(f.X, f.W, (White, 0.24, 0), (White, 0.09, 0.25), (White, 0.09, 1)), null, new Rect(f.X, mid - 0.5, f.W - f.X, 1));
        if (f.X < 2) return;

        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.1, 0), (Black, 1, 0.3), (Black, 1, 1)));
        dc.DrawRectangle(Vertical(0, f.H, (f.A, 0, 0.1), (f.A, f.F * 0.2, 0.5), (f.A, 0, 0.9)), null, new Rect(0, 0, f.X, f.H));
        var threads = SpinSilk(f, out double scale);
        dc.DrawImage(threads, new Rect(0, 0, threads.PixelWidth / scale, threads.PixelHeight / scale));
        dc.Pop();

        dc.DrawEllipse(Radial(f.X, mid, 16, 11, (White, 0.9, 0), (f.A, 0.45, 0.3), (f.A, 0, 1)), null, new Point(f.X, mid), 16, 11);
        dc.DrawRectangle(Horizontal(f.X - 26, f.X + 26, (White, 0, 0), (White, 0.75, 0.5), (White, 0, 1)), null, new Rect(f.X - 26, mid - 0.5, 52, 1));
    }

    /// <summary>
    /// Rasterises the silk threads by hand, one pixel column at a time. As WPF strokes the same
    /// curves cost several times the CPU (strokes are re-tessellated every frame); here light
    /// also adds up where threads cross, which is what makes them read as silk.
    /// </summary>
    WriteableBitmap SpinSilk(Frame f, out double scale)
    {
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int pw = Math.Max(1, (int)Math.Ceiling(f.W * scale)), ph = Math.Max(1, (int)Math.Ceiling(f.H * scale));
        if (_silk == null || _silk.PixelWidth != pw || _silk.PixelHeight != ph)
        {
            _silk = new WriteableBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
            _silkLight = new float[pw * ph * 4];
            _silkPixels = new byte[pw * ph * 4];
        }
        else if (_silkPass == _renderPass)
        {
            return _silk;
        }
        _silkPass = _renderPass;
        var light = _silkLight;
        Array.Clear(light);

        const int threads = 14;
        double s = scale, mid = f.H / 2;
        double gather = Math.Min(260, f.X * 0.7);
        double amp = f.H * 0.38 * (0.85 + f.Level * 0.45);
        double core = s, halo = 1.6 * s;
        float strength = (float)Math.Min(1, 0.35 + f.F * 0.55);
        int last = Math.Min(pw - 1, (int)(f.X * s));
        for (int i = 0; i < threads; i++)
        {
            double u = i / (double)(threads - 1);
            var color = ColorUtil.Lerp(u < 0.5 ? ColorUtil.Lerp(f.B, f.A, u * 2) : ColorUtil.Lerp(f.A, f.C, u * 2 - 1), White, 0.2);
            float cr = color.R / 255f, cg = color.G / 255f, cb = color.B / 255f;
            double k = 2 * Math.PI / (f.W * (0.1 + 0.18 * Hash(i * 3 + 1)) + 50);
            double speed = 0.45 + 0.7 * Hash(i * 7 + 2);
            double phase = Hash(i * 13 + 5) * 2 * Math.PI;
            double a = amp * (0.45 + 0.55 * Hash(i * 5 + 3));
            double Y(double x) => s * (mid + a * Smooth(Math.Clamp((f.X - x) / gather, 0, 1))
                * (0.8 * Math.Sin(k * x - f.T * speed + phase) + 0.25 * Math.Sin(2.7 * k * x + f.T * speed * 0.6 + phase * 1.7)));

            double y0 = Y(0);
            for (int px = 0; px <= last; px++)
            {
                double y1 = Y((px + 1) / s);
                // The segment through this column, as a vertical span: full coverage inside the core,
                // fading out over a short halo either side.
                double lo = Math.Min(y0, y1) - core / 2, hi = Math.Max(y0, y1) + core / 2;
                int r0 = Math.Max(0, (int)(lo - halo)), r1 = Math.Min(ph - 1, (int)(hi + halo));
                for (int row = r0; row <= r1; row++)
                {
                    double cover = Math.Clamp(Math.Min(hi, row + 1) - Math.Max(lo, row), 0, 1);
                    double centre = row + 0.5;
                    double gap = centre < lo ? lo - centre : centre > hi ? centre - hi : 0;
                    double glow = Math.Max(0, 1 - gap / halo) * 0.16;
                    float w = (float)Math.Min(1, cover + glow) * strength;
                    if (w <= 0) continue;
                    int p = (row * pw + px) * 4;
                    light[p] += cb * w;
                    light[p + 1] += cg * w;
                    light[p + 2] += cr * w;
                    light[p + 3] += w;
                }
                y0 = y1;
            }
        }

        // Soft tone mapping: crossings grow denser in the threads' own colours instead of clipping to
        // white. Scaling every channel by the same factor keeps the premultiplied pixel valid.
        var pixels = _silkPixels;
        for (int p = 0; p < light.Length; p += 4)
        {
            float sum = light[p + 3];
            float factor = sum > 0 ? (1 - MathF.Exp(-1.3f * sum)) / sum * 255 : 0;
            pixels[p] = (byte)(light[p] * factor);
            pixels[p + 1] = (byte)(light[p + 1] * factor);
            pixels[p + 2] = (byte)(light[p + 2] * factor);
            pixels[p + 3] = (byte)(sum * factor);
        }
        _silk.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, pw * 4, 0);
        return _silk;
    }

    // 12. Holo: holographic foil shifting around the song's hue, with light gliding over it.
    static void DrawHolo(Frame f)
    {
        var dc = f.Dc;
        ColorUtil.ToHsl(f.A, out double hue, out _, out _);
        double spread = 1 + f.Level * 0.6;
        double drift = f.T * 24;
        var foil = Gradient(new Point(drift, 0), new Point(drift + 150, f.H * 2.4), new[]
        {
            (ColorUtil.FromHsl(hue - 60 * spread, 0.7, 0.68), 1.0, 0.0),
            (ColorUtil.FromHsl(hue - 15, 0.85, 0.64), 1.0, 0.28),
            (ColorUtil.FromHsl(hue + 35 * spread, 0.8, 0.7), 1.0, 0.55),
            (ColorUtil.FromHsl(hue + 110, 0.65, 0.76), 1.0, 0.8),
            (ColorUtil.FromHsl(hue + 170, 0.55, 0.8), 1.0, 1.0),
        }, GradientSpreadMethod.Reflect);

        if (f.X < f.W)
        {
            dc.PushOpacity(0.05);
            dc.DrawRectangle(foil, null, new Rect(f.X, 0, f.W - f.X, f.H));
            dc.Pop();
        }
        if (f.X < 1) return;

        var played = new Rect(0, 0, f.X, f.H);
        dc.PushClip(new RectangleGeometry(played));
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.35, 0), (Black, 1, 0.3), (Black, 1, 1)));
        dc.PushOpacity(Math.Min(1, 0.25 + f.F * 0.8));
        dc.DrawRectangle(foil, null, played);
        dc.Pop();
        dc.DrawRectangle(Lenticular, null, played);

        // Thin glints tilted against the colour bands, sliding the other way.
        var glints = Gradient(new Point(-drift * 1.7, f.H * 2), new Point(-drift * 1.7 + 120, 0),
            new[] { (White, 0.0, 0.0), (White, 0.0, 0.62), (White, 0.2 + f.Level * 0.15, 0.8), (White, 0.0, 0.98) }, GradientSpreadMethod.Repeat);
        dc.DrawRectangle(glints, null, played);

        // Every few seconds a broad light sweeps across the foil.
        double sx = -150 + (f.T % 6.5) / 6.5 * (f.X + 300);
        dc.DrawRectangle(Gradient(new Point(sx - 70, f.H), new Point(sx + 70, 0),
            new[] { (White, 0.0, 0.0), (White, 0.32, 0.5), (White, 0.0, 1.0) }), null, played);
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.16, 0), (White, 0, 0.4), (Black, 0, 0.6), (Black, 0.2, 1)), null, played);
        dc.Pop();
        dc.Pop();

        double mid = f.H / 2;
        var sheen = ColorUtil.FromHsl(hue + 35, 0.8, 0.8);
        dc.DrawEllipse(Radial(f.X, mid, 9, f.H * 0.75, (White, 0.55, 0), (sheen, 0.25, 0.45), (sheen, 0, 1)), null, new Point(f.X, mid), 9, f.H * 0.75);
        dc.DrawRectangle(Solid(White, 0.85), null, new Rect(f.X - 0.5, 0, 1, f.H));
    }

    // 13. Bokeh: out-of-focus lights at different depths, each pulsing with its own slice of the spectrum.
    void DrawBokeh(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        var lights = new[] { Vivid(f.A), Vivid(f.B), Vivid(f.C), ColorUtil.Lerp(Vivid(f.A), White, 0.5), ColorUtil.Lerp(Vivid(f.B), White, 0.35) };
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double soft = Math.Max(0, (f.X - 40) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.45, 0), (Black, 1, 0.2), (Black, 1, soft), (Black, 0.25, 1)));
        dc.DrawRectangle(Horizontal(0, f.X, (f.B, f.F * 0.06, 0), (f.A, f.F * 0.14, 1)), null, new Rect(0, 0, f.X, f.H));

        int n = Math.Max(24, (int)(f.W / 40));
        double strength = 0.6 + f.F * 0.9;
        // Near lights (large, soft) first; the far, crisp ones sit on top.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = 0; j < n; j++)
            {
                double depth = Hash(j * 3 + 7);
                bool near = depth > 0.55;
                if (near != (pass == 0)) continue;
                double band = SampleBand(Hash(j * 17 + 9));
                double r = f.H * (0.14 + 0.7 * depth * depth) * (1 + 0.08 * band);
                double cx = Hash(j * 11 + 1) * f.W + Math.Sin(f.T * (0.12 + 0.1 * depth) + j) * (4 + 10 * depth);
                if (cx - r > f.X) continue;
                double cy = f.H * (0.18 + 0.64 * Hash(j * 5 + 3)) + Math.Sin(f.T * 0.21 + j * 1.3) * f.H * 0.05;
                double a = strength * (1 - depth * 0.35) * (0.5 + 0.7 * band);
                var c = lights[j % lights.Length];
                var brush = near
                    ? Radial(cx, cy, r, r, (c, a * 0.6, 0), (c, a * 0.6, 0.7), (c, a * 0.3, 0.9), (c, 0, 1))
                    : Radial(cx, cy, r, r, (c, a * 0.65, 0), (c, a * 0.8, 0.7), (ColorUtil.Lerp(c, White, 0.4), a, 0.86), (c, 0, 1));
                dc.DrawEllipse(brush, null, new Point(cx, cy), r, r);
            }
        }
        dc.Pop();
        dc.Pop();

        // The playhead is the one light in focus.
        double mid = f.H / 2;
        dc.DrawEllipse(Radial(f.X, mid, f.H * 0.7, f.H * 0.6, (White, 0.55, 0), (f.A, 0.25, 0.4), (f.A, 0, 1)), null, new Point(f.X, mid), f.H * 0.7, f.H * 0.6);
        dc.DrawEllipse(Solid(White, 0.95), null, new Point(f.X, mid), 2.2, 2.2);
    }

    // 14. Ink: watercolour bleeding out of the playhead; each drop keeps spreading after the head has passed.
    void DrawInk(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        double splash = 1 + f.Level * 0.8;
        double Edge(double y) => f.X - 5
            + 14 * splash * (Noise(y * 0.08 + f.T * 0.22) - 0.5)
            + 6 * (Noise(y * 0.22 - f.T * 0.35 + 7) - 0.5)
            + 1.5 * (Noise(y * 0.5 + f.T * 0.6 + 13) - 0.5);

        var palette = new[] { f.A, f.B, f.C, ColorUtil.ShiftHue(f.A, -25, -0.04) };
        double strength = 0.3 + f.F * 0.7;
        dc.DrawGeometry(Solid(f.A, 0.05 + f.F * 0.1), null, EdgeShape(f.H, y => Edge(y) + 6, closed: true));

        dc.PushClip(EdgeShape(f.H, Edge, closed: true));
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.4, 0), (Black, 1, 0.35), (Black, 1, 1)));
        // Uneven pigment, as if the wash had dried in patches.
        var wash = new GradientStopCollection();
        for (int i = 0; i <= 12; i++)
        {
            double u = i / 12.0;
            double density = 0.2 + 0.3 * Noise(i * 1.7 + 3);
            wash.Add(new GradientStop(ColorUtil.WithAlpha(ColorUtil.Lerp(f.B, f.A, u), Math.Min(1, density * (0.5 + f.F))), u));
        }
        var washBrush = new LinearGradientBrush(wash, new Point(0, 0), new Point(f.X + 12, 0)) { MappingMode = BrushMappingMode.Absolute };
        washBrush.Freeze();
        dc.DrawRectangle(washBrush, null, new Rect(0, 0, f.X + 14, f.H));

        // Drops dropped at regular places along the bar; how far each has spread depends on
        // how long ago the playhead passed it, so the newest are still blooming open.
        const double spacing = 34;
        int n = (int)((f.X + 20) / spacing) + 1;
        for (int j = 0; j < n; j++)
        {
            double cx = (j + Hash(j * 7 + 3)) * spacing;
            double age = f.X - cx;
            if (age < -6) continue;
            double grow = 1 - Math.Exp(-(age + 6) / 32);
            double ry = f.H * (0.35 + 0.35 * Hash(j * 3 + 1)) * (0.3 + 0.7 * grow);
            double rx = ry * (1.6 + 1.4 * Hash(j * 5 + 2));
            double cy = f.H * (0.25 + 0.5 * Hash(j * 11 + 4));
            var c = palette[j % palette.Length];
            double a = strength * (0.5 + 0.5 * Math.Exp(-Math.Max(0, age) / 500));
            Bloom(cx, cy, rx, ry, c, a, j);
        }

        // A wobbly blot: pigment pools toward its rim and dries into a darker tide line.
        void Bloom(double cx, double cy, double rx, double ry, Color c, double a, int seed)
        {
            const int points = 36;
            var blot = new StreamGeometry();
            using (var g = blot.Open())
            {
                for (int k = 0; k < points; k++)
                {
                    double th = 2 * Math.PI * k / points;
                    double wob = 1 + 0.13 * Math.Sin(3 * th + seed * 1.7) + 0.08 * Math.Sin(5 * th + seed * 2.9 + f.T * 0.15)
                                 + 0.05 * Math.Sin(8 * th + seed * 4.3);
                    var p = new Point(cx + Math.Cos(th) * rx * wob, cy + Math.Sin(th) * ry * wob);
                    if (k == 0) g.BeginFigure(p, true, true);
                    else g.LineTo(p, true, true);
                }
            }
            blot.Freeze();
            var fill = Radial(cx, cy, rx * 1.25, ry * 1.25,
                (ColorUtil.Lerp(c, White, 0.12), a * 0.22, 0), (c, a * 0.4, 0.5), (c, a * 0.75, 0.85), (c, a * 0.9, 1));
            dc.DrawGeometry(fill, MakePen(ColorUtil.Lerp(c, White, 0.2), a * 0.5, 0.8), blot);
        }
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.06, 0), (White, 0, 0.4), (Black, 0, 0.6), (Black, 0.12, 1)), null, new Rect(0, 0, f.X + 14, f.H));
        dc.Pop();

        // Fine fingers of ink creeping ahead of the wet edge.
        for (int k = 0; k < 3; k++)
        {
            double ty = f.H * (0.15 + 0.7 * Noise(f.T * 0.05 + k * 9.1));
            double ex = Edge(ty);
            double length = (4 + 12 * Noise(f.T * 0.25 + k * 4.3)) * splash;
            double tilt = (Noise(f.T * 0.2 + k * 2.9) - 0.5) * 6;
            var pen = new Pen(Horizontal(ex - 2, ex + length, (f.A, 0.5 * strength, 0), (f.A, 0, 1)), 1) { EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawLine(pen, new Point(ex - 2, ty), new Point(ex + length, ty + tilt));
        }
        dc.DrawGeometry(null, MakePen(ColorUtil.Lerp(f.A, White, 0.3), 0.15 + f.F * 0.3, 1.1), EdgeShape(f.H, Edge, closed: false));
    }

    // ------------------------------------------------------------------ art processing

    static Color[] BuildStrip(BitmapSource art, int count)
    {
        var small = Pixels(art, count, 8, out int w, out int h);
        var colors = new Color[w];
        for (int x = 0; x < w; x++)
        {
            double r = 0, g = 0, b = 0;
            for (int y = 0; y < h; y++)
            {
                int i = (y * w + x) * 4;
                b += small[i];
                g += small[i + 1];
                r += small[i + 2];
            }
            var avg = Color.FromRgb((byte)(r / h), (byte)(g / h), (byte)(b / h));
            ColorUtil.ToHsl(avg, out var hue, out var sat, out var light);
            colors[x] = ColorUtil.FromHsl(hue, Math.Min(1, sat * 1.25 + 0.05), Math.Clamp(light, 0.32, 0.72));
        }
        return colors;
    }

    /// <summary>A cover colour pushed toward something that glows on a dark bar.</summary>
    // ------------------------------------------------------------------ visualizers
    // They only dance to the music: no progress, the whole width, a frame for every display frame.

    // Falling peak caps (Equalizer, LED) and the fireflies, carried from frame to frame.
    float[] _vizPeaks = [];
    double _vizPeaksAt = -1;
    readonly List<Firefly> _flies = [];
    readonly Random _rng = new(7);
    double _spawnDebt;
    float _lastBeat;
    readonly Dictionary<(Color, int), Brush> _glows = [];

    sealed class Firefly
    {
        // Y is the height above the screen edge.
        public double X, Y, Vx, Vy, Age, Life, Size, Phase;
        public int Hue;
    }

    /// <summary>Advances what lives from frame to frame: the fireflies' flight.</summary>
    void StepVisualizer(double t, double dt)
    {
        if (_style != SeekStyle.Fireflies)
        {
            _flies.Clear();
            return;
        }
        double w = Full, h = ActualHeight;
        if (w < 4 || h < 4) return;
        // New ones rise with the music: steadily with its loudness, in a burst on each beat.
        _spawnDebt += dt * (4 + 46 * _level) * (_playing ? 1 : 0.15);
        if (_beat > 0.85f && _lastBeat <= 0.85f) _spawnDebt += 7 + 10 * _level;
        _lastBeat = _beat;
        while (_spawnDebt >= 1 && _flies.Count < 180)
        {
            _spawnDebt -= 1;
            // Low notes rise near the middle, high ones toward the ends, each as busy as its band.
            double band = _rng.NextDouble();
            double side = _rng.Next(2) == 0 ? -1 : 1;
            _flies.Add(new Firefly
            {
                X = w / 2 + side * band * w / 2 + (_rng.NextDouble() - 0.5) * 30,
                Vx = (_rng.NextDouble() - 0.5) * 14,
                Vy = h * (0.45 + 0.8 * _rng.NextDouble()) * (0.6 + SampleBand(band)),
                Life = 1.1 + 1.6 * _rng.NextDouble(),
                Size = 0.8 + 1.5 * _rng.NextDouble(),
                Phase = _rng.NextDouble() * 10,
                Hue = _rng.Next(3),
            });
        }
        _spawnDebt = Math.Min(_spawnDebt, 1);
        for (int i = _flies.Count - 1; i >= 0; i--)
        {
            var fly = _flies[i];
            fly.Age += dt;
            if (fly.Age >= fly.Life)
            {
                _flies.RemoveAt(i);
                continue;
            }
            fly.Y += fly.Vy * dt;
            fly.X += (fly.Vx + 10 * Math.Sin(t * 2.3 + fly.Phase)) * dt;
            fly.Vy *= Math.Exp(-dt * 0.6);
        }
    }

    /// <summary>Peak caps fall at a steady speed whatever the frame rate.</summary>
    double FallPeaks(int count, double speed)
    {
        if (_vizPeaks.Length != count) _vizPeaks = new float[count];
        double now = Now;
        double fallen = _vizPeaksAt >= 0 ? speed * Math.Clamp(now - _vizPeaksAt, 0, 0.1) : 0;
        _vizPeaksAt = now;
        return fallen;
    }

    /// <summary>The spectrum folded around the middle: 0 at the centre (the bass) to 1 at either end (the treble).</summary>
    static double FromMiddle(int i, int count) => Math.Abs((i + 0.5) / count * 2 - 1);

    // 15. Equalizer: bars standing on the screen edge, the bass in the middle and the treble out
    // to both ends, balanced around a taskbar's centred icons.
    void DrawEqualizer(Frame f)
    {
        var dc = f.Dc;
        const double barWidth = 3.2, gap = 2.4;
        int n = (int)((f.W + gap) / (barWidth + gap)) & ~1;
        if (n < 4) return;
        double fallen = FallPeaks(n, 0.55);
        double offset = (f.W - (n * (barWidth + gap) - gap)) / 2;
        double baseY = f.Top ? f.H : 0, toward = f.Top ? -1 : 1;
        double maxH = f.H * 0.92;
        double lift = 1 + _beat * 0.08;

        var bars = new StreamGeometry();
        var caps = new StreamGeometry();
        using (var g = bars.Open())
        using (var c = caps.Open())
        {
            for (int i = 0; i < n; i++)
            {
                double v = Math.Clamp(Math.Pow(SampleBand(FromMiddle(i, n)), 0.85) * lift, 0, 1);
                _vizPeaks[i] = (float)Math.Max(v, _vizPeaks[i] - fallen);
                double bh = Math.Max(1.5, maxH * v);
                double bx = offset + i * (barWidth + gap);
                AddBar(g, bx, barWidth, baseY, baseY + toward * bh, barWidth / 2);
                double ph = Math.Max(2, maxH * _vizPeaks[i]) + 2.5;
                AddRect(c, bx, f.Top ? f.H - ph - 1.6 : ph, barWidth, 1.6);
            }
        }
        // A soft bloom off the screen edge, a little brighter on the beat.
        dc.DrawRectangle(Vertical(baseY, baseY + toward * f.H, (f.A, 0.1 + 0.16 * _beat, 0), (f.A, 0, 1)), null, new Rect(0, 0, f.W, f.H));
        dc.DrawGeometry(Vertical(baseY, baseY + toward * maxH,
            (f.B, 0.6 + f.F * 0.4, 0), (f.A, 0.85 + f.F * 0.15, 0.5), (ColorUtil.Lerp(f.C, White, 0.35), 1, 1)), null, Frozen(bars));
        dc.DrawGeometry(Solid(White, 0.75), null, Frozen(caps));
    }

    // 16. Oscilloscope: the sound itself, a glowing trace across the bar, its ends pinned to the middle.
    void DrawOscilloscope(Frame f)
    {
        var dc = f.Dc;
        double mid = f.H / 2;
        double amp = f.H * 0.42 * (0.35 + 0.65 * Math.Min(1, _level * 1.3));
        int n = _wave.Length;
        Point At(int i)
        {
            double u = i / (double)(n - 1);
            return new Point(u * f.W, mid - _wave[i] * amp * Math.Pow(Math.Sin(Math.PI * u), 0.35));
        }
        var trace = new StreamGeometry();
        using (var g = trace.Open())
        {
            g.BeginFigure(At(0), false, false);
            // Through the midpoints between samples: a smooth curve rather than a zigzag.
            var points = new List<Point>(2 * n);
            for (int i = 1; i < n - 1; i++)
            {
                Point p = At(i), q = At(i + 1);
                points.Add(p);
                points.Add(new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2));
            }
            g.PolyQuadraticBezierTo(points, true, true);
        }
        trace.Freeze();
        dc.DrawRectangle(Horizontal(0, f.W, (White, 0, 0), (White, 0.07, 0.5), (White, 0, 1)), null, new Rect(0, Math.Round(mid), f.W, 1));
        var ink = Horizontal(0, f.W, (f.B, 1, 0), (f.A, 1, 0.5), (f.C, 1, 1));
        foreach (var (alpha, width) in new[] { (0.12 + 0.1 * _beat, 7.0), (0.32, 3.2), (0.95, 1.4) })
        {
            dc.PushOpacity(alpha);
            dc.DrawGeometry(null, new Pen(ink, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, trace);
            dc.Pop();
        }
    }

    // 17. Horizon: the spectrum as a smooth ridge mirrored on the bar's middle line, the bass at the centre.
    void DrawHorizon(Frame f)
    {
        var dc = f.Dc;
        double mid = f.H / 2, amp = f.H * 0.47;
        const int count = 72;
        var top = new Point[count];
        var bottom = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double u = i / (double)(count - 1);
            double d = Math.Abs(u * 2 - 1);
            // Tapered at the ends, so the ridge meets the line instead of stopping in mid-air.
            double v = SampleBand(d) * Math.Pow(1 - d, 0.3) * (1 + _beat * 0.12);
            double y = Math.Max(0.5, v * amp);
            top[i] = new Point(u * f.W, mid - y);
            bottom[count - 1 - i] = new Point(u * f.W, mid + y);
        }
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(top[0], true, true);
            AddSmooth(g, top);
            g.LineTo(bottom[0], true, true);
            AddSmooth(g, bottom);
        }
        body.Freeze();
        var edge = new StreamGeometry();
        using (var g = edge.Open())
        {
            g.BeginFigure(top[0], false, false);
            AddSmooth(g, top);
            g.BeginFigure(bottom[0], false, false);
            AddSmooth(g, bottom);
        }
        edge.Freeze();
        dc.DrawGeometry(Vertical(mid - amp, mid + amp,
            (f.B, 0.1, 0), (f.A, 0.55 + f.F * 0.35, 0.42), (ColorUtil.Lerp(f.A, White, 0.25), 0.95, 0.5),
            (f.A, 0.55 + f.F * 0.35, 0.58), (f.B, 0.1, 1)), null, body);
        dc.DrawGeometry(null, MakePen(ColorUtil.Lerp(f.C, White, 0.4), 0.7, 1), edge);
        dc.DrawRectangle(Horizontal(0, f.W, (f.C, 0, 0), (White, 0.55 + 0.3 * _beat, 0.5), (f.C, 0, 1)), null, new Rect(0, mid - 0.5, f.W, 1));
    }

    /// <summary>A smooth curve on through <paramref name="points"/> (Catmull-Rom), from the first, already reached.</summary>
    static void AddSmooth(StreamGeometryContext g, IReadOnlyList<Point> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Point p0 = points[Math.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Math.Min(points.Count - 1, i + 2)];
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            g.BezierTo(c1, c2, p2, true, true);
        }
    }

    // 18. Aura: soft lights rising off the screen edge, each swelling with its part of the music:
    // the bass in the middle, the treble at the ends. A sheen crosses the bar on the beat.
    void DrawAura(Frame f)
    {
        var dc = f.Dc;
        double[] at = [0.5, 0.33, 0.67, 0.16, 0.84, 0.02, 0.98];
        double[] part = [0.03, 0.24, 0.3, 0.52, 0.58, 0.82, 0.88];
        var colors = new[] { f.A, f.B, f.C, f.C, f.A, f.B, f.C };
        double cy = f.Top ? f.H : 0;
        for (int i = 0; i < at.Length; i++)
        {
            double e = SampleBand(part[i]);
            double cx = at[i] * f.W + Math.Sin(f.T * (0.21 + 0.07 * i) + i * 1.7) * f.W * 0.025;
            double rx = f.W * (0.06 + 0.12 * e) * (i == 0 ? 1.35 : 1);
            double ry = f.H * (0.9 + 1.5 * e);
            double a = Math.Min(1, (0.16 + 0.66 * e) * (0.75 + f.F * 0.75));
            var c = colors[i];
            dc.DrawRectangle(Radial(cx, cy, rx, ry, (ColorUtil.Lerp(c, White, 0.25 * e), a, 0), (c, a * 0.5, 0.4), (c, 0, 1)),
                null, new Rect(cx - rx, cy - ry, 2 * rx, 2 * ry));
        }
        if (_beat > 0.02)
            dc.DrawRectangle(Vertical(cy, f.H - cy, (White, 0.13 * _beat, 0), (White, 0, 1)), null, new Rect(0, 0, f.W, f.H));
    }

    // 19. Fireflies: sparks rising off the screen edge, more as the music gets louder and a burst on every beat.
    void DrawFireflies(Frame f)
    {
        var dc = f.Dc;
        double edge = f.Top ? f.H : 0;
        dc.DrawRectangle(Vertical(edge, f.H - edge, (f.A, 0.1 + 0.18 * f.Level, 0), (f.A, 0, 1)), null, new Rect(0, 0, f.W, f.H));
        var colors = new[] { f.A, f.B, ColorUtil.Lerp(f.C, White, 0.3) };
        foreach (var fly in _flies)
        {
            double k = fly.Age / fly.Life;
            double fade = Math.Min(1, k * 6) * (1 - Smooth(k));
            double y = f.Top ? f.H - fly.Y : fly.Y;
            if (y < -6 || y > f.H + 6 || fade <= 0.02) continue;
            var c = colors[fly.Hue];
            double r = fly.Size * (1 + 0.45 * Math.Sin(f.T * 9 + fly.Phase));
            dc.DrawEllipse(Glow(c, fade), null, new Point(fly.X, y), r * 4, r * 4);
            dc.DrawEllipse(Solid(ColorUtil.Lerp(c, White, 0.65), 0.9 * fade), null, new Point(fly.X, y), r * 0.75, r * 0.75);
        }
    }

    /// <summary>A soft round glow in a color, at one of a few strengths (kept, not made anew every frame).</summary>
    Brush Glow(Color c, double strength)
    {
        int step = (int)Math.Round(Math.Clamp(strength, 0, 1) * 8);
        if (_glows.TryGetValue((c, step), out var brush)) return brush;
        if (_glows.Count > 96) _glows.Clear();
        var made = new RadialGradientBrush(ColorUtil.WithAlpha(c, 0.4 * step / 8), ColorUtil.WithAlpha(c, 0));
        made.Freeze();
        _glows[(c, step)] = made;
        return made;
    }

    // 20. LED: a dot-matrix spectrum, cool by the screen edge and hot at the top, with held peaks.
    void DrawLed(Frame f)
    {
        var dc = f.Dc;
        const double cell = 3.4, gap = 1.6;
        int cols = (int)((f.W + gap) / (cell + gap));
        int rows = Math.Max(3, (int)((f.H * 0.92 + gap) / (cell + gap)));
        if (cols < 2) return;
        double fallen = FallPeaks(cols, 0.45);
        double offset = (f.W - (cols * (cell + gap) - gap)) / 2;
        var dark = new StreamGeometry();
        var low = new StreamGeometry();
        var middle = new StreamGeometry();
        var high = new StreamGeometry();
        var peak = new StreamGeometry();
        using (var gd = dark.Open())
        using (var gl = low.Open())
        using (var gm = middle.Open())
        using (var gh = high.Open())
        using (var gp = peak.Open())
        {
            for (int col = 0; col < cols; col++)
            {
                double v = Math.Clamp(SampleBand(FromMiddle(col, cols)) * (1 + _beat * 0.1), 0, 1);
                _vizPeaks[col] = (float)Math.Max(v, _vizPeaks[col] - fallen);
                int lit = (int)Math.Round(v * rows);
                int held = Math.Max(lit, (int)Math.Round(_vizPeaks[col] * rows)) - 1;
                double x = offset + col * (cell + gap);
                for (int row = 0; row < rows; row++)
                {
                    double y = f.Top ? f.H - 1 - (row + 1) * (cell + gap) + gap : 1 + row * (cell + gap);
                    var target = row < lit ? (row < rows * 0.5 ? gl : row < rows * 0.8 ? gm : gh)
                        : row == held && held >= lit ? gp
                        : gd;
                    AddRect(target, x, y, cell, cell);
                }
            }
        }
        dc.DrawGeometry(Solid(White, 0.05), null, Frozen(dark));
        dc.DrawGeometry(Solid(f.B, 0.95), null, Frozen(low));
        dc.DrawGeometry(Solid(f.A, 1), null, Frozen(middle));
        dc.DrawGeometry(Solid(ColorUtil.Lerp(f.C, White, 0.3), 1), null, Frozen(high));
        dc.DrawGeometry(Solid(White, 0.8), null, Frozen(peak));
    }

    static Color Vivid(Color c)
    {
        ColorUtil.ToHsl(c, out var h, out var s, out var l);
        return ColorUtil.FromHsl(h, Math.Max(s, 0.55), Math.Clamp(l, 0.5, 0.7));
    }

    static BitmapSource BuildBlur(BitmapSource art, int width, int height)
    {
        var px = Pixels(art, width, height, out int w, out int h);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    static byte[] Pixels(BitmapSource art, int width, int height, out int w, out int h)
    {
        var scaled = new TransformedBitmap(art, new ScaleTransform(width / (double)art.PixelWidth, height / (double)art.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth;
        h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        return px;
    }

    // ------------------------------------------------------------------ drawing helpers

    static StreamGeometry Ribbon(double from, double to, double step, Func<double, double> center, Func<double, double> thickness)
    {
        var top = new List<Point>();
        var bottom = new List<Point>();
        for (double x = from; x <= to + step; x += step)
        {
            double c = center(x), half = thickness(x) / 2;
            top.Add(new Point(x, c - half));
            bottom.Add(new Point(x, c + half));
        }
        bottom.Reverse();
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(top[0], true, true);
            g.PolyLineTo(top, false, true);
            g.PolyLineTo(bottom, false, true);
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>The area left of a ragged vertical edge (closed), or just the edge as a line.</summary>
    static StreamGeometry EdgeShape(double h, Func<double, double> edge, bool closed)
    {
        int steps = Math.Max(2, (int)Math.Ceiling(h / 1.5));
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(closed ? new Point(0, 0) : new Point(edge(0), 0), closed, closed);
            if (closed) g.LineTo(new Point(edge(0), 0), false, true);
            for (int i = 1; i <= steps; i++)
            {
                double y = h * i / steps;
                g.LineTo(new Point(edge(y), y), !closed, true);
            }
            if (closed) g.LineTo(new Point(0, h), false, true);
        }
        geo.Freeze();
        return geo;
    }

    static SolidColorBrush Solid(Color c, double alpha)
    {
        var brush = new SolidColorBrush(ColorUtil.WithAlpha(c, alpha * c.A / 255.0));
        brush.Freeze();
        return brush;
    }

    static Pen MakePen(Color c, double alpha, double thickness)
    {
        var pen = new Pen(Solid(c, alpha), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    static LinearGradientBrush Horizontal(double x0, double x1, params (Color C, double A, double O)[] stops)
        => Gradient(new Point(x0, 0), new Point(x1 <= x0 ? x0 + 1 : x1, 0), stops);

    static LinearGradientBrush Vertical(double y0, double y1, params (Color C, double A, double O)[] stops)
        => Gradient(new Point(0, y0), new Point(0, Math.Abs(y1 - y0) < 0.5 ? y0 + 1 : y1), stops);

    static LinearGradientBrush Gradient(Point start, Point end, (Color C, double A, double O)[] stops,
        GradientSpreadMethod spread = GradientSpreadMethod.Pad)
    {
        var collection = new GradientStopCollection(stops.Length);
        foreach (var (c, a, o) in stops) collection.Add(new GradientStop(ColorUtil.WithAlpha(c, a), o));
        var brush = new LinearGradientBrush(collection, start, end) { MappingMode = BrushMappingMode.Absolute, SpreadMethod = spread };
        brush.Freeze();
        return brush;
    }

    static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, params (Color C, double A, double O)[] stops)
    {
        var collection = new GradientStopCollection(stops.Length);
        foreach (var (c, a, o) in stops) collection.Add(new GradientStop(ColorUtil.WithAlpha(c, a), o));
        var brush = new RadialGradientBrush(collection)
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(cx, cy),
            GradientOrigin = new Point(cx, cy),
            RadiusX = Math.Max(0.5, rx),
            RadiusY = Math.Max(0.5, ry),
        };
        brush.Freeze();
        return brush;
    }

    static double Hash(double n)
    {
        double s = Math.Sin(n * 127.1 + 311.7) * 43758.5453;
        return s - Math.Floor(s);
    }

    static double Noise(double x)
    {
        double i = Math.Floor(x), k = x - i;
        return Hash(i) + (Hash(i + 1) - Hash(i)) * Smooth(k);
    }

    static double Smooth(double k) => k * k * (3 - 2 * k);

    static double Bump(double u, double center, double width)
    {
        double d = (u - center) / width;
        return Math.Exp(-d * d);
    }
}
