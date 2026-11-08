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
[StructLayout(LayoutKind.Explicit, Size = 176)]
public struct SkinInputs
{
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


    /// <summary>The app's one renderer, once <see cref="Start"/> has succeeded.</summary>
    public static SkinGpu? Shared { get; private set; }

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
    readonly Dictionary<byte[], ID3D11PixelShader?> _shaders = new(ReferenceEqualityComparer.Instance);
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkinTarget, object> _targets = new();
    readonly byte[] _vertexCode;
    readonly System.Windows.Threading.DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(10) };
    DateTime _lastDraw;
    bool _lost;

    SkinGpu()
    {
        _vertexCode = SkinLibrary.CompileOwn(VertexSource, "main", "vs_4_0");
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
        Generation++;
    }

    void ReleaseDevice()
    {
        foreach (var s in _shaders.Values) s?.Dispose();
        _shaders.Clear();
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
    public bool Draw(SkinTarget target, byte[] code, in SkinInputs inputs, ReadOnlySpan<byte> audio, int width, int height)
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

    public void Dispose()
    {
        ReleaseDevice();
        if (ReferenceEquals(Shared, this)) Shared = null;
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

    /// <summary>Lets go of the textures (the renderer going to sleep, or this stretch leaving the screen); the next frame makes them anew.</summary>
    internal void Release()
    {
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
