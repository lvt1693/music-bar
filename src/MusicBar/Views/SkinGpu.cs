using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Services;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace MusicBar.Views;

/// <summary>What a skin is told for one frame, laid out register by register as WPF fills them (see SkinLibrary.Prelude11).</summary>
[StructLayout(LayoutKind.Explicit, Size = 224)]
public struct SkinInputs
{
    /// <summary>The 2.0 engine's inputs (c12): x the seconds since the last frame, y the pass (0 the state pass, 1 the
    /// picture), z the cover's width over its height, w 1 while there is a cover.</summary>
    [FieldOffset(192)] public Vector4 Engine;
    /// <summary>The layers' pictures (c13): where the skin's y falls in them (v = x + y * y), z 1 with a back one, w with a front one.</summary>
    [FieldOffset(208)] public Vector4 Layers;
    // c11 is read only by skins with headroom (SkinLibrary.HeadroomPrelude11); the others never declare it.
    [FieldOffset(0)] public float Time;
    [FieldOffset(16)] public float Progress;
    [FieldOffset(32)] public float Level;
    [FieldOffset(48)] public float Playing;
    [FieldOffset(64)] public Vector2 Size;
    [FieldOffset(80)] public Vector4 ColorA;
    [FieldOffset(96)] public Vector4 ColorB;
    [FieldOffset(112)] public Vector4 ColorC;
    [FieldOffset(128)] public float Beat;
    [FieldOffset(144)] public float BottomEdge;
    [FieldOffset(160)] public Vector2 Span;
    /// <summary>Where the bar is in the part drawn (x start, y height, as shares of it) and the headroom shown (z).</summary>
    [FieldOffset(176)] public Vector4 Bar;

    /// <summary>A color as WPF hands it to a shader: its sRGB channels from 0 to 1, as they are (measured).</summary>
    public static Vector4 From(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}

/// <summary>
/// Draws skins with Direct3D 11 instead of as WPF effects. WPF runs pixel shaders as ps_3_0, which caps a
/// skin at a couple of hundred constants, 32 registers and (on the PC this was measured on) about 3,250
/// instructions; shader model 5 has no such walls. Each stretch of the bar is drawn into a texture of its
/// own, read back a frame later (so the CPU never waits on the card) and shown as a bitmap, so the window
/// around it can stay on WPF's CPU renderer. One device serves every skin in the app, on the UI thread.
/// </summary>
public sealed class SkinGpu : IDisposable
{
    const string VertexSource = """
        struct Corner { float2 uv : TEXCOORD0; float4 position : SV_Position; };
        Corner main(uint id : SV_VertexID)
        {
            // One triangle covering the whole target; uv runs 0 to 1 from the top left, as in WPF.
            Corner o;
            float2 t = float2((id << 1) & 2, id & 2);
            o.uv = t;
            o.position = float4(t * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        """;


    // The 2.0 engine's own passes: bloom (a bright pass while halving, halvings, tent upsamples added back up the
    // chain) and the last pass that lays the glow on the picture and writes what WPF shows.
    const string PassCommon = """
        Texture2D src : register(t0);
        Texture2D extra : register(t1);
        SamplerState linearClamp : register(s0);
        cbuffer EngineInputs : register(b0) { float4 p0; float4 p1; float4 p2; };
        float3 down13(float2 uv, float2 t)
        {
            float3 a = src.SampleLevel(linearClamp, uv + t * float2(-2, -2), 0).rgb;
            float3 b = src.SampleLevel(linearClamp, uv + t * float2( 0, -2), 0).rgb;
            float3 c = src.SampleLevel(linearClamp, uv + t * float2( 2, -2), 0).rgb;
            float3 d = src.SampleLevel(linearClamp, uv + t * float2(-1, -1), 0).rgb;
            float3 e = src.SampleLevel(linearClamp, uv, 0).rgb;
            float3 f = src.SampleLevel(linearClamp, uv + t * float2( 1, -1), 0).rgb;
            float3 g = src.SampleLevel(linearClamp, uv + t * float2(-2,  0), 0).rgb;
            float3 h = src.SampleLevel(linearClamp, uv + t * float2( 2,  0), 0).rgb;
            float3 i = src.SampleLevel(linearClamp, uv + t * float2(-1,  1), 0).rgb;
            float3 j = src.SampleLevel(linearClamp, uv + t * float2( 1,  1), 0).rgb;
            float3 k = src.SampleLevel(linearClamp, uv + t * float2(-2,  2), 0).rgb;
            float3 l = src.SampleLevel(linearClamp, uv + t * float2( 0,  2), 0).rgb;
            float3 m = src.SampleLevel(linearClamp, uv + t * float2( 2,  2), 0).rgb;
            return e * 0.125 + (a + c + k + m) * 0.03125 + (b + g + h + l) * 0.0625 + (d + f + i + j) * 0.125;
        }

        """;

