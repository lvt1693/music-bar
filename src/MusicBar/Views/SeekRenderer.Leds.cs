using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>
/// The RGB LED strip: LEDs along the screen edge, lit by one of ten effects the way a gaming strip's
/// controller lights them, only driven by the music. It reads as one diffused strip, not a row of
/// dots: a continuous line of light along the edge, and a glow blurred sideways so neighbouring LEDs
/// melt together, washing up over the taskbar, the brighter parts reaching further.
/// </summary>
public sealed partial class SeekRenderer
{
    const double LedPitch = 8.5;
    const int GlowRows = 12;

    public static bool IsLedStrip(SeekStyle s) => s >= SeekStyle.LedRainbow;

    /// <summary>Effects with colors of their own (a rainbow, fire, a VU meter's green to red): a palette doesn't change them.</summary>
    public static bool HasOwnColors(SeekStyle s) => s is SeekStyle.LedRainbow or SeekStyle.LedSpectrum or SeekStyle.LedFire or SeekStyle.LedVu;

    static readonly float[] Grey = [0.22f, 0.22f, 0.25f];

    // This frame's light, LED by LED: color (0-1) and brightness.
    float[] _ledR = [], _ledG = [], _ledB = [], _ledV = [];
    // What lives from frame to frame: fire's heat, the twinkles, the scanner's and meteors' trails, and their colors (0-2: A, B, C).
    float[] _ledHeat = [];
    byte[] _ledHue = [];
    readonly List<(double At, double Born, int Hue)> _ripples = [];
    readonly List<(double At, double Speed, int Hue)> _meteors = [];
    double _ledPhase, _scanAt = 0.15, _scanWay = 1, _ledCalm, _ledSurge;
    float _ledLastBeat, _vuPeak;
    int _ledHueNext;

    WriteableBitmap? _glowBitmap, _lineBitmap;
    byte[] _glowPixels = [], _linePixels = [];
    ImageBrush? _lineBrush;
    // The light blurred sideways (premultiplied color and brightness), for the glow and, a little, for the line.
    float[] _blurR = [], _blurG = [], _blurB = [], _blurV = [], _blurTemp = [];

    static int LedCount(double length) => Math.Max(8, (int)Math.Round(length / LedPitch));

    void EnsureLeds(int n)
    {
        if (_ledV.Length == n) return;
        _ledR = new float[n];
        _ledG = new float[n];
        _ledB = new float[n];
        _ledV = new float[n];
        _ledHeat = new float[n];
        _ledHue = new byte[n];
        _ripples.Clear();
        _meteors.Clear();
    }

