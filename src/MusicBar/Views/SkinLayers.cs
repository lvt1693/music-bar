using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MusicBar.Infrastructure;
using MusicBar.Services;
using SkiaSharp;
using SkiaSharp.Skottie;
using Svg.Skia;

namespace MusicBar.Views;

/// <summary>
/// A skin's layers ("//! layer:", <see cref="SkinLayerSpec"/>): the pictures it lays in front of and behind what its
/// shader paints. Their files are read once per skin (again when one changes); each frame they are drawn with Skia on
/// the CPU into one or two canvases (back, front) that span the whole bar and its headroom, which Direct3D 11 then
/// takes as textures: the 2.0 engine's last pass lays them round the picture, and the skin may read them itself
/// (layerFront(uv), layerBack(uv)). A Lottie animation of a few hundred shapes draws in well under a millisecond.
/// UI thread only.
/// </summary>
public sealed class SkinLayerSet
{
    static readonly ConditionalWeakTable<SkinInfo, SkinLayerSet> Sets = new();

    readonly SkinInfo _skin;
    readonly List<(SkinLayerSpec Spec, ILayerAsset Asset)> _layers = [];
    readonly Dictionary<string, DateTime> _written = new(StringComparer.OrdinalIgnoreCase);
    readonly List<SkinLayerCanvas> _canvases = [];
    readonly Stopwatch _sinceCheck = Stopwatch.StartNew();

    SkinLayerSet(SkinInfo skin)
    {
        _skin = skin;
        Load();
    }

    /// <summary>The layers of a skin that has any, read on first use.</summary>
    public static SkinLayerSet? For(SkinInfo skin)
    {
        if (skin.Features?.HasLayers != true) return null;
        var set = Sets.GetValue(skin, s => new SkinLayerSet(s));
        // A picture edited while the skin is on comes back in a moment or two (the skin's own file is watched elsewhere).
        if (set._sinceCheck.Elapsed > TimeSpan.FromSeconds(2))
        {
            set._sinceCheck.Restart();
            if (set.Changed()) set.Reload();
        }
        return set;
    }

    public IReadOnlyList<(SkinLayerSpec Spec, ILayerAsset Asset)> Layers => _layers;

    bool Changed()
    {
        foreach (var (path, written) in _written)
        {
            try
            {
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) != written) return true;
            }
            catch (IOException)
            {
                return true;
            }
        }
        return false;
    }

    void Reload()
    {
        foreach (var (_, asset) in _layers) asset.Dispose();
        _layers.Clear();
        _written.Clear();
        Load();
        foreach (var c in _canvases) c.Invalidate();
    }

    void Load()
    {
        var folder = SkinLibrary.AssetFolder(_skin.File);
        foreach (var spec in _skin.Features!.Layers!)
        {
            var path = Path.Combine(folder, spec.File);
            _written[path] = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            try
            {
                var asset = LayerAssets.Open(path);
                if (asset != null) _layers.Add((spec, asset));
                else Log.Error($"Skin '{_skin.File}': layer '{spec.File}' isn't a picture MusicBar can draw (Lottie, SVG, PNG, JPG, WebP, GIF)");
            }
            catch (Exception ex)
            {
                Log.Error($"Skin '{_skin.File}': layer '{spec.File}' could not be read", ex);
            }
        }
    }

    /// <summary>
    /// The canvases for a bar of this size: <paramref name="width"/> and <paramref name="bar"/> its width and height in
    /// DIPs, <paramref name="headroom"/> the most the skin draws past the taskbar's edge, <paramref name="above"/> true
    /// when that is above it (a taskbar at the bottom). The bar and its headroom ask with the same numbers and share one.
    /// </summary>
    public SkinLayerCanvas Canvas(double width, double bar, double headroom, bool above, double dpiX, double dpiY)
    {
        foreach (var c in _canvases)
            if (c.Fits(width, bar, headroom, above, dpiX, dpiY))
            {
                c.LastUsed = Environment.TickCount64;
                return c;
            }
        // A few sizes at once at most (the taskbar, the settings' preview); the one longest unused goes.
        if (_canvases.Count >= 4) _canvases.Remove(_canvases.MinBy(c => c.LastUsed)!);
        var made = new SkinLayerCanvas(this, width, bar, headroom, above, dpiX, dpiY) { LastUsed = Environment.TickCount64 };
        _canvases.Add(made);
        return made;
    }
}

/// <summary>
/// Where one skin's layers are drawn for one size of bar: a back and a front picture (only those its layers use), in
/// the screen's pixels, the bar and its headroom together, the headroom side up as the screen has it.
/// </summary>
public sealed class SkinLayerCanvas
{
    readonly SkinLayerSet _set;
    readonly double _width, _bar, _headroom, _dpiX, _dpiY;
    readonly bool _above;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastWall = double.NaN;
    double _shown = 1;
    // Where layers that follow the song's progress are: glides after a seek, starts over at once for a new song.
    double _progress = double.NaN;