    // p0: the source's texel (xy), the threshold (z), its soft knee (w).
    const string BrightSource = PassCommon + """
        float4 main(float2 uv : TEXCOORD0, float4 pos : SV_Position) : SV_Target
        {
            float3 o = down13(uv, p0.xy);
            float br = max(o.r, max(o.g, o.b));
            float knee = max(p0.w, 1e-4);
            float soft = clamp(br - p0.z + knee, 0, 2 * knee);
            soft = soft * soft / (4 * knee);
            return float4(o * (max(soft, br - p0.z) / max(br, 1e-4)), 1);
        }
        """;

    const string DownSource = PassCommon + """
        float4 main(float2 uv : TEXCOORD0, float4 pos : SV_Position) : SV_Target { return float4(down13(uv, p0.xy), 1); }
        """;

    // A 3x3 tent over the smaller level, added (blending) onto the larger one.
    const string UpSource = PassCommon + """
        float4 main(float2 uv : TEXCOORD0, float4 pos : SV_Position) : SV_Target
        {
            float2 t = p0.xy;
            float3 o = src.SampleLevel(linearClamp, uv, 0).rgb * 4
                + (src.SampleLevel(linearClamp, uv + t * float2(0, -1), 0).rgb + src.SampleLevel(linearClamp, uv + t * float2(-1, 0), 0).rgb
                 + src.SampleLevel(linearClamp, uv + t * float2(1, 0), 0).rgb + src.SampleLevel(linearClamp, uv + t * float2(0, 1), 0).rgb) * 2
                + src.SampleLevel(linearClamp, uv + t * float2(-1, -1), 0).rgb + src.SampleLevel(linearClamp, uv + t * float2(1, -1), 0).rgb
                + src.SampleLevel(linearClamp, uv + t * float2(-1, 1), 0).rgb + src.SampleLevel(linearClamp, uv + t * float2(1, 1), 0).rgb;
            return float4(o / 16, 0);
        }
        """;

    // The picture (premultiplied) plus the glow times p1.x; the alpha rises where the glow lands, so it shows on the
    // desktop too, and the color never exceeds it (a valid premultiplied pixel). Then the skin's layers: the back one
    // under it (p1.y), the front one over it (p1.z), found at p2.xy + uv * p2.zw in their pictures.
    const string CompositeSource = PassCommon + """
        Texture2D layerBack : register(t2);
        Texture2D layerFront : register(t3);
        float4 main(float2 uv : TEXCOORD0, float4 pos : SV_Position) : SV_Target
        {
            float4 c = src.Load(int3(pos.xy, 0));
            float3 rgb = max(c.rgb, 0) + extra.SampleLevel(linearClamp, uv, 0).rgb * p1.x;
            float a = saturate(max(c.a, max(rgb.r, max(rgb.g, rgb.b))));
            float4 o = float4(min(rgb, a), a);
            float2 l = p2.xy + uv * p2.zw;
            if (all(l >= 0) && all(l <= 1))
            {
                if (p1.y > 0.5) o += layerBack.SampleLevel(linearClamp, l, 0) * (1 - o.a);
                if (p1.z > 0.5) { float4 f = layerFront.SampleLevel(linearClamp, l, 0); o = f + o * (1 - f.a); }
            }
            return o;
        }
        """;

    /// <summary>The app's one renderer, once <see cref="Start"/> has succeeded.</summary>
    public static SkinGpu? Shared { get; private set; }

    // The song's cover for skins that ask for it ("//! cover"): its pixels, set on the UI thread, uploaded (with a
    // chain of smaller copies for blurred reads) when a skin needs them and they changed.
    static byte[]? _coverPixels;
    static int _coverWidth, _coverHeight, _coverVersion;

    /// <summary>There is a cover to show, and its width over its height.</summary>
    public static bool HasCover => _coverPixels != null;
    public static float CoverAspect => _coverHeight > 0 ? (float)_coverWidth / _coverHeight : 1;

    /// <summary>The cover skins may sample (null: none). Kept at 512 pixels at most.</summary>
    public static void SetCover(BitmapSource? cover)
    {
        if (cover == null)
        {
            if (_coverPixels == null) return;
            _coverPixels = null;
            _coverVersion++;
            return;
        }
        try
        {
            BitmapSource src = cover;
            double longest = Math.Max(src.PixelWidth, src.PixelHeight);
            if (longest > 512) src = new TransformedBitmap(src, new ScaleTransform(512 / longest, 512 / longest));
            var bgra = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);
            (_coverPixels, _coverWidth, _coverHeight) = (pixels, w, h);
            _coverVersion++;
        }
        catch (Exception ex)
        {
            Log.Error("Reading the cover for skins failed", ex);
        }
    }

