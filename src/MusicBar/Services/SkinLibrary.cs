using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>A skin as MusicBar knows it: what its header says, and its compiled shader (or why it didn't compile).</summary>
public sealed record SkinInfo(string File, string Name, string Author, string Description, bool Visualizer, byte[]? Bytecode, string? Error)
{
    public bool Works => Bytecode != null;
}

/// <summary>
/// Skins people make, or have an AI write (docs/skins.md is written for that): one pixel shader in
/// HLSL that paints the taskbar's layer on the GPU, Shadertoy style. MusicBar adds the inputs (time,
/// progress, loudness, beat, the spectrum and the waveform, three palette colors) in front of the
/// file, compiles it with the shader compiler every Windows 10 and 11 has (d3dcompiler_47), and
/// runs it under the taskbar. A shader can only draw: it reads no files and reaches no network.
/// Skins live in %LOCALAPPDATA%\MusicBar\Skins as .hlsl files.
/// </summary>
public static class SkinLibrary
{
    public static string Folder => Path.Combine(AppPaths.DataDir, "Skins");

    /// <summary>Put in front of every skin: its inputs and a few helpers. Kept in step with docs/skins.md.</summary>
    public const string Prelude = """
        sampler2D implicitInput : register(s0);
        sampler2D audioTexture : register(s1);
        float time : register(c0);
        float progress : register(c1);
        float level : register(c2);
        float playing : register(c3);
        float2 size : register(c4);
        float4 colorA : register(c5);
        float4 colorB : register(c6);
        float4 colorC : register(c7);
        float beat : register(c8);
        float bottomEdge : register(c9);
        float2 musicbarSpan : register(c10);
        float spectrum(float x) { return tex2D(audioTexture, float2(saturate(x), 0.25)).r; }
        float wave(float x) { return tex2D(audioTexture, float2(saturate(x), 0.75)).r * 2 - 1; }
        float rise(float2 uv) { return bottomEdge > 0.5 ? 1 - uv.y : uv.y; }
        float4 paint(float3 rgb, float alpha) { alpha = saturate(alpha); return float4(saturate(rgb) * alpha, alpha); }

        """;

    static readonly Dictionary<string, (DateTime Written, SkinInfo Info)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every skin in the folder, compiled (once per change of the file).</summary>
    public static IReadOnlyList<SkinInfo> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.EnumerateFiles(Folder, "*.hlsl")
            .Select(path => Load(Path.GetFileName(path)))
            .OfType<SkinInfo>()
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>One skin by its file name; null when the file is gone.</summary>
    public static SkinInfo? Load(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        var path = Path.Combine(Folder, Path.GetFileName(file));
        if (!File.Exists(path)) return null;
        var written = File.GetLastWriteTimeUtc(path);
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var hit) && hit.Written == written) return hit.Info;
        }
        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return new SkinInfo(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), "", "", false, null, ex.Message);
        }
        var info = Read(Path.GetFileName(path), source);
        if (info.Error != null) Log.Error($"Skin '{info.File}' did not compile: {info.Error}");
        lock (Cache) Cache[path] = (written, info);
        return info;
    }

    /// <summary>Copies a skin into the folder once it compiles; otherwise says why not.</summary>
    public static SkinInfo Import(string path)
    {
        var info = Read(Path.GetFileName(path), File.ReadAllText(path));
        if (!info.Works) return info;
        Directory.CreateDirectory(Folder);
        var name = Path.GetFileName(path);
        if (!name.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name) + ".hlsl";
        File.Copy(path, Path.Combine(Folder, name), overwrite: true);
        Log.Info($"Skin imported: {name}");
        return Load(name)!;
    }

    public static void Delete(string file)
    {
        var path = Path.Combine(Folder, Path.GetFileName(file));
        if (File.Exists(path)) File.Delete(path);
        lock (Cache) Cache.Remove(path);
    }

    /// <summary>Writes the sample skins that ship with MusicBar into the folder (a skin of the same name is left alone).</summary>
    public static int InstallSamples()
    {
        Directory.CreateDirectory(Folder);
        int added = 0;
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("MusicBar.Skins.", StringComparison.Ordinal)))
        {
            var target = Path.Combine(Folder, resource["MusicBar.Skins.".Length..]);
            if (File.Exists(target)) continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var file = File.Create(target);
            stream.CopyTo(file);
            added++;
        }
        return added;
    }

    /// <summary>Reads the header ("//! name: ...") and compiles the shader.</summary>
    static SkinInfo Read(string file, string source)
    {
        string name = Path.GetFileNameWithoutExtension(file), author = "", description = "";
        bool visualizer = false;
        foreach (var raw in source.Split('\n').Take(40))
        {
            var line = raw.Trim();
            if (!line.StartsWith("//!", StringComparison.Ordinal)) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[3..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "name": name = value; break;
                case "author": author = value; break;
                case "description": description = value; break;
                case "kind": visualizer = value.Equals("visualizer", StringComparison.OrdinalIgnoreCase); break;
            }
        }
        var bytecode = Compile(source, out var error);
        return new SkinInfo(file, name, author, description, visualizer, bytecode, error);
    }

    /// <summary>
    /// Put after every skin: the real entry point. It hands the skin's main the point it would be
    /// at on the whole bar, so a bar cut into stretches between the icons is one skin across them
    /// all (musicbarSpan: where this stretch starts along the bar, and its share of it).
    /// </summary>
    const string Entry = """

        #line 1 "musicbar"
        float4 musicbarMain(float2 uv : TEXCOORD) : COLOR { return main(float2(musicbarSpan.x + uv.x * musicbarSpan.y, uv.y)); }
        """;

    /// <summary>
    /// Compiles a skin as a pixel shader (ps_3_0, or ps_2_0 where the GPU has nothing newer), with
    /// the prelude in front and line numbers that still point into the skin's own file.
    /// </summary>
    public static byte[]? Compile(string source, out string? error)
    {
        // A main of another shape won't take the entry point; on its own, its errors are its own.
        return Compile(source + Entry, "musicbarMain", out error) ?? Compile(source, "main", out error);
    }

    static byte[]? Compile(string source, string entry, out string? error)
    {
        error = null;
        var target = RenderCapability.IsPixelShaderVersionSupported(3, 0) ? "ps_3_0" : "ps_2_0";
        var text = Encoding.UTF8.GetBytes(Prelude + "#line 1 \"skin\"\n" + source);
        try
        {
            int hr = D3DCompile(text, (IntPtr)text.Length, "skin", IntPtr.Zero, IntPtr.Zero, entry, target,
                OptimizationLevel3, 0, out var code, out var errors);
            if (hr < 0 || code == null)
            {
                error = errors != null ? BlobText(errors) : $"0x{hr:X8}";
                return null;
            }
            var bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            return bytes;
        }
        catch (DllNotFoundException)
        {
            error = "d3dcompiler_47.dll is missing";
            return null;
        }
    }

    static string BlobText(ID3DBlob blob)
    {
        var text = Marshal.PtrToStringAnsi(blob.GetBufferPointer(), (int)blob.GetBufferSize()) ?? "";
        // "skin(12,5): error X3004: undeclared identifier 'foo'" → "line 12: undeclared identifier 'foo'"
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^.*?\((\d+),\d+(?:-\d+)?\):\s*error\s+X\d+:\s*", "line $1: "))
            .Take(3);
        var joined = string.Join("\n", lines);
        return joined.Length > 0 ? joined : text.Trim();
    }

    const uint OptimizationLevel3 = 1 << 15;

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] IntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll")]
    static extern int D3DCompile(byte[] source, IntPtr size, string name, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2, out ID3DBlob code, out ID3DBlob errors);
}