    internal SkinLayerCanvas(SkinLayerSet set, double width, double bar, double headroom, bool above, double dpiX, double dpiY)
    {
        (_set, _width, _bar, _headroom, _above, _dpiX, _dpiY) = (set, width, bar, headroom, above, dpiX, dpiY);
        PixelWidth = Math.Max(1, (int)Math.Round(width * dpiX));
        PixelHeight = Math.Max(1, (int)Math.Round((bar + headroom) * dpiY));
        // Where a skin's own y (measured on the bar: 0 its top, 1 its bottom, past them in the headroom) falls in this
        // picture, top to bottom: v = MapA + y * MapK. The headroom is above the bar for a taskbar at the bottom.
        double k = bar / (bar + headroom);
        MapK = (float)k;
        MapA = (float)(above ? 1 - k : 0);
    }

    internal long LastUsed { get; set; }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public float MapA { get; }
    public float MapK { get; }

    /// <summary>The pictures, premultiplied BGRA (null when no layer goes there).</summary>
    public SKBitmap? Back { get; private set; }
    public SKBitmap? Front { get; private set; }

    /// <summary>Bumped each time the pictures are drawn again: the card takes them anew when it changed.</summary>
    public int Version { get; private set; }

    internal bool Fits(double width, double bar, double headroom, bool above, double dpiX, double dpiY) =>
        Math.Abs(width - _width) < 0.5 && Math.Abs(bar - _bar) < 0.5 && Math.Abs(headroom - _headroom) < 0.5 && above == _above
        && Math.Abs(dpiX - _dpiX) < 0.01 && Math.Abs(dpiY - _dpiY) < 0.01;

    internal void Invalidate() => _lastWall = double.NaN;

    /// <summary>
    /// Draws the layers for now, once a frame: the bar and its headroom both ask, a moment apart, and the second finds
    /// it done. <paramref name="music"/> is the skin's clock (it slows when the music is paused); <paramref name="progress"/>
    /// how far the song has played (0 to 1).
    /// </summary>
    public void Render(double music, double beat, bool playing, double progress)
    {
        double wall = _clock.Elapsed.TotalSeconds;
        if (!double.IsNaN(_lastWall) && wall - _lastWall < 0.004) return;
        double dt = double.IsNaN(_lastWall) ? 0 : Math.Clamp(wall - _lastWall, 0, 0.25);
        _lastWall = wall;
        // Layers that hide while the music is paused fade out over half a second, and back in.
        _shown = Math.Clamp(_shown + (playing ? dt : -dt) / 0.5, 0, 1);
        progress = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        // A seek glides over a few tenths of a second; a new song (far back) starts at the left end straight away.
        if (double.IsNaN(_progress) || progress < _progress - 0.25) _progress = progress;
        else _progress += (progress - _progress) * (1 - Math.Exp(-dt * 7));

        bool wantBack = false, wantFront = false;
        foreach (var (spec, _) in _set.Layers)
        {
            if (spec.Front) wantFront = true;
            else wantBack = true;
        }
        Back = Fit(Back, wantBack);
        Front = Fit(Front, wantFront);
        if (Back != null) Draw(Back, front: false, music, wall, beat);
        if (Front != null) Draw(Front, front: true, music, wall, beat);
        Version++;
    }