    /// <summary>
    /// Brings up Direct3D 11 on the default graphics card. False, with the reason logged, when there is
    /// none to be had (a remote session, a broken driver); skins then stay with WPF.
    /// </summary>
    public static bool Start()
    {
        if (Shared != null) return true;
        try
        {
            Shared = new SkinGpu();
            Log.Info($"Direct3D 11 ready for skins ({Shared.Level}, {Shared.PixelTarget}) on {Shared.Adapter}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Direct3D 11 isn't available, skins stay with WPF", ex);
            return false;
        }
    }

    // The device and what hangs off it: there only while skins are being drawn (see Sleep).
    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    ID3D11VertexShader? _vertex;
    ID3D11Buffer? _inputs;
    ID3D11Texture2D? _audio;
    ID3D11ShaderResourceView? _audioView;
    ID3D11SamplerState? _sampler;
    // The 2.0 engine's passes, their constants, the additive blend of the upsamples, and the cover on the card.
    ID3D11PixelShader? _bright, _down, _up, _composite;
    ID3D11Buffer? _passInputs;
    ID3D11BlendState? _additive;
    ID3D11Texture2D? _cover;
    ID3D11ShaderResourceView? _coverView;
    int _coverUploaded = -1;
    readonly Dictionary<byte[], ID3D11PixelShader?> _shaders = new(ReferenceEqualityComparer.Instance);
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkinTarget, object> _targets = new();
    // Each canvas of skin layers on the card (SkinLayers.cs): taken anew when it was drawn again.
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkinLayerCanvas, LayerTextures> _layers = new();
    readonly byte[] _vertexCode;
    readonly byte[] _brightCode, _downCode, _upCode, _compositeCode;
    readonly System.Windows.Threading.DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(10) };
    DateTime _lastDraw;
    bool _lost;

    SkinGpu()
    {
        _vertexCode = SkinLibrary.CompileOwn(VertexSource, "main", "vs_4_0");
        _brightCode = SkinLibrary.CompileOwn(BrightSource, "main", "ps_4_0");
        _downCode = SkinLibrary.CompileOwn(DownSource, "main", "ps_4_0");
        _upCode = SkinLibrary.CompileOwn(UpSource, "main", "ps_4_0");
        _compositeCode = SkinLibrary.CompileOwn(CompositeSource, "main", "ps_4_0");
        // Made once to learn what the card can do, then put to sleep until a skin is drawn: a device costs the
        // process 30 MB or so (measured on an RTX 2060), wasted on everyone who uses the built-in styles.
        CreateDevice();
        PixelTarget = Level >= FeatureLevel.Level_11_0 ? "ps_5_0" : "ps_4_0";
        _idle.Tick += (_, _) =>
        {
            if (DateTime.Now - _lastDraw > IdleAfter) Sleep();
        };
        Sleep();
    }

    /// <summary>No skin drawn for this long (a style chosen instead, the settings closed): the device goes.</summary>
    static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Lets go of the device and every target's textures, so the memory really comes back (each texture holds
    /// on to its device). The next frame drawn makes a new one.
    /// </summary>
    void Sleep()
    {
        _idle.Stop();
        if (_device == null) return;
        foreach (var (target, _) in _targets) target.Release();
        ReleaseDevice();
    }

    /// <summary>The shader model skins are compiled for: ps_5_0, or ps_4_0 on a card from before Direct3D 11.</summary>
    public string PixelTarget { get; }

    /// <summary>Show each frame as soon as it is drawn, waiting for the card (for tools that grab frames, not for the app).</summary>
    public static bool WaitForFrames { get; set; }

    public FeatureLevel Level { get; private set; }

    public string Adapter { get; private set; } = "";

    /// <summary>Bumped whenever the device is made anew: targets made on an older one start over.</summary>
    internal int Generation { get; private set; }

    /// <summary>The same, for tools that check a lost device was replaced.</summary>
    public int GenerationNow => Generation;

    void CreateDevice()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
            out var device, out var level, out var context).CheckError();
        _device = device!;
        _context = context!;
        Level = level;
        using (var dxgi = _device.QueryInterface<IDXGIDevice>())
        using (var adapter = dxgi.GetAdapter())
            Adapter = adapter.Description.Description;
        _vertex = _device.CreateVertexShader(_vertexCode);
        _inputs = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<SkinInputs>(), BindFlags.ConstantBuffer,
            ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.None, 0));
        // The audio as a 256 by 2 picture: the spectrum across the first row, the waveform across the second.
        _audio = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, 256, 2, 1, 1, BindFlags.ShaderResource,
            ResourceUsage.Dynamic, CpuAccessFlags.Write, 1, 0, ResourceOptionFlags.None));
        _audioView = _device.CreateShaderResourceView(_audio);
        _sampler = _device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp,
            TextureAddressMode.Clamp, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, new Vortice.Mathematics.Color4(0, 0, 0, 0), 0, float.MaxValue));
        _bright = _device.CreatePixelShader(_brightCode);
        _down = _device.CreatePixelShader(_downCode);
        _up = _device.CreatePixelShader(_upCode);
        _composite = _device.CreatePixelShader(_compositeCode);
        _passInputs = _device.CreateBuffer(new BufferDescription(48, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write,
            ResourceOptionFlags.None, 0));
        _additive = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.One));
        _coverUploaded = -1;
        Generation++;
    }

    void ReleaseDevice()
    {
        foreach (var (_, layers) in _layers) layers.Release();
        foreach (var s in _shaders.Values) s?.Dispose();
        _shaders.Clear();
        _coverView?.Dispose();
        _cover?.Dispose();
        _additive?.Dispose();
        _passInputs?.Dispose();
        _composite?.Dispose();
        _up?.Dispose();
        _down?.Dispose();
        _bright?.Dispose();
        (_coverView, _cover, _additive, _passInputs, _composite, _up, _down, _bright) = (null, null, null, null, null, null, null, null);
        _sampler?.Dispose();
        _audioView?.Dispose();
        _audio?.Dispose();
        _inputs?.Dispose();
        _vertex?.Dispose();
        _context?.ClearState();
        _context?.Dispose();
        _device?.Dispose();
        (_sampler, _audioView, _audio, _inputs, _vertex, _context, _device) = (null, null, null, null, null, null, null);
    }

    /// <summary>
    /// The card was reset or its driver updated under us: start again with a new device on the next frame.
    /// Logged, since a driver that keeps falling over is worth knowing about.
    /// </summary>
    void Lose(Exception ex)
    {
        if (_lost) return;
        _lost = true;
        Log.Error($"Direct3D 11 lost its device ({_device?.DeviceRemovedReason.Description}); making a new one", ex);
    }

    /// <summary>A device to draw with: made on the first frame after a sleep, made anew after one was lost.</summary>
    bool Ready()
    {
        if (!_lost && _device != null) return true;
        try
        {
            ReleaseDevice();
            CreateDevice();
            _lost = false;
            _idle.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Making a new Direct3D 11 device failed", ex);
            return false;
        }
    }

    ID3D11PixelShader? Shader(byte[] code)
    {
        if (_shaders.TryGetValue(code, out var shader)) return shader;
        // Skins come and go as the gallery is browsed; a long session would otherwise keep every one.
        if (_shaders.Count >= 256)
        {
            foreach (var s in _shaders.Values) s?.Dispose();
            _shaders.Clear();
        }
        try
        {
            shader = _device!.CreatePixelShader(code);
        }
        catch (Exception ex)
        {
            Log.Error("A skin's compiled shader was turned down by Direct3D 11", ex);
            shader = null;
        }
        _shaders[code] = shader;
        return shader;
    }

    /// <summary>
    /// Draws one frame of a skin into a target of the given size in pixels, and brings the frame before it
    /// to the target's bitmap. False when it can't be drawn (no device, a shader the card refuses).
    /// </summary>
    public bool Draw(SkinTarget target, byte[] code, in SkinInputs inputs, ReadOnlySpan<byte> audio, int width, int height,
                     SkinFeatures? features = null, SkinLayerCanvas? layers = null)
    {
        if (!Ready()) return false;
        _lastDraw = DateTime.Now;
        _targets.TryAdd(target, this);
        try
        {
            // Ready() made all of these.
            var (device, context, inputBuffer, audioTexture) = (_device!, _context!, _inputs!, _audio!);
            var shader = Shader(code);
            if (shader == null) return false;
            target.Fit(this, device, width, height);
            if (features is { Any: true })
            {
                DrawEngine(target, shader, inputs, audio, width, height, features, layers);
                return true;
            }

            var mapped = context.Map(inputBuffer, MapMode.WriteDiscard, MapFlags.None);
            Marshal.StructureToPtr(inputs, mapped.DataPointer, false);
            context.Unmap(inputBuffer, 0);
            mapped = context.Map(audioTexture, 0, MapMode.WriteDiscard, MapFlags.None);
            unsafe
            {
                fixed (byte* src = audio)
                    for (int y = 0; y < 2; y++)
                        Buffer.MemoryCopy(src + y * 256 * 4, (byte*)mapped.DataPointer + y * mapped.RowPitch, 256 * 4, 256 * 4);
            }
            context.Unmap(audioTexture, 0);

            context.OMSetRenderTargets(target.View!, null);
            context.RSSetViewport(0, 0, width, height, 0, 1);
            context.ClearRenderTargetView(target.View!, new Vortice.Mathematics.Color4(0, 0, 0, 0));
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertex!);
            context.PSSetShader(shader);
            context.PSSetConstantBuffer(0, inputBuffer);
            context.PSSetShaderResource(1, _audioView!);
            context.PSSetSampler(1, _sampler!);
            context.Draw(3, 0);
            target.Capture(context);
            // Sent to the card now: left in the queue, the copy never finishes and the frame never comes back.
            context.Flush();
            target.Collect(context, WaitForFrames);
            return true;
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            Lose(ex);
            return false;
        }
    }

    void WriteInputs(in SkinInputs inputs)
    {
        var mapped = _context!.Map(_inputs!, MapMode.WriteDiscard, MapFlags.None);
        Marshal.StructureToPtr(inputs, mapped.DataPointer, false);
        _context.Unmap(_inputs!, 0);
    }

    void WritePass(float a, float b, float c, float d, float strength, float back = 0, float front = 0, Vector4 map = default)
    {
        var mapped = _context!.Map(_passInputs!, MapMode.WriteDiscard, MapFlags.None);
        unsafe
        {
            var p = (float*)mapped.DataPointer;
            p[0] = a; p[1] = b; p[2] = c; p[3] = d;
            p[4] = strength; p[5] = back; p[6] = front; p[7] = 0;
            p[8] = map.X; p[9] = map.Y; p[10] = map.Z; p[11] = map.W;
        }
        _context.Unmap(_passInputs!, 0);
    }

    /// <summary>A canvas of skin layers as the card has it: a back and a front picture, each there only when used.</summary>
    sealed class LayerTextures
    {
        public ID3D11Texture2D? Back, Front;
        public ID3D11ShaderResourceView? BackView, FrontView;
        public int Version = -1, Generation = -1, Width, Height;

        public void Release()
        {
            BackView?.Dispose();
            Back?.Dispose();
            FrontView?.Dispose();
            Front?.Dispose();
            (BackView, Back, FrontView, Front) = (null, null, null, null);
            Version = -1;
        }
    }

    /// <summary>The layers' pictures on the card, sent again when they were drawn again since.</summary>
    (ID3D11ShaderResourceView? Back, ID3D11ShaderResourceView? Front) LayerViews(SkinLayerCanvas canvas)
    {
        var t = _layers.GetValue(canvas, _ => new LayerTextures());
        int w = canvas.PixelWidth, h = canvas.PixelHeight;
        if (t.Generation != Generation || t.Width != w || t.Height != h || (t.Back != null) != (canvas.Back != null) || (t.Front != null) != (canvas.Front != null))
        {
            t.Release();
            (t.Generation, t.Width, t.Height) = (Generation, w, h);
            if (canvas.Back != null) (t.Back, t.BackView) = Dynamic(w, h);
            if (canvas.Front != null) (t.Front, t.FrontView) = Dynamic(w, h);
        }
        if (t.Version != canvas.Version)
        {
            Upload(t.Back, canvas.Back, h);
            Upload(t.Front, canvas.Front, h);
            t.Version = canvas.Version;
        }
        return (t.BackView, t.FrontView);
    }

    (ID3D11Texture2D, ID3D11ShaderResourceView) Dynamic(int w, int h)
    {
        var texture = _device!.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.ShaderResource,
            ResourceUsage.Dynamic, CpuAccessFlags.Write, 1, 0, ResourceOptionFlags.None));
        return (texture, _device.CreateShaderResourceView(texture));
    }

    unsafe void Upload(ID3D11Texture2D? texture, SkiaSharp.SKBitmap? bitmap, int height)
    {
        if (texture == null || bitmap == null) return;
        var mapped = _context!.Map(texture, 0, MapMode.WriteDiscard, MapFlags.None);
        var src = (byte*)bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes, copy = Math.Min(rowBytes, (int)mapped.RowPitch);
        for (int y = 0; y < height; y++)
            Buffer.MemoryCopy(src + y * rowBytes, (byte*)mapped.DataPointer + y * mapped.RowPitch, copy, copy);
        _context.Unmap(texture, 0);
    }

    /// <summary>The cover on the card, uploaded again when it changed, with its smaller copies made; null when there is none.</summary>
    ID3D11ShaderResourceView? CoverView()
    {
        var pixels = _coverPixels;
        if (pixels == null) return null;
        if (_coverUploaded == _coverVersion && _coverView != null) return _coverView;
        _coverView?.Dispose();
        _cover?.Dispose();
        int w = _coverWidth, h = _coverHeight;
        _cover = _device!.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 0,
            BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips));
        unsafe
        {
            fixed (byte* p = pixels)
                _context!.UpdateSubresource(_cover, 0, null, (IntPtr)p, (uint)(w * 4), (uint)(w * h * 4));
        }
        _coverView = _device.CreateShaderResourceView(_cover);
        _context!.GenerateMips(_coverView);
        _coverUploaded = _coverVersion;
        return _coverView;
    }

    /// <summary>One full-target draw of the shader bound with whatever is bound.</summary>
    void Fill(ID3D11RenderTargetView view, int width, int height, ID3D11PixelShader shader, bool clear = true)
    {
        var context = _context!;
        context.OMSetRenderTargets(view, null);
        context.RSSetViewport(0, 0, width, height, 0, 1);
        if (clear) context.ClearRenderTargetView(view, new Vortice.Mathematics.Color4(0, 0, 0, 0));
        context.PSSetShader(shader);
        context.Draw(3, 0);
    }

    /// <summary>
    /// The 2.0 engine's frame. The skin draws into a 16-bit float picture: with feedback it reads its last frame
    /// (t2) and its frame is kept for the next (two pictures taking turns); with a state pass it draws twice, the
    /// state (pass 0, reading the last state) and then the picture (pass 1, reading the state just made). Then the
    /// bloom chain, and the last pass that writes the picture and its glow for WPF.
    /// </summary>
    void DrawEngine(SkinTarget target, ID3D11PixelShader shader, SkinInputs inputs, ReadOnlySpan<byte> audio, int width, int height,
                    SkinFeatures features, SkinLayerCanvas? layers)
    {
        var (device, context) = (_device!, _context!);
        target.FitEngine(device, width, height, features);

        // The skin's layers, for the skin to read (t4, t5) and for the last pass to lay round its picture.
        ID3D11ShaderResourceView? layerBack = null, layerFront = null;
        if (layers != null) (layerBack, layerFront) = LayerViews(layers);
        inputs.Layers = layers != null
            ? new Vector4(layers.MapA, layers.MapK, layerBack != null ? 1 : 0, layerFront != null ? 1 : 0)
            : default;

        var mapped = context.Map(_audio!, 0, MapMode.WriteDiscard, MapFlags.None);
        unsafe
        {
            fixed (byte* src = audio)
                for (int y = 0; y < 2; y++)
                    Buffer.MemoryCopy(src + y * 256 * 4, (byte*)mapped.DataPointer + y * mapped.RowPitch, 256 * 4, 256 * 4);
        }
        context.Unmap(_audio!, 0);

        var cover = features.Cover ? CoverView() : null;
        inputs.Engine.Z = cover != null ? CoverAspect : 1;
        inputs.Engine.W = cover != null ? 1 : 0;

        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertex!);
        context.PSSetConstantBuffer(0, _inputs!);
        context.PSSetShaderResource(1, _audioView!);
        context.PSSetSampler(1, _sampler!);
        context.PSSetSampler(2, _sampler!);
        context.PSSetShaderResource(3, cover!);
        context.PSSetShaderResource(4, layerBack!);
        context.PSSetShaderResource(5, layerFront!);
        context.OMSetBlendState(null);

        if (target.StateNeedsClear)
        {
            foreach (var s in target.States) if (s != null) context.ClearRenderTargetView(s.View, new Vortice.Mathematics.Color4(0, 0, 0, 0));
            target.StateNeedsClear = false;
        }

        ID3D11ShaderResourceView picture;
        if (features.Feedback)
        {
            var (last, now) = (target.States[target.Turn ^ 1]!, target.States[target.Turn]!);
            if (features.StatePass)
            {
                inputs.Engine.Y = 0;
                WriteInputs(inputs);
                context.OMSetRenderTargets(now.View, null);
                context.PSSetShaderResource(2, last.Resource);
                Fill(now.View, width, height, shader);
                inputs.Engine.Y = 1;
                WriteInputs(inputs);
                context.OMSetRenderTargets(target.Scene!.View, null);
                context.PSSetShaderResource(2, now.Resource);
                Fill(target.Scene.View, width, height, shader);
                picture = target.Scene.Resource;
            }
            else
            {
                inputs.Engine.Y = 1;
                WriteInputs(inputs);
                context.OMSetRenderTargets(now.View, null);
                context.PSSetShaderResource(2, last.Resource);
                Fill(now.View, width, height, shader);
                picture = now.Resource;
            }
            target.Turn ^= 1;
        }
        else
        {
            inputs.Engine.Y = 1;
            WriteInputs(inputs);
            context.PSSetShaderResource(2, null!);
            Fill(target.Scene!.View, width, height, shader);
            picture = target.Scene.Resource;
        }
        context.PSSetShaderResource(2, null!);
        context.PSSetShaderResource(3, null!);
        context.PSSetShaderResource(4, null!);
        context.PSSetShaderResource(5, null!);

        // Bloom: bright pass while halving into level 1, halvings down the chain, then each level's tent upsample
        // added onto the one above it; level 1 ends up with the glow.
        context.PSSetConstantBuffer(0, _passInputs!);
        context.PSSetSampler(0, _sampler!);
        ID3D11ShaderResourceView? glow = null;
        var levels = target.Bloom;
        if (features.Bloom > 0 && levels.Length > 0)
        {
            WritePass(1f / width, 1f / height, features.BloomThreshold, Math.Max(0.05f, features.BloomThreshold * 0.5f), 0);
            context.OMSetRenderTargets(levels[0].View, null);
            context.PSSetShaderResource(0, picture);
            Fill(levels[0].View, levels[0].Width, levels[0].Height, _bright!);
            for (int i = 1; i < levels.Length; i++)
            {
                WritePass(1f / levels[i - 1].Width, 1f / levels[i - 1].Height, 0, 0, 0);
                context.OMSetRenderTargets(levels[i].View, null);
                context.PSSetShaderResource(0, levels[i - 1].Resource);
                Fill(levels[i].View, levels[i].Width, levels[i].Height, _down!);
            }
            context.OMSetBlendState(_additive);
            for (int i = levels.Length - 1; i > 0; i--)
            {
                WritePass(1f / levels[i].Width, 1f / levels[i].Height, 0, 0, 0);
                context.PSSetShaderResource(0, null!);
                context.OMSetRenderTargets(levels[i - 1].View, null);
                context.PSSetShaderResource(0, levels[i].Resource);
                Fill(levels[i - 1].View, levels[i - 1].Width, levels[i - 1].Height, _up!, clear: false);
            }
            context.OMSetBlendState(null);
            glow = levels[0].Resource;
        }

        // Where this stretch's pixels fall in the layers' pictures: across, as its share of the bar; down, through the
        // skin's own y (the stretch's y less where the bar starts in it, over the bar's share of it).
        var map = default(Vector4);
        if (layers != null)
        {
            float barShare = inputs.Bar.Y > 0 ? inputs.Bar.Y : 1;
            float k = layers.MapK / barShare;
            map = new Vector4(inputs.Span.X, layers.MapA - inputs.Bar.X * k, inputs.Span.Y, k);
        }
        WritePass(0, 0, 0, 0, glow != null ? features.Bloom : 0, layerBack != null ? 1 : 0, layerFront != null ? 1 : 0, map);
        context.PSSetShaderResource(0, null!);
        context.OMSetRenderTargets(target.View!, null);
        context.PSSetShaderResource(0, picture);
        context.PSSetShaderResource(1, glow!);
        context.PSSetShaderResource(2, layerBack!);
        context.PSSetShaderResource(3, layerFront!);
        Fill(target.View!, width, height, _composite!);
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(1, null!);
        context.PSSetShaderResource(2, null!);
        context.PSSetShaderResource(3, null!);

        target.Capture(context);
        context.Flush();
        target.Collect(context, WaitForFrames);
    }

    public void Dispose()
    {
        ReleaseDevice();
        if (ReferenceEquals(Shared, this)) Shared = null;
    }
}