    /// <summary>Advances the effects that remember: ripples, trails, twinkles, the fire, the phases.</summary>
    void StepLeds(double t, double dt)
    {
        int n = LedCount(Full);
        EnsureLeds(n);
        // Paused, the strip slows down with the rest (or stops, without idle motion).
        double k = dt * _pace;
        bool hit = _beat > 0.8f && _ledLastBeat <= 0.8f;
        _ledLastBeat = _beat;
        double level = _level;

        switch (_style)
        {
            case SeekStyle.LedRainbow:
                _ledPhase += k * (0.05 + 0.3 * level);
                break;
            case SeekStyle.LedSpectrum:
                _ledPhase += k * 0.025;
                break;
            case SeekStyle.LedBreathe:
                _ledCalm += (level - _ledCalm) * (1 - Math.Exp(-dt * 3));
                _ledPhase += k * 0.045;
                break;
            case SeekStyle.LedRipple:
                // Every beat drops a ripple near the middle; quiet music still gets one now and then.
                if (hit || (_ripples.Count == 0 && _rng.NextDouble() < k * 0.9))
                    _ripples.Add((0.5 + (_rng.NextDouble() - 0.5) * 0.6, t, _ledHueNext++ % 3));
                _ripples.RemoveAll(r => t - r.Born > 1.9 || t < r.Born);
                break;
            case SeekStyle.LedScanner:
            {
                double from = _scanAt;
                _scanAt += _scanWay * k * (0.3 + 0.85 * level);
                if (_scanAt > 1) (_scanAt, _scanWay) = (2 - _scanAt, -1);
                if (_scanAt < 0) (_scanAt, _scanWay) = (-_scanAt, 1);
                Fade(k * 4.5);
                // A wider eye on the beat; every LED it passed this step is stamped, so fast sweeps leave no gaps.
                Stamp(from, _scanAt, n, 1 + (int)Math.Round(2 * _beat), 0);
                break;
            }
            case SeekStyle.LedStarlight:
            {
                Fade(k * 1.8);
                double born = k * (4 + 70 * level) + (hit ? 18 : 0);
                for (; born > 0; born--)
                {
                    if (born < 1 && _rng.NextDouble() > born) break;
                    // A twinkle is a small soft spot, not a single LED: the strip is diffused.
                    int i = _rng.Next(n);
                    byte hue = (byte)_rng.Next(3);
                    for (int j = -3; j <= 3; j++)
                    {
                        int at = i + j;
                        if (at < 0 || at >= n) continue;
                        float spot = 1 - Math.Abs(j) * 0.24f;
                        if (spot < _ledHeat[at]) continue;
                        _ledHeat[at] = spot;
                        _ledHue[at] = hue;
                    }
                }
                break;
            }
            case SeekStyle.LedFire:
            {
                // Fire along a strip: it cools, the heat spreads to the neighbours, and the bass strikes sparks.
                for (int i = 0; i < n; i++) _ledHeat[i] = (float)Math.Max(0, _ledHeat[i] - _rng.NextDouble() * k * 1.2);
                float previous = _ledHeat[0];
                double mix = 1 - Math.Exp(-k * 9);
                for (int i = 1; i < n - 1; i++)
                {
                    float here = _ledHeat[i];
                    _ledHeat[i] = (float)(here + ((previous + 2 * here + _ledHeat[i + 1]) / 4 - here) * mix);
                    previous = here;
                }
                double sparks = k * (8 + 55 * SampleBand(0.04)) + (hit ? 10 : 0);
                for (; sparks > 0; sparks--)
                {
                    if (sparks < 1 && _rng.NextDouble() > sparks) break;
                    int i = _rng.Next(n);
                    _ledHeat[i] = (float)Math.Min(1, _ledHeat[i] + 0.45 + 0.55 * _rng.NextDouble());
                }
                break;
            }
            case SeekStyle.LedMeteor:
            {
                if (hit || _rng.NextDouble() < k * 0.5 * level)
                {
                    bool fromLeft = _rng.Next(2) == 0;
                    _meteors.Add((fromLeft ? -0.02 : 1.02, (0.45 + 0.6 * _rng.NextDouble()) * (fromLeft ? 1 : -1), _ledHueNext++ % 3));
                }
                // Trails die away unevenly, so they sparkle.
                for (int i = 0; i < n; i++) _ledHeat[i] *= (float)Math.Exp(-k * (1.8 + 4.5 * _rng.NextDouble()));
                for (int m = _meteors.Count - 1; m >= 0; m--)
                {
                    var (at, speed, hue) = _meteors[m];
                    double next = at + speed * k;
                    Stamp(at, next, n, 1, hue);
                    if (next < -0.05 || next > 1.05) _meteors.RemoveAt(m);
                    else _meteors[m] = (next, speed, hue);
                }
                break;
            }
            case SeekStyle.LedChase:
                // The bands glide along with the music, surging forward on each beat and easing off.
                if (hit) _ledSurge = 40;
                _ledSurge *= Math.Exp(-dt * 5);
                _ledPhase += k * (6 + 20 * level + _ledSurge);
                break;
            case SeekStyle.LedVu:
                _vuPeak = (float)Math.Max(VuLevel(), _vuPeak - k * 0.45);
                break;
        }
    }

    float VuLevel() => (float)Math.Clamp(_level * 1.25 + _beat * 0.08, 0, 1);

    void Fade(double amount)
    {
        float keep = (float)Math.Exp(-amount);
        for (int i = 0; i < _ledHeat.Length; i++) _ledHeat[i] *= keep;
    }

    /// <summary>Lights every LED between two points along the strip (0-1), and <paramref name="spread"/> either side.</summary>
    void Stamp(double from, double to, int n, int spread, int hue)
    {
        int a = (int)Math.Floor(Math.Min(from, to) * n), b = (int)Math.Ceiling(Math.Max(from, to) * n);
        for (int i = Math.Max(0, a - spread); i <= Math.Min(n - 1, b + spread); i++)
        {
            _ledHeat[i] = 1;
            _ledHue[i] = (byte)hue;
        }
    }