    SKBitmap? Fit(SKBitmap? bitmap, bool wanted)
    {
        if (!wanted)
        {
            bitmap?.Dispose();
            return null;
        }
        return bitmap ?? new SKBitmap(new SKImageInfo(PixelWidth, PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
    }

    void Draw(SKBitmap target, bool front, double music, double wall, double beat)
    {
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)_dpiX, (float)_dpiY);
        foreach (var (spec, asset) in _set.Layers)
        {
            if (spec.Front != front) continue;
            double opacity = spec.Opacity * (spec.HideWhenPaused ? _shown : 1);
            if (opacity <= 0.002) continue;
            double t = spec.Always ? wall : music;

            double h = spec.Size > 0 ? spec.Size : _bar;
            double w = asset.Height > 0 ? h * asset.Width / asset.Height : h;
            double x;
            if (spec.FollowProgress)
            {
                // From whole at the left end to whole at the right end.
                x = w / 2 + _progress * Math.Max(0, _width - w);
            }
            else if (spec.Across != 0)
            {
                // From just off one end to just off the other, again and again; x is where on the way it starts.
                double run = t / Math.Abs(spec.Across) + spec.X;
                double along = run - Math.Floor(run);
                x = -w / 2 + along * (_width + w);
                if (spec.Across < 0) x = _width - x;
            }
            else
            {
                x = spec.X * _width;
            }
            double out_ = double.IsNaN(spec.Y) ? -_bar / 2 : spec.Y;
            if (spec.BobHeight != 0) out_ += spec.BobHeight * Math.Sin(2 * Math.PI * t / spec.BobPeriod);
            // Out from the taskbar's edge towards the desktop: up for a taskbar at the bottom, down for one at the top.
            double y = _above ? _headroom + _bar - (_bar + out_) : _bar + out_;
            double angle = spec.SwayDegrees != 0 ? spec.SwayDegrees * Math.Sin(2 * Math.PI * t / spec.SwayPeriod) : 0;
            double grow = 1 + spec.Beat * Math.Clamp(beat, 0, 1);

            canvas.Save();
            canvas.Translate((float)x, (float)y);
            if (angle != 0) canvas.RotateDegrees((float)angle);
            canvas.Scale((float)(spec.FlipX ? -grow : grow), (float)(spec.FlipY ? -grow : grow));
            var dest = new SKRect((float)(-w / 2), (float)(-h / 2), (float)(w / 2), (float)(h / 2));
            var mode = BlendOf(spec.Blend);
            bool layered = opacity < 0.999 || mode != SKBlendMode.SrcOver;
            if (layered)
            {
                using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)), BlendMode = mode };
                canvas.SaveLayer(dest, paint);
            }
            try
            {
                asset.Draw(canvas, dest, t * spec.Speed);
            }
            catch (Exception ex)
            {
                Log.Error($"Drawing layer '{spec.File}' failed", ex);
            }
            if (layered) canvas.Restore();
            canvas.Restore();
        }
        canvas.Flush();
    }

    static SKBlendMode BlendOf(string blend) => blend switch
    {
        "add" or "plus" => SKBlendMode.Plus,
        "screen" => SKBlendMode.Screen,
        "multiply" => SKBlendMode.Multiply,
        "overlay" => SKBlendMode.Overlay,
        "lighten" => SKBlendMode.Lighten,
        "darken" => SKBlendMode.Darken,
        _ => SKBlendMode.SrcOver,
    };
}

/// <summary>A layer's picture: its own size (any units, for the aspect), drawn into a rectangle at a time in seconds.</summary>
public interface ILayerAsset : IDisposable
{
    float Width { get; }
    float Height { get; }
    void Draw(SKCanvas canvas, SKRect dest, double seconds);
}

/// <summary>Opens a layer's file by what it is.</summary>
public static class LayerAssets
{
    /// <summary>Files past this are refused (an animation of this size would be far too heavy for the taskbar anyway).</summary>
    const long MaxBytes = 32 * 1024 * 1024;

    public static ILayerAsset? Open(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No such file", path);
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException($"Bigger than {MaxBytes / (1024 * 1024)} MB");
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => LottieAsset.FromJson(File.ReadAllText(path), Path.GetDirectoryName(path)),
            ".lottie" => LottieAsset.FromDotLottie(path),
            ".svg" => SvgAsset.Open(path),
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => ImageAsset.Open(path),
            _ => null,
        };
    }
}

/// <summary>A Lottie animation (Skottie): it loops over its length. Expressions in it are not run.</summary>
public sealed class LottieAsset : ILayerAsset
{
    readonly Animation _animation;
    readonly double _length;

    LottieAsset(Animation animation)
    {
        _animation = animation;
        _length = Math.Max(1.0 / 60, animation.Duration.TotalSeconds);
    }

    public float Width => _animation.Size.Width;
    public float Height => _animation.Size.Height;

    /// <summary>From its JSON; pictures it names are looked for in <paramref name="folder"/>.</summary>
    public static LottieAsset FromJson(string json, string? folder)
    {
        var builder = Animation.CreateBuilder(default);
        if (folder != null)
            builder = builder.SetResourceProvider(new SkiaSharp.Resources.CachingResourceProvider(
                new SkiaSharp.Resources.DataUriResourceProvider(new SkiaSharp.Resources.FileResourceProvider(folder, true), true)));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var animation = builder.Build(stream) ?? throw new InvalidDataException("Not a Lottie animation Skottie can read");
        return new LottieAsset(animation);
    }