/// <summary>A 16-bit float picture the 2.0 engine draws into and reads from.</summary>
internal sealed class EngineSurface : IDisposable
{
    public required ID3D11Texture2D Texture { get; init; }
    public required ID3D11RenderTargetView View { get; init; }
    public required ID3D11ShaderResourceView Resource { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public static EngineSurface Make(ID3D11Device device, int width, int height)
    {
        var texture = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)Math.Max(1, width), (uint)Math.Max(1, height),
            1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        return new EngineSurface
        {
            Texture = texture,
            View = device.CreateRenderTargetView(texture),
            Resource = device.CreateShaderResourceView(texture),
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
        };
    }

    public void Dispose()
    {
        Resource.Dispose();
        View.Dispose();
        Texture.Dispose();
    }
}

/// <summary>
/// Where one stretch of a skin is drawn: the texture on the card, two copies the CPU can read (one being
/// filled while the other is read), and the bitmap WPF shows.
/// </summary>
public sealed class SkinTarget : IDisposable
{
    const int WasStillDrawing = unchecked((int)0x887A000A);

    ID3D11Texture2D? _texture;
    readonly ID3D11Texture2D?[] _readable = new ID3D11Texture2D?[2];
    readonly bool[] _filled = new bool[2];
    int _next;
    int _width, _height, _generation = -1;