    // 21-30. The RGB LED strip.
    void DrawLedStrip(Frame f)
    {
        int n = LedCount(f.W);
        EnsureLeds(n);
        float[] a = Rgb(f.A), b = Rgb(f.B), c = Rgb(f.C);
        float[] Pick(int hue) => hue == 0 ? a : hue == 1 ? b : c;
        double level = _level, beat = _beat;

        for (int i = 0; i < n; i++)
        {
            double u = (i + 0.5) / n, mid = FromMiddle(i, n);
            float r, g, bl;
            double v;
            switch (_style)
            {
                case SeekStyle.LedRainbow:
                    Hue(u + _ledPhase, out r, out g, out bl);
                    v = 0.35 + 0.4 * level + 0.25 * beat + 0.35 * SampleBand(mid);
                    break;
                case SeekStyle.LedSpectrum:
                {
                    // The bass red in the middle, through the rainbow to violet treble at the ends.
                    Hue(mid * 0.78 + _ledPhase, out r, out g, out bl);
                    v = Math.Max(0.05, Math.Pow(SampleBand(mid), 1.1) * 1.5);
                    break;
                }
                case SeekStyle.LedBreathe:
                {
                    var color = Cycle(a, b, c, _ledPhase);
                    (r, g, bl) = (color[0], color[1], color[2]);
                    v = (0.2 + 0.85 * _ledCalm + 0.35 * beat) * (0.85 + 0.15 * (1 - mid));
                    break;
                }
                case SeekStyle.LedRipple:
                {
                    double sr = a[0] * (0.05 + 0.1 * level), sg = a[1] * (0.05 + 0.1 * level), sb = a[2] * (0.05 + 0.1 * level);
                    foreach (var (at, born, hue) in _ripples)
                    {
                        double age = f.T - born, radius = age * 0.55, d = Math.Abs(u - at) - radius;
                        double ring = Math.Exp(-d * d / 0.0018) * Math.Pow(Math.Max(0, 1 - age / 1.9), 1.3);
                        var col = Pick(hue);
                        sr += col[0] * ring;
                        sg += col[1] * ring;
                        sb += col[2] * ring;
                    }
                    v = Math.Max(sr, Math.Max(sg, sb));
                    double scale = v > 0.001 ? 1 / v : 0;
                    (r, g, bl) = ((float)(sr * scale), (float)(sg * scale), (float)(sb * scale));
                    break;
                }
                case SeekStyle.LedScanner:
                {
                    double heat = _ledHeat[i];
                    var col = Mix(b, a, heat);
                    if (heat > 0.9) col = Mix(col, [1, 1, 1], (heat - 0.9) * 4);
                    (r, g, bl) = (col[0], col[1], col[2]);
                    v = Math.Pow(heat, 1.6);
                    break;
                }
                case SeekStyle.LedStarlight:
                case SeekStyle.LedMeteor:
                {
                    double heat = _ledHeat[i];
                    // Fresh sparks run white-hot, then settle into their color.
                    var col = Mix(Pick(_ledHue[i]), [1, 1, 1], Math.Pow(heat, 4) * 0.7);
                    (r, g, bl) = (col[0], col[1], col[2]);
                    v = _style == SeekStyle.LedStarlight ? 0.03 + Math.Pow(heat, 1.3) : heat;
                    break;
                }
                case SeekStyle.LedFire:
                {
                    // A bed of flickering flame, louder music burning higher, the bass's sparks on top;
                    // black to red, red to yellow, yellow to white.
                    double flame = Math.Pow(0.55 * Noise(f.T * 2.3 + i * 0.23) + 0.45 * Noise(f.T * 6.1 + i * 0.71 + 40), 1.6) * (0.5 + 0.75 * level);
                    double heat = Math.Clamp(Math.Max(flame, _ledHeat[i]), 0, 1);
                    r = (float)Math.Min(1, heat * 3);
                    g = (float)Math.Clamp(heat * 3 - 1, 0, 1);
                    bl = (float)Math.Clamp(heat * 3 - 2, 0, 1);
                    v = Math.Min(1, heat * 1.6);
                    double top = Math.Max(r, Math.Max(g, bl));
                    if (top > 0.001) (r, g, bl) = ((float)(r / top), (float)(g / top), (float)(bl / top));
                    break;
                }
                case SeekStyle.LedChase:
                {
                    // Soft bands of light chasing along, each band in the next of A, B and C.
                    const double band = 16;
                    double p = (i - _ledPhase) / band;
                    double lit = Math.Pow(0.5 + 0.5 * Math.Cos(2 * Math.PI * p), 2.5);
                    var col = Pick((int)(((Math.Floor(p + 0.5) % 3) + 3) % 3));
                    (r, g, bl) = (col[0], col[1], col[2]);
                    v = 0.04 + lit * (0.45 + 0.55 * level + 0.3 * beat);
                    break;
                }
                default:
                {
                    // VU: out from the middle, green, then yellow, then red, with the peak held.
                    float[] col = mid < 0.6 ? [0.2f, 1f, 0.35f] : mid < 0.82 ? [1f, 0.82f, 0.1f] : [1f, 0.16f, 0.1f];
                    bool lit = mid <= VuLevel();
                    bool peak = Math.Abs(mid - _vuPeak) < 1.0 / n && _vuPeak > 0.02;
                    if (peak) col = [1, 1, 1];
                    (r, g, bl) = (col[0], col[1], col[2]);
                    v = lit || peak ? 1 : 0.07;
                    break;
                }
            }
            _ledR[i] = r;
            _ledG[i] = g;
            _ledB[i] = bl;
            _ledV[i] = (float)Math.Clamp(v, 0, 1);
        }
        PaintStrip(f, n);
    }

