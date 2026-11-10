using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

public sealed record TrackDetails(string? Genre, int? Year, string? Album, string? Artist);

/// <summary>
/// Looks up genre, release year and album on the public iTunes Search API,
/// since most players do not publish them. Results are cached per track.
/// </summary>
public sealed class TrackInfoService
{
    sealed class SearchResponse
    {
        public List<Item>? Results { get; set; }
    }

    sealed class Item
    {
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? CollectionName { get; set; }
        public string? PrimaryGenreName { get; set; }
        public string? ReleaseDate { get; set; }
        public long? TrackTimeMillis { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    readonly AppSettings _settings;
    readonly Dictionary<string, TrackDetails?> _cache = new();
    HttpClient? _http;
    string? _httpProxy;

    public TrackInfoService(AppSettings settings) => _settings = settings;

    public bool TryGetCached(string artist, string title, out TrackDetails? details)
        => _cache.TryGetValue(Key(artist, title), out details);

    /// <param name="appId">The app playing it, which helps tell a song from a meeting or a web page; empty when unknown.</param>
    public async Task<TrackDetails?> LookupAsync(string artist, string title, TimeSpan duration, CancellationToken ct, string appId = "")
    {
        if (!_settings.OnlineTrackInfo || string.IsNullOrWhiteSpace(title)) return null;
        if (!SongGuess.IsLikelySong(title, artist, duration, appId)) return null;
        var key = Key(artist, title);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var (cleanArtist, cleanTitle) = TitleCleaner.Clean(artist, title);
        try
        {
            var country = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            var term = Uri.EscapeDataString($"{TitleCleaner.FirstArtist(cleanArtist)} {cleanTitle}".Trim());
            using var response = await Client().GetAsync(
                $"search?term={term}&media=music&entity=song&limit=10&country={country}", ct);
            response.EnsureSuccessStatusCode();
            var data = JsonSerializer.Deserialize<SearchResponse>(await response.Content.ReadAsStringAsync(ct), Json);
            var best = Pick(data?.Results ?? [], cleanArtist, cleanTitle, duration);
            TrackDetails? details = best == null ? null : new TrackDetails(
                best.PrimaryGenreName,
                DateTime.TryParse(best.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d) ? d.Year : null,
                best.CollectionName,
                best.ArtistName);
            _cache[key] = details;
            return details;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Track info lookup failed for '{artist} - {title}'", ex);
            return null;
        }
    }

    static Item? Pick(List<Item> items, string artist, string title, TimeSpan duration)
    {
        Item? best = null;
        double bestScore = 0;
        foreach (var item in items)
        {
            double titleSim = TitleCleaner.Similarity(item.TrackName ?? "", title);
            double artistSim = artist.Length > 0 ? TitleCleaner.Similarity(item.ArtistName ?? "", artist) : 0.5;
            if (titleSim < 0.5 || artistSim < 0.3) continue;
            double score = titleSim * 2 + artistSim;
            if (duration > TimeSpan.Zero && item.TrackTimeMillis is long ms)
            {
                double diff = Math.Abs(ms / 1000.0 - duration.TotalSeconds);
                score += diff <= 3 ? 1 : diff <= 10 ? 0.3 : -1;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }
        return best;
    }

    static string Key(string artist, string title) => $"{TitleCleaner.Loose(artist)}|{TitleCleaner.Loose(title)}";

    HttpClient Client()
    {
        var proxy = _settings.ProxyUrl;
        if (_http != null && proxy == _httpProxy) return _http;
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }
        _http?.Dispose();
        _http = new HttpClient(handler) { BaseAddress = new Uri("https://itunes.apple.com/"), Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicBar/1.0 (Windows desktop lyrics bar)");
        _httpProxy = proxy;
        return _http;
    }
}