    internal ID3D11RenderTargetView? View { get; private set; }

    /// <summary>The latest frame that came back from the card; replaced (and BitmapChanged raised) when the size changes.</summary>
    public WriteableBitmap? Bitmap { get; private set; }

    public event Action? BitmapChanged;

    internal void Fit(SkinGpu gpu, ID3D11Device device, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && gpu.Generation == _generation && _texture != null) return;
        Release();
        _width = width;
        _height = height;
        _generation = gpu.Generation;
        _texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        View = device.CreateRenderTargetView(_texture);
        for (int i = 0; i < 2; i++)
        {
            _readable[i] = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
                BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
            _filled[i] = false;
        }
        if (Bitmap == null || Bitmap.PixelWidth != width || Bitmap.PixelHeight != height)
        {
            // Premultiplied, as the skins paint: paint() multiplies the color by its alpha.
            Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            BitmapChanged?.Invoke();
        }
    }

    /// <summary>Copies the frame just drawn to a readable texture.</summary>
    internal void Capture(ID3D11DeviceContext context)
    {
        context.CopyResource(_readable[_next]!, _texture!);
        _filled[_next] = true;
        _next ^= 1;
    }

    /// <summary>
    /// Brings back the frame before (the copy now at _next), if the card is done with it: a frame late, but
    /// the UI thread never waits. Still drawing, the bitmap keeps the frame it has. With <paramref name="wait"/>,
    /// the frame just drawn instead, waiting for the card (tools that grab frames).
    /// </summary>
    internal unsafe void Collect(ID3D11DeviceContext context, bool wait)
    {
        int i = wait ? _next ^ 1 : _next;
        if (!_filled[i] || Bitmap == null) return;
        var result = context.Map(_readable[i]!, 0, MapMode.Read, wait ? MapFlags.None : MapFlags.DoNotWait, out var mapped);
        if (result.Code == WasStillDrawing) return;
        result.CheckError();
        try
        {
            Bitmap.Lock();
            int rowBytes = _width * 4;
            for (int y = 0; y < _height; y++)
                Buffer.MemoryCopy((byte*)mapped.DataPointer + y * mapped.RowPitch, (byte*)Bitmap.BackBuffer + y * Bitmap.BackBufferStride,
                    rowBytes, rowBytes);
            Bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            Bitmap.Unlock();
        }
        finally
        {
            context.Unmap(_readable[i]!, 0);
        }
        _filled[i] = false;
    }

    // The 2.0 engine's pictures: the skin's (when it isn't one of the states), the two states taking turns, the bloom chain.
    internal EngineSurface? Scene { get; private set; }
    internal EngineSurface?[] States { get; } = new EngineSurface?[2];
    internal EngineSurface[] Bloom { get; private set; } = [];
    internal int Turn { get; set; }
    internal bool StateNeedsClear { get; set; }
    SkinFeatures? _engineFor;
    int _engineWidth, _engineHeight;

    /// <summary>The kept state starts over (a new song): cleared before the next frame.</summary>
    public void ResetState() => StateNeedsClear = true;

    /// <summary>The 2.0 engine's pictures for this size and these features (made with the target, gone with it).</summary>
    internal void FitEngine(ID3D11Device device, int width, int height, SkinFeatures features)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (Scene != null && _engineFor == features && _engineWidth == width && _engineHeight == height) return;
        ReleaseEngine();
        (_engineFor, _engineWidth, _engineHeight) = (features, width, height);
        if (!features.Feedback || features.StatePass) Scene = EngineSurface.Make(device, width, height);
        if (features.Feedback)
        {
            States[0] = EngineSurface.Make(device, width, height);
            States[1] = EngineSurface.Make(device, width, height);
            StateNeedsClear = true;
        }
        // A picture to show without a scene of its own (one-pass feedback) still needs Scene set for the check above.
        Scene ??= States[0];
        if (features.Bloom > 0)
        {
            // Halvings down to a few pixels on the short side (a 48 px bar: 24, 12, 6, 3).
            int count = Math.Clamp((int)Math.Floor(Math.Log2(Math.Min(width, height))) - 1, 1, 6);
            var levels = new EngineSurface[count];
            for (int i = 0; i < count; i++) levels[i] = EngineSurface.Make(device, Math.Max(1, width >> (i + 1)), Math.Max(1, height >> (i + 1)));
            Bloom = levels;
        }
    }

    void ReleaseEngine()
    {
        if (Scene != null && !ReferenceEquals(Scene, States[0]) && !ReferenceEquals(Scene, States[1])) Scene.Dispose();
        Scene = null;
        for (int i = 0; i < 2; i++)
        {
            States[i]?.Dispose();
            States[i] = null;
        }
        foreach (var level in Bloom) level.Dispose();
        Bloom = [];
        _engineFor = null;
    }

    /// <summary>Lets go of the textures (the renderer going to sleep, or this stretch leaving the screen); the next frame makes them anew.</summary>
    internal void Release()
    {
        ReleaseEngine();
        View?.Dispose();
        View = null;
        _texture?.Dispose();
        _texture = null;
        for (int i = 0; i < 2; i++)
        {
            _readable[i]?.Dispose();
            _readable[i] = null;
        }
    }

    public void Dispose() => Release();
}
