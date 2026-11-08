using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>One thing in the online gallery, as the server lists it (its links already absolute).</summary>
public sealed record GalleryItem(int Id, string Slug, string Type, string Category, string Name, string Description,
    string Author, string Accent, string[] Colors, string Style, int Downloads, string Sha256, long Size,
    string DownloadUrl, string? PreviewUrl, string PageUrl)
{
    public bool IsSkin => Type == "skin";
}

/// <summary>A banner for the settings window: its picture and where a click goes (through the server, which counts it).</summary>
public sealed record AdBanner(int Id, string Title, string Alt, string ImageUrl, string ClickUrl, int Width, int Height);

public sealed record SubmitResult(bool Ok, string Message, IReadOnlyList<string> Findings);

/// <summary>
/// MusicBar's website: the online gallery (browse, install, share) and the banners shown in the settings.
/// Every file installed is checked against the SHA-256 the gallery lists, and a skin must compile before
/// it is kept. The server is musicbar.truclv.com; MUSICBAR_SERVER points elsewhere for testing.
/// </summary>
public sealed class Community(AppSettings settings)
{
    public static string Server { get; } = (Environment.GetEnvironmentVariable("MUSICBAR_SERVER") ?? "https://musicbar.truclv.com").TrimEnd('/');

    static string CacheFolder => Path.Combine(AppPaths.DataDir, "Cache", "Community");

    // Previews compiled at once: a quarter of the cores (one on a 4-thread laptop), so a page of skins
    // doesn't take the whole PC the way 30 compiles side by side did (#17).
    static readonly SemaphoreSlim CompileGate = new(Math.Clamp(Environment.ProcessorCount / 4, 1, 3));

    readonly AppSettings _s = settings;