    /// <summary>
    /// From a .lottie file (a zip: manifest.json, animations/*.json, images/): its first animation. Its pictures, if it
    /// has any, are unpacked into the cache once, so the animation can find them.
    /// </summary>
    public static LottieAsset FromDotLottie(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        string? id = null;
        if (zip.GetEntry("manifest.json") is { } manifest)
        {
            using var stream = manifest.Open();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("animations", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
                && list[0].TryGetProperty("id", out var first))
                id = first.GetString();
        }
        var entry = (id != null ? zip.GetEntry($"animations/{id}.json") : null)
                    ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("animations/", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("No animation in the .lottie file");
        string json;
        using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();

        string? folder = null;
        var pictures = zip.Entries.Where(e => e.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0).ToList();
        if (pictures.Count > 0)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))[..16];
            folder = Path.Combine(AppPaths.DataDir, "Cache", "Layers", hash);
            foreach (var picture in pictures)
            {
                // Only plain names inside images/: nothing in the zip can write outside the folder.
                var target = Path.Combine(folder, "images", Path.GetFileName(picture.FullName));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                picture.ExtractToFile(target, overwrite: true);
            }
        }
        return FromJson(json, folder);
    }

    public void Draw(SKCanvas canvas, SKRect dest, double seconds)
    {
        double at = seconds % _length;
        if (at < 0) at += _length;
        _animation.SeekFrameTime(at);
        _animation.Render(canvas, dest);
    }

    public void Dispose() => _animation.Dispose();
}

/// <summary>An SVG drawing (Svg.Skia): still, or animated with SMIL (animate, animateTransform, ...), set to the time asked.</summary>
public sealed class SvgAsset : ILayerAsset
{
    readonly SKSvg _svg;
    readonly SKRect _bounds;
    double _lastTime = double.NaN;

    SvgAsset(SKSvg svg, SKRect bounds)
    {
        _svg = svg;
        _bounds = bounds;
    }

    public float Width => _bounds.Width;
    public float Height => _bounds.Height;

    public static SvgAsset Open(string path)
    {
        var svg = new SKSvg();
        var picture = svg.Load(path) ?? throw new InvalidDataException("Not an SVG Svg.Skia can read");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidDataException("The SVG has no size");
        return new SvgAsset(svg, bounds);
    }

    public void Draw(SKCanvas canvas, SKRect dest, double seconds)
    {
        if (_svg.HasAnimations && seconds != _lastTime)
        {
            _svg.SetAnimationTime(TimeSpan.FromSeconds(Math.Max(0, seconds)));
            _lastTime = seconds;
        }
        if (_svg.Picture is not { } picture) return;
        canvas.Save();
        canvas.Translate(dest.Left, dest.Top);
        canvas.Scale(dest.Width / _bounds.Width, dest.Height / _bounds.Height);
        canvas.Translate(-_bounds.Left, -_bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    public void Dispose() => _svg.Dispose();
}

/// <summary>A picture; an animated GIF or WebP plays its frames, decoded once (up to a limit of memory).</summary>
public sealed class ImageAsset : ILayerAsset
{
    /// <summary>The most decoded pixels an animation keeps (about 96 MB): past it, the frames after are dropped.</summary>
    const long MaxPixels = 24L * 1024 * 1024;

    readonly List<(SKImage Image, double Until)> _frames;
    readonly double _length;

    ImageAsset(List<(SKImage, double)> frames, int width, int height)
    {
        _frames = frames;
        _length = frames[^1].Item2;
        Width = width;
        Height = height;
    }

    public float Width { get; }
    public float Height { get; }

    public static ImageAsset Open(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Not a picture Skia can read");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frames = new List<(SKImage, double)>();
        int count = Math.Max(1, codec.FrameCount);
        var timing = codec.FrameInfo;
        var decoded = new List<SKBitmap>();
        double at = 0;
        long pixels = 0;
        for (int i = 0; i < count && pixels + (long)info.Width * info.Height <= MaxPixels; i++)
        {
            var bitmap = new SKBitmap(info);
            int required = i < timing.Length ? timing[i].RequiredFrame : -1;
            if (required >= 0 && required < decoded.Count) decoded[required].CopyTo(bitmap);
            else bitmap.Erase(SKColors.Transparent);
            var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, required));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                bitmap.Dispose();
                if (i == 0) throw new InvalidDataException($"The picture didn't decode ({result})");
                break;
            }
            decoded.Add(bitmap);
            pixels += (long)info.Width * info.Height;
            // A frame of 0 or 10 ms is shown for 100 ms, as browsers do.
            double duration = i < timing.Length && timing[i].Duration > 10 ? timing[i].Duration / 1000.0 : count > 1 ? 0.1 : 1;
            at += duration;
            // A copy: the bitmaps go once every frame is decoded (later frames are built on earlier ones).
            frames.Add((SKImage.FromPixelCopy(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes), at));
        }
        foreach (var b in decoded) b.Dispose();
        return new ImageAsset(frames, info.Width, info.Height);
    }

    public void Draw(SKCanvas canvas, SKRect dest, double seconds)
    {
        var image = _frames[0].Image;
        if (_frames.Count > 1)
        {
            double at = seconds % _length;
            if (at < 0) at += _length;
            foreach (var (frame, until) in _frames)
            {
                image = frame;
                if (at < until) break;
            }
        }
        canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    public void Dispose()
    {
        foreach (var (image, _) in _frames) image.Dispose();
    }
}