    /// <summary>The strip's light washing over the taskbar, then the strip itself: one continuous line along the screen edge.</summary>
    void PaintStrip(Frame f, int n)
    {
        if (_glowBitmap == null || _glowBitmap.PixelWidth != n)
        {
            _glowBitmap = new WriteableBitmap(n, GlowRows, 96, 96, PixelFormats.Pbgra32, null);
            _lineBitmap = new WriteableBitmap(n, 1, 96, 96, PixelFormats.Pbgra32, null);
            _glowPixels = new byte[n * GlowRows * 4];
            _linePixels = new byte[n * 4];
            _lineBrush = new ImageBrush(_lineBitmap) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute };
            RenderOptions.SetBitmapScalingMode(_lineBrush, BitmapScalingMode.Linear);
            _blurR = new float[n];
            _blurG = new float[n];
            _blurB = new float[n];
            _blurV = new float[n];
            _blurTemp = new float[n];
        }
        double gain = 0.55 + 0.45 * f.F;

        // The line: each LED softened into its neighbours, a dim grey strip where it's off, its color
        // running toward white where it's bright.
        Diffuse(n, 1);
        for (int i = 0; i < n; i++)
        {
            double v = _blurV[i];
            Diffused(i, out float r, out float g, out float b);
            double hot = 0.45 * v * v;
            double cr = Grey[0] + ((r + (1 - r) * hot) - Grey[0]) * v;
            double cg = Grey[1] + ((g + (1 - g) * hot) - Grey[1]) * v;
            double cb = Grey[2] + ((b + (1 - b) * hot) - Grey[2]) * v;
            Put(_linePixels, i * 4, (float)cr, (float)cg, (float)cb, 0.12 + 0.88 * v);
        }

        // The glow: spread much wider, as a diffuser would, brighter parts reaching further up.
        Diffuse(n, 3);
        for (int i = 0; i < n; i++)
        {
            double v = Math.Min(1, _blurV[i] * 1.25);
            Diffused(i, out float r, out float g, out float b);
            double reach = 0.12 + 0.5 * Math.Sqrt(v);
            for (int row = 0; row < GlowRows; row++)
            {
                double d = (row + 0.5) / GlowRows;
                double alpha = v * gain * 0.85 * Math.Exp(-d / reach);
                int y = f.Top ? GlowRows - 1 - row : row;
                Put(_glowPixels, (y * n + i) * 4, r, g, b, alpha);
            }
        }
        _glowBitmap.WritePixels(new Int32Rect(0, 0, n, GlowRows), _glowPixels, n * 4, 0);
        _lineBitmap!.WritePixels(new Int32Rect(0, 0, n, 1), _linePixels, n * 4, 0);