    HttpClient Client(TimeSpan timeout)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (!string.IsNullOrWhiteSpace(_s.ProxyUrl))
        {
            handler.Proxy = new WebProxy(_s.ProxyUrl);
            handler.UseProxy = true;
        }
        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicBar/{UpdateService.Current}");
        return http;
    }

    /// <summary>The device's id for the gallery (random, made once): lets the server tell one person's floods from many people.</summary>
    public string Device
    {
        get
        {
            if (_s.CommunityDevice.Length == 0) _s.CommunityDevice = Guid.NewGuid().ToString("N");
            return _s.CommunityDevice;
        }
    }

    // ------------------------------------------------------------------ gallery

    public async Task<(IReadOnlyList<GalleryItem> Items, int Total)> GalleryAsync(string category, string sort, int page, CancellationToken ct)
    {
        using var http = Client(TimeSpan.FromSeconds(15));
        var url = $"{Server}/api/v1/gallery?category={Uri.EscapeDataString(category)}&sort={Uri.EscapeDataString(sort)}&page={page}&size=30";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(Item).ToList();
        return (items, doc.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>One item by its slug (a musicbar:// link names it so); null when the gallery has no such item.</summary>
    public async Task<GalleryItem?> ItemAsync(string slug, CancellationToken ct)
    {
        using var http = Client(TimeSpan.FromSeconds(15));
        using var response = await http.GetAsync($"{Server}/api/v1/items/{Uri.EscapeDataString(slug)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Item(doc.RootElement);
    }

    static GalleryItem Item(JsonElement e) => new(
        e.GetProperty("id").GetInt32(), Str(e, "slug"), Str(e, "type"), Str(e, "category"), Str(e, "name"),
        Str(e, "description"), Str(e, "author"), Str(e, "accent"),
        e.TryGetProperty("colors", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [],
        Str(e, "style"), e.TryGetProperty("downloads", out var d) ? d.GetInt32() : 0, Str(e, "sha256"),
        e.TryGetProperty("size", out var z) ? z.GetInt64() : 0, Absolute(Str(e, "download_url"))!,
        Absolute(Str(e, "preview_url")), Absolute(Str(e, "page_url"))!);

    /// <summary>
    /// The item's file as text, checked against the SHA-256 the gallery lists. A preview fetch (a skin's code
    /// to draw its tile) says so, and the server doesn't count it as a download.
    /// </summary>
    public async Task<string> FetchAsync(GalleryItem item, CancellationToken ct, bool preview = false)
    {
        using var http = Client(TimeSpan.FromSeconds(20));
        var bytes = await http.GetByteArrayAsync(preview ? item.DownloadUrl + "?preview=1" : item.DownloadUrl, ct);
        if (bytes.Length > 64 * 1024) throw new InvalidDataException("file too large");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (item.Sha256.Length > 0 && sha != item.Sha256) throw new InvalidDataException("the file doesn't match the gallery's checksum");
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    /// <summary>
    /// A gallery skin's shader for its tile: compiled once and kept by the file's checksum (and what the
    /// compiler was given), so the tile starts at once the next time, without downloading it again. Null
    /// when the skin doesn't compile.
    /// </summary>
    public async Task<byte[]?> PreviewShaderAsync(GalleryItem item, CancellationToken ct)
    {
        var kept = item.Sha256.Length == 64 ? Path.Combine(CacheFolder, $"{item.Sha256[..32]}-{SkinLibrary.CompilerKey}.cso") : null;
        if (kept != null && File.Exists(kept)) return await File.ReadAllBytesAsync(kept, ct);
        var text = await FetchAsync(item, ct, preview: true);
        byte[]? bytecode;
        await CompileGate.WaitAsync(ct);
        try
        {
            bytecode = await Task.Run(() => SkinLibrary.Compile(text, out _), ct);
        }
        finally
        {
            CompileGate.Release();
        }
        if (bytecode != null && kept != null) SkinLibrary.Keep(kept, bytecode);
        return bytecode;
    }

    /// <summary>Downloads and keeps an item: a skin into the skins folder once it compiles, a preset into the presets folder.</summary>
    public async Task<string> InstallAsync(GalleryItem item, CancellationToken ct)
    {
        var text = await FetchAsync(item, ct);
        var name = LookPresets.SafeName(item.Slug);
        if (item.IsSkin)
        {
            // Kept once compiled, so the skin starts at once when it is put on.
            var bytecode = await Task.Run(() => SkinLibrary.CompileKept(text, out var error) ?? throw new InvalidDataException(error ?? "the skin doesn't compile"), ct);
            Directory.CreateDirectory(SkinLibrary.Folder);
            var file = name + ".hlsl";
            await File.WriteAllTextAsync(Path.Combine(SkinLibrary.Folder, file), text, new UTF8Encoding(false), ct);
            Log.Info($"Gallery skin installed: {file} ({bytecode.Length} bytes of shader)");
            return file;
        }
        if (LookPresets.Save(name, text) is not { } preset) throw new InvalidDataException("not a MusicBar preset");
        return preset.File;
    }

    /// <summary>A picture from the server (a preview or a banner), cached on disk by its address.</summary>
    public async Task<byte[]?> ImageAsync(string url, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            var path = Path.Combine(CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32]);
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(3))
                return await File.ReadAllBytesAsync(path, ct);
            using var http = Client(TimeSpan.FromSeconds(15));
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (bytes.Length > 4 * 1024 * 1024) return null;
            await File.WriteAllBytesAsync(path, bytes, ct);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ sharing

    /// <summary>Sends a skin or a preset for review, with the nickname set in the settings.</summary>
    public async Task<SubmitResult> SubmitAsync(string fileName, byte[] file, string name, string description, string category,
        byte[]? previewPng, CancellationToken ct)
    {
        using var http = Client(TimeSpan.FromSeconds(30));
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },
            { new StringContent(_s.CommunityNickname), "nickname" },
            { new StringContent(description), "description" },
            { new StringContent(category), "category" },
            { new StringContent(Device), "device" },
            { new ByteArrayContent(file), "file", fileName },
        };
        if (previewPng != null) form.Add(new ByteArrayContent(previewPng), "preview", "preview.png");
        try
        {
            using var response = await http.PostAsync($"{Server}/api/v1/submit", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var findings = doc.RootElement.TryGetProperty("findings", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().Select(x => Str(x, "message") + (Str(x, "where") is { Length: > 0 } w ? $" ({w})" : "")).ToList()
                : [];
            bool ok = doc.RootElement.TryGetProperty("ok", out var o) && o.GetBoolean();
            Log.Info($"Gallery submission '{name}': {(ok ? "received" : "refused")} {(int)response.StatusCode}");
            return new SubmitResult(ok, ok ? "" : response.StatusCode == HttpStatusCode.TooManyRequests ? "rate" : "refused", findings);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Error("Sending to the gallery failed", ex);
            return new SubmitResult(false, "network", [ex.Message]);
        }
    }

    // ------------------------------------------------------------------ banners

    public async Task<(IReadOnlyList<AdBanner> Ads, double Seconds)> AdsAsync(CancellationToken ct)
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(10));
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"{Server}/api/v1/ads", ct));
            var ads = doc.RootElement.GetProperty("ads").EnumerateArray()
                .Select(a => new AdBanner(a.GetProperty("id").GetInt32(), Str(a, "title"), Str(a, "alt"), Absolute(Str(a, "image_url"))!,
                    Absolute(Str(a, "click_url"))!, a.GetProperty("width").GetInt32(), a.GetProperty("height").GetInt32()))
                .Where(a => a.ClickUrl.StartsWith(Server, StringComparison.OrdinalIgnoreCase))
                .ToList();
            double seconds = doc.RootElement.TryGetProperty("rotate_seconds", out var s) ? Math.Clamp(s.GetDouble(), 4, 60) : 8;
            return (ads, seconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ([], 8);
        }
    }

    /// <summary>Tells the server a banner was seen (it counts one a person an hour).</summary>
    public async Task SeenAsync(AdBanner ad)
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(10));
            using var _ = await http.PostAsync($"{Server}/api/v1/ads/{ad.Id}/seen", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>A link from the server made absolute; only links to the server itself are kept.</summary>
    static string? Absolute(string link)
    {
        if (string.IsNullOrEmpty(link)) return null;
        if (link.StartsWith('/')) return Server + link;
        return link.StartsWith(Server + "/", StringComparison.OrdinalIgnoreCase) ? link : null;
    }
}