        var dc = f.Dc;
        dc.DrawImage(_glowBitmap, new Rect(0, 0, f.W, f.H));
        double lineH = Math.Clamp(f.H * 0.08, 2, 3.5);
        var line = new Rect(0, f.Top ? f.H - lineH - 1 : 1, f.W, lineH);
        _lineBrush!.Viewport = line;
        dc.DrawRectangle(_lineBrush, null, line);
    }

    /// <summary>
    /// The LEDs' light spread sideways, twice over a box <paramref name="radius"/> LEDs either side
    /// (close to a Gaussian): brightness, and color weighted by it, into the _blur arrays.
    /// </summary>
    void Diffuse(int n, int radius)
    {
        for (int i = 0; i < n; i++)
        {
            float v = _ledV[i];
            (_blurR[i], _blurG[i], _blurB[i], _blurV[i]) = (_ledR[i] * v, _ledG[i] * v, _ledB[i] * v, v);
        }
        foreach (var channel in new[] { _blurR, _blurG, _blurB, _blurV })
        {
            BoxBlur(channel, n, radius);
            BoxBlur(channel, n, radius);
        }
    }

    void BoxBlur(float[] values, int n, int radius)
    {
        if (radius <= 0) return;
        float sum = 0;
        int count = 0;
        // A running sum over the window; at the strip's ends the window is cut short, not padded dark.
        for (int i = 0; i < Math.Min(radius, n); i++)
        {
            sum += values[i];
            count++;
        }
        for (int i = 0; i < n; i++)
        {
            int add = i + radius, drop = i - radius - 1;
            if (add < n) { sum += values[add]; count++; }
            if (drop >= 0) { sum -= values[drop]; count--; }
            _blurTemp[i] = sum / count;
        }
        Array.Copy(_blurTemp, values, n);
    }

    /// <summary>The diffused color at an LED: the blurred color divided back by the blurred brightness.</summary>
    void Diffused(int i, out float r, out float g, out float b)
    {
        float v = _blurV[i];
        if (v < 1e-4f)
        {
            (r, g, b) = (0, 0, 0);
            return;
        }
        r = Math.Min(1, _blurR[i] / v);
        g = Math.Min(1, _blurG[i] / v);
        b = Math.Min(1, _blurB[i] / v);
    }

    static void Put(byte[] pixels, int o, float r, float g, float b, double alpha)
    {
        double a = Math.Clamp(alpha, 0, 1);
        pixels[o] = (byte)(b * a * 255);
        pixels[o + 1] = (byte)(g * a * 255);
        pixels[o + 2] = (byte)(r * a * 255);
        pixels[o + 3] = (byte)(a * 255);
    }

    static float[] Rgb(Color c) => [c.R / 255f, c.G / 255f, c.B / 255f];

    static float[] Mix(float[] x, float[] y, double k)
    {
        float t = (float)Math.Clamp(k, 0, 1);
        return [x[0] + (y[0] - x[0]) * t, x[1] + (y[1] - x[1]) * t, x[2] + (y[2] - x[2]) * t];
    }

    /// <summary>Round the three palette colors, one turn per unit of <paramref name="phase"/>.</summary>
    static float[] Cycle(float[] a, float[] b, float[] c, double phase)
    {
        double p = (phase % 1 + 1) % 1 * 3;
        return p < 1 ? Mix(a, b, Smooth(p)) : p < 2 ? Mix(b, c, Smooth(p - 1)) : Mix(c, a, Smooth(p - 2));
    }

    /// <summary>A fully saturated hue, 0-1 round the color wheel.</summary>
    static void Hue(double h, out float r, out float g, out float b)
    {
        double x = (h % 1 + 1) % 1 * 6;
        r = (float)Math.Clamp(Math.Abs(x - 3) - 1, 0, 1);
        g = (float)Math.Clamp(2 - Math.Abs(x - 2), 0, 1);
        b = (float)Math.Clamp(2 - Math.Abs(x - 4), 0, 1);
    }
}
