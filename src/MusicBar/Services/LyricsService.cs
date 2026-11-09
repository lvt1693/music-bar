using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>
/// Finds synced lyrics: first in the local folder (.lrc files, including ones
/// the user drops in), then on LRCLIB (lrclib.net, the open lyrics database),
/// then, if the user allows it, on NetEase Cloud Music. Downloads are written
/// back as .lrc (with word times when the song was synced word by word) so the
/// next play is instant and offline. Lyrics can also be shared back to LRCLIB.
/// </summary>
public sealed partial class LyricsService
{
    // Every file MusicBar downloaded starts with one of these; files without are the user's own.
    const string MarkerStart = "[re:MusicBar/";
    const string Marker = MarkerStart + "LRCLIB]";
    const string NetEaseMarker = MarkerStart + "NetEase]";
    static readonly TimeSpan MissTtl = TimeSpan.FromDays(1);
    const string NetEaseMiss = "|netease";
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal sealed class LrclibTrack
    {
        public long Id { get; set; }
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? AlbumName { get; set; }
        public double? Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
        /// <summary>LRCLIB's YAML Lyricsfile: the same lyrics, with word times when someone synced them word by word.</summary>
        public string? Lyricsfile { get; set; }

        /// <summary>The synced lyrics as LRC, with word times when there are any.</summary>
        public string? Lrc => Services.Lyricsfile.ToWordLrc(Lyricsfile) ?? SyncedLyrics;

        public bool HasWordSync => Lyricsfile?.Contains("words:", StringComparison.Ordinal) == true;
    }

    readonly AppSettings _settings;
    readonly object _missLock = new();
    readonly Dictionary<string, HttpClient> _http = new();
    readonly NetEaseLyrics _netEase;
    string? _httpProxy;
    Dictionary<string, DateTime>? _misses;

    public LyricsService(AppSettings settings)
    {
        _settings = settings;
        _netEase = new NetEaseLyrics(Client);
    }

    public string Folder => string.IsNullOrWhiteSpace(_settings.LyricsFolder) ? AppPaths.DefaultLyricsDir : _settings.LyricsFolder;

    string MissFile => Path.Combine(Folder, "not-found.json");

    // ------------------------------------------------------------------ public API

    public async Task<LyricsResult> GetAsync(TrackQuery q, bool force, CancellationToken ct)
    {
        if (q.AppId == DemoContent.AppId) return DemoContent.GetLyrics(q.Title);
        if (string.IsNullOrWhiteSpace(q.Title)) return new LyricsResult(LyricsStatus.None, null, "", null);

        Directory.CreateDirectory(Folder);
        var (artist, title) = TitleCleaner.Clean(q.Artist, q.Title);
        // A miss with NetEase off says nothing about NetEase: turning it on asks again.
        var key = MissKey(artist, title) + (_settings.LyricsFromNetEase ? NetEaseMiss : "");

        if (!force)
        {
            if (TryLoadLocal(q, artist, title) is { } local) return local;
            if (IsRecentMiss(key)) return new LyricsResult(LyricsStatus.NotFound, null, "cache", null);
        }
        if (!_settings.FetchOnline) return new LyricsResult(LyricsStatus.NotFound, null, "offline", null);

        LrclibTrack? found = null;
        bool failed = false;
        try
        {
            found = await FetchAsync(q, artist, title, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"LRCLIB lookup failed for '{artist} - {title}'", ex);
            failed = true;
        }

        var path = Path.Combine(Folder, FileName(artist, title) + ".lrc");
        if (found is { Instrumental: true })
        {
            Save(path, Header(found, artist, title) + "[instrumental:true]\n");
            return new LyricsResult(LyricsStatus.Instrumental, null, "LRCLIB", path);
        }

        if (found?.Lrc is { Length: > 0 } lrc)
        {
            var text = Header(found, artist, title) + lrc.Replace("\r\n", "\n");
            Save(path, text);
            ForgetMiss(key);
            return new LyricsResult(LyricsStatus.Synced, LrcParser.Parse(text), "LRCLIB", path);
        }

        // Nothing synced on LRCLIB (or LRCLIB is down): NetEase, if the user allows it.
        if (_settings.LyricsFromNetEase && await FromNetEaseAsync(q, artist, title, path, ct) is { } fromNetEase)
        {
            ForgetMiss(key);
            return fromNetEase;
        }

        if (failed) return new LyricsResult(LyricsStatus.Error, null, "LRCLIB", null);
        if (found == null)
        {
            RecordMiss(key);
            return new LyricsResult(LyricsStatus.NotFound, null, "LRCLIB", null);
        }
        var plainPath = Path.ChangeExtension(path, ".txt");
        if (!string.IsNullOrWhiteSpace(found.PlainLyrics)) Save(plainPath, found.PlainLyrics);
        return new LyricsResult(LyricsStatus.PlainOnly, null, "LRCLIB", plainPath);
    }

    /// <summary>
    /// The song on NetEase: the closest match by name, artist and length (not a live or remixed
    /// version unless that is what plays), saved as its .lrc. Null when NetEase has nothing synced.
    /// </summary>
    async Task<LyricsResult?> FromNetEaseAsync(TrackQuery q, string artist, string title, string path, CancellationToken ct)
    {
        try
        {
            var variants = TitleCleaner.Variants(q.Artist, q.Title);
            var songs = (await _netEase.SearchAsync($"{artist} {title}".Trim(), ct)).ToList();
            var song = PickNetEase(songs, variants, q.Title, q.Duration.TotalSeconds);
            if (song == null)
            {
                songs.AddRange(await _netEase.SearchAsync(title, ct));
                song = PickNetEase(songs, variants, q.Title, q.Duration.TotalSeconds);
            }
            if (song == null) return null;
            var lrc = await _netEase.LyricsAsync(song.Id, ct);
            if (lrc == null) return null;
            var text = NetEaseHeader(song) + lrc;
            Save(path, text);
            Log.Info($"Lyrics from NetEase: '{song.Artist} - {song.Name}' ({song.Id})");
            return new LyricsResult(LyricsStatus.Synced, LrcParser.Parse(text), "NetEase", path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"NetEase lookup failed for '{artist} - {title}'", ex);
            return null;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(live|remix|unplugged|acoustic|instrumental|inst\.?|karaoke|cover|sped up|slowed|nightcore|伴奏|现场|翻唱)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Version();

    /// <summary>The NetEase song that is most likely the one playing, scored like LRCLIB's candidates.</summary>
    static NetEaseSong? PickNetEase(List<NetEaseSong> songs, IReadOnlyList<(string Artist, string Title)> variants, string playing, double duration)
    {
        var artists = variants.Select(v => v.Artist).Where(a => a.Length > 0).Distinct().ToList();
        NetEaseSong? best = null;
        double bestScore = double.MinValue;
        foreach (var s in songs)
        {
            double titleSim = variants.Max(v => TitleCleaner.Similarity(s.Name, v.Title));
            if (titleSim < 0.45) continue;
            double? diff = duration > 0 && s.Seconds > 0 ? Math.Abs(s.Seconds - duration) : null;
            // NetEase holds many versions of a song; another length is another recording, and its lyrics would run out of step.
            if (diff > 8) continue;
            // Stricter than LRCLIB: NetEase is full of covers and remixes of the same length, and it
            // writes other spellings of a name in brackets, which the artist match already reads.
            double artistSim = artists.Count == 0 ? 0.5 : artists.Max(a => TitleCleaner.ArtistMatch(s.Artist, a));
            if (artists.Count > 0 && artistSim < TitleCleaner.SameArtistAt) continue;
            // A live, remixed, covered or karaoke version only when that is what plays.
            if (Version().IsMatch(s.Name) && !Version().IsMatch(playing)) continue;
            double score = titleSim * 40 + artistSim * 60;
            if (diff is double gap)
            {
                if (gap <= 2) score += 40;
                else if (gap <= 5) score += 20;
                else if (gap > 20) score -= 80;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        return best;
    }

    static string NetEaseHeader(NetEaseSong s)
    {
        var sb = new StringBuilder();
        sb.Append(NetEaseMarker).Append('\n');
        sb.Append("[ti:").Append(s.Name).Append("]\n");
        sb.Append("[ar:").Append(s.Artist).Append("]\n");
        if (s.Album.Length > 0) sb.Append("[al:").Append(s.Album).Append("]\n");
        if (s.Seconds > 0) sb.Append("[length:").Append(TimeSpan.FromSeconds(s.Seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture)).Append("]\n");
        sb.Append("[id:netease-").Append(s.Id).Append("]\n");
        return sb.ToString();
    }

    /// <summary>
    /// Songs LRCLIB has for words typed by hand (the lyrics panel's search): the playing song's
    /// artist first, so a song of the same name by someone else sinks; then synced lyrics, then
    /// the closest in length to the song playing.
    /// </summary>
    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string text, TrackQuery playing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        // NetEase alongside, when allowed; it failing only means fewer results.
        var netEase = _settings.LyricsFromNetEase ? NetEaseCandidatesAsync(text, ct) : null;
        var found = await GetAsync<List<LrclibTrack>>("api/search" + Query(("q", text.Trim())), ct) ?? [];
        double seconds = playing.Duration.TotalSeconds;
        var artists = TitleCleaner.Variants(playing.Artist, playing.Title).Select(v => v.Artist).Where(a => a.Length > 0).Distinct().ToList();
        bool SameArtist(LyricsCandidate c) => artists.Count > 0 && artists.Max(a => TitleCleaner.ArtistMatch(c.Artist, a)) >= TitleCleaner.SameArtistAt;
        var candidates = found
            .Where(t => t.Instrumental || !string.IsNullOrWhiteSpace(t.SyncedLyrics) || !string.IsNullOrWhiteSpace(t.PlainLyrics))
            .Select(t => new LyricsCandidate(t.TrackName ?? "", t.ArtistName ?? "", t.AlbumName ?? "",
                TimeSpan.FromSeconds(t.Duration ?? 0), !string.IsNullOrWhiteSpace(t.SyncedLyrics), t.Instrumental) { Track = t })
            .ToList();
        if (netEase != null) candidates.AddRange(await netEase);
        return candidates
            .OrderByDescending(SameArtist)
            .ThenByDescending(c => c.Synced)
            .ThenBy(c => c.From == "LRCLIB" ? 0 : 1)
            .ThenBy(c => seconds > 0 && c.Duration > TimeSpan.Zero ? Math.Abs(c.Duration.TotalSeconds - seconds) : 0)
            .Take(30)
            .ToList();
    }

    async Task<List<LyricsCandidate>> NetEaseCandidatesAsync(string text, CancellationToken ct)
    {
        try
        {
            // Whether a NetEase song has synced lyrics is only known once they are fetched: shown as synced, checked when picked.
            // When NetEase distrusts a caller it answers with unrelated songs: only those sharing words with the search stay.
            return (await _netEase.SearchAsync(text, ct))
                .Where(s => TitleCleaner.Similarity($"{s.Name} {s.Artist}", text) >= 0.2)
                .Select(s => new LyricsCandidate(s.Name, s.Artist, s.Album, TimeSpan.FromSeconds(s.Seconds), true, false) { From = "NetEase", NetEaseId = s.Id })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("NetEase search failed", ex);
            return [];
        }
    }

    /// <summary>
    /// Makes a hand-picked search result the lyrics of the song playing: saved as its .lrc file,
    /// so it is used from now on, replacing whatever was found before. Null when the pick turned
    /// out to have no lyrics after all (a NetEase song without synced lyrics).
    /// </summary>
    public async Task<LyricsResult?> UseAsync(LyricsCandidate pick, TrackQuery q, CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        var (artist, title) = TitleCleaner.Clean(q.Artist, q.Title);
        var path = Path.Combine(Folder, FileName(artist, title) + ".lrc");
        var plainPath = Path.ChangeExtension(path, ".txt");
        if (pick.Track is not { } t)
        {
            var lrc = await _netEase.LyricsAsync(pick.NetEaseId, ct);
            if (lrc == null) return null;
            var text = NetEaseHeader(new NetEaseSong(pick.NetEaseId, pick.Title, pick.Artist, pick.Album, pick.Duration.TotalSeconds)) + lrc;
            Save(path, text);
            try { File.Delete(plainPath); } catch (Exception) { }
            ForgetMiss(MissKey(artist, title));
            return new LyricsResult(LyricsStatus.Synced, LrcParser.Parse(text), "NetEase", path);
        }
        ForgetMiss(MissKey(artist, title));
        if (t.Instrumental)
        {
            Save(path, Header(t, artist, title) + "[instrumental:true]\n");
            return new LyricsResult(LyricsStatus.Instrumental, null, "LRCLIB", path);
        }
        if (t.Lrc is { Length: > 0 } synced)
        {
            var text = Header(t, artist, title) + synced.Replace("\r\n", "\n");
            Save(path, text);
            try { File.Delete(plainPath); } catch (Exception) { }
            return new LyricsResult(LyricsStatus.Synced, LrcParser.Parse(text), "LRCLIB", path);
        }
        // Words only: drop a synced file that was the wrong song, or it would win next time.
        try { File.Delete(path); } catch (Exception) { }
        Save(plainPath, t.PlainLyrics ?? "");
        return new LyricsResult(LyricsStatus.PlainOnly, null, "LRCLIB", plainPath);
    }

    /// <summary>Persists a per-song timing correction into the song's .lrc file.</summary>
    public void SaveOffset(LyricsResult result, int offsetMs)
    {
        if (result.Lyrics == null) return;
        result.Lyrics.Offset = TimeSpan.FromMilliseconds(offsetMs);
        if (result.FilePath == null || !File.Exists(result.FilePath)) return;
        try
        {
            var text = File.ReadAllText(result.FilePath);
            File.WriteAllText(result.FilePath, LrcParser.WithOffset(text, offsetMs), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error("Saving lyric offset failed", ex);
        }
    }

    /// <summary>
    /// Where lyrics on disk came from, by the mark MusicBar put at the top of the file: "LRCLIB",
    /// "NetEase", or "file" for one the user added; null for none (demo lyrics, nothing found).
    /// </summary>
    public static string? OriginOf(LyricsResult? result)
    {
        if (result?.FilePath is not { } path || !File.Exists(path)) return null;
        try
        {
            using var reader = new StreamReader(path);
            var head = (reader.ReadLine() ?? "").Trim();
            return head.StartsWith(Marker, StringComparison.Ordinal) ? "LRCLIB"
                : head.StartsWith(NetEaseMarker, StringComparison.Ordinal) ? "NetEase"
                : "file";
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ sharing on LRCLIB

    /// <summary>
    /// The lyrics on screen as something worth sharing on LRCLIB, or null when there is nothing to
    /// add: lyrics from a file of the user's own, or LRCLIB lyrics whose timing they corrected. A
    /// correction keeps the LRCLIB track's own name, album and length, so it becomes a new revision of
    /// that track instead of a duplicate. Lyrics from NetEase are not offered.
    /// </summary>
    public LyricsDraft? DraftFor(LyricsResult? result, TrackQuery q)
    {
        if (result?.Lyrics is not { Lines.Count: > 0 } lyrics || result.FilePath is not { } path || !File.Exists(path)) return null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception)
        {
            return null;
        }
        var head = text.TrimStart();
        bool own = !head.StartsWith(MarkerStart, StringComparison.Ordinal);
        bool fromLrclib = head.StartsWith(Marker, StringComparison.Ordinal);
        int offset = (int)lyrics.Offset.TotalMilliseconds;
        if (!own && !(fromLrclib && offset != 0)) return null;

        var tags = LrcParser.Tags(text);
        var (artist, title) = TitleCleaner.Clean(q.Artist, q.Title);
        string Tag(string key, string fallback) => tags.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;
        var duration = tags.TryGetValue("length", out var length) && ParseLength(length) is { } l ? l : q.Duration;

        // The correction folded into the times: a positive offset showed the lyrics earlier.
        var shift = TimeSpan.FromMilliseconds(offset);
        var synced = new StringBuilder();
        var plain = new StringBuilder();
        int count = 0;
        foreach (var line in lyrics.Lines)
        {
            synced.Append(LrcParser.Stamp(line.Start - shift)).Append(line.Text).Append('\n');
            plain.Append(line.Text).Append('\n');
            if (!line.IsBlank) count++;
        }
        return new LyricsDraft(Tag("ti", title), Tag("ar", artist), Tag("al", q.Album), duration,
            synced.ToString().TrimEnd('\n'), plain.ToString().Trim('\n'), count, offset);
    }

    /// <summary>
    /// A song's timing correction in words: plus is later, as the subtitle delay of a video player.
    /// Inside the file the [offset:] tag keeps LRC's own sense, where plus is earlier.
    /// </summary>
    public static string DescribeTiming(int offsetMs)
    {
        if (offsetMs == 0) return Loc.T("LyrP_InTime");
        string amount = (Math.Abs(offsetMs) / 1000.0).ToString("0.00");
        return Loc.T(offsetMs < 0 ? "LyrP_Late" : "LyrP_Early", amount);
    }

    static TimeSpan? ParseLength(string text)
    {
        var parts = text.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int m) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
            return TimeSpan.FromSeconds(m * 60 + s);
        return null;
    }

    /// <summary>
    /// Why LRCLIB would refuse (or should not get) these lyrics, as a string key, or null when they look right:
    /// a name and artist, a length, a handful of timed lines, none past the end of the song.
    /// </summary>
    public static string? Problem(LyricsDraft d)
    {
        if (d.Title.Trim().Length == 0 || d.Artist.Trim().Length == 0) return "LyrS_NeedName";
        if (d.Duration < TimeSpan.FromSeconds(10)) return "LyrS_NeedLength";
        if (d.Lines < 4) return "LyrS_TooFew";
        var last = LrcParser.Parse(d.Synced).Lines.LastOrDefault();
        if (last != null && last.Start > d.Duration + TimeSpan.FromSeconds(2)) return "LyrS_TooLong";
        return null;
    }

    sealed class Challenge
    {
        public string Prefix { get; set; } = "";
        public string Target { get; set; } = "";
    }

    /// <summary>
    /// Shares lyrics on LRCLIB. LRCLIB keeps spam out with a small proof of work: it hands out a
    /// prefix and a target, and wants a number that, after the prefix, hashes (SHA-256) to no more
    /// than the target; finding one takes a few seconds. Reports its stage: "challenge", "solving", "sending".
    /// </summary>
    public async Task PublishAsync(LyricsDraft d, IProgress<string>? stage, CancellationToken ct)
    {
        stage?.Report("challenge");
        Challenge challenge;
        using (var response = await Client().PostAsync("api/request-challenge", null, ct))
        {
            response.EnsureSuccessStatusCode();
            challenge = JsonSerializer.Deserialize<Challenge>(await response.Content.ReadAsStringAsync(ct), Json)
                ?? throw new HttpRequestException("LRCLIB sent no challenge");
        }

        stage?.Report("solving");
        var nonce = await Task.Run(() => Solve(challenge.Prefix, challenge.Target, ct), ct);

        stage?.Report("sending");
        var body = JsonSerializer.Serialize(new
        {
            trackName = d.Title.Trim(),
            artistName = d.Artist.Trim(),
            // LRCLIB wants an album, which a video often lacks; a single's album is its own name.
            albumName = d.Album.Trim().Length > 0 ? d.Album.Trim() : d.Title.Trim(),
            duration = Math.Round(d.Duration.TotalSeconds),
            plainLyrics = d.Plain,
            syncedLyrics = d.Synced,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/publish") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Publish-Token", $"{challenge.Prefix}:{nonce}");
        using var published = await Client().SendAsync(request, ct);
        if (!published.IsSuccessStatusCode)
        {
            var reason = await published.Content.ReadAsStringAsync(ct);
            Log.Error($"LRCLIB refused the lyrics ({(int)published.StatusCode}): {reason}");
            if ((int)published.StatusCode is >= 400 and < 500 and not 429) throw new RefusedException(ReasonOf(reason));
            throw new HttpRequestException($"LRCLIB refused the lyrics ({(int)published.StatusCode})", null, published.StatusCode);
        }
        Log.Info($"Lyrics shared on LRCLIB: '{d.Artist} - {d.Title}' ({d.Lines} lines, offset {d.OffsetMs} ms folded in)");
    }

    /// <summary>LRCLIB turned the lyrics down and said why (its own words, in English).</summary>
    public sealed class RefusedException(string reason) : Exception(reason)
    {
        public string Reason { get; } = reason;
    }

    /// <summary>The message of an LRCLIB error ({"message": "...", "name": "..."}), or the raw text.</summary>
    static string ReasonOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } message) return message;
        }
        catch (JsonException)
        {
        }
        return body.Length > 160 ? body[..160] + "…" : body;
    }

    /// <summary>The proof of work: the first number (on any core) whose hash after the prefix is at most the target.</summary>
    static string Solve(string prefix, string targetHex, CancellationToken ct)
    {
        var target = Convert.FromHexString(targetHex);
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        long found = -1;
        int workers = Math.Max(1, Environment.ProcessorCount - 1);
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, w =>
        {
            Span<byte> input = stackalloc byte[prefixBytes.Length + 20];
            prefixBytes.CopyTo(input);
            Span<byte> hash = stackalloc byte[32];
            for (long n = w, i = 0; Interlocked.Read(ref found) < 0; n += workers, i++)
            {
                if ((i & 0xFFFF) == 0 && ct.IsCancellationRequested) return;
                n.TryFormat(input[prefixBytes.Length..], out int digits, default, CultureInfo.InvariantCulture);
                SHA256.HashData(input[..(prefixBytes.Length + digits)], hash);
                if (AtMost(hash, target))
                {
                    Interlocked.CompareExchange(ref found, n, -1);
                    return;
                }
            }
        });
        ct.ThrowIfCancellationRequested();
        return found.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The hash is no larger than the target, compared byte by byte from the first (as LRCLIB does).</summary>
    static bool AtMost(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> target)
    {
        for (int i = 0; i < hash.Length && i < target.Length; i++)
        {
            if (hash[i] > target[i]) return false;
            if (hash[i] < target[i]) return true;
        }
        return true;
    }

    public (int Saved, int Misses) GetCacheStats()
    {
        try
        {
            int saved = Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*.lrc").Count() : 0;
            lock (_missLock) return (saved, LoadMisses().Count);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    public void ClearMisses()
    {
        lock (_missLock)
        {
            _misses = new Dictionary<string, DateTime>();
            try { File.Delete(MissFile); } catch (Exception) { }
        }
    }

    /// <summary>Deletes only files this app downloaded; hand-made .lrc files are kept.</summary>
    public int ClearDownloaded()
    {
        int removed = 0;
        if (!Directory.Exists(Folder)) return 0;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.*").Where(f => f.EndsWith(".lrc") || f.EndsWith(".txt")).ToList())
        {
            try
            {
                using (var reader = new StreamReader(file))
                {
                    var head = reader.ReadLine() ?? "";
                    if (!file.EndsWith(".txt") && !head.Contains(MarkerStart)) continue;
                }
                File.Delete(file);
                removed++;
            }
            catch (Exception)
            {
            }
        }
        ClearMisses();
        return removed;
    }

    // ------------------------------------------------------------------ local files

    LyricsResult? TryLoadLocal(TrackQuery q, string artist, string title)
    {
        var names = new[] { FileName(artist, title), FileName(q.Artist, q.Title), FileName("", title) }
            .Distinct().ToList();

        foreach (var name in names)
        {
            var lrc = Path.Combine(Folder, name + ".lrc");
            if (!File.Exists(lrc)) continue;
            try
            {
                var lyrics = LrcParser.Parse(File.ReadAllText(lrc));
                if (lyrics.IsInstrumental) return new LyricsResult(LyricsStatus.Instrumental, null, "local", lrc);
                if (lyrics.Lines.Count > 0) return new LyricsResult(LyricsStatus.Synced, lyrics, "local", lrc);
            }
            catch (Exception ex)
            {
                Log.Error($"Unreadable lyrics file {lrc}", ex);
            }
        }

        foreach (var name in names)
        {
            var txt = Path.Combine(Folder, name + ".txt");
            if (File.Exists(txt)) return new LyricsResult(LyricsStatus.PlainOnly, null, "local", txt);
        }
        return null;
    }

    static void Save(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error($"Writing lyrics file {path} failed", ex);
        }
    }

    static string Header(LrclibTrack t, string artist, string title)
    {
        var sb = new StringBuilder();
        sb.Append(Marker).Append('\n');
        sb.Append("[ti:").Append(t.TrackName ?? title).Append("]\n");
        sb.Append("[ar:").Append(t.ArtistName ?? artist).Append("]\n");
        if (!string.IsNullOrWhiteSpace(t.AlbumName)) sb.Append("[al:").Append(t.AlbumName).Append("]\n");
        if (t.Duration is double d) sb.Append("[length:").Append(TimeSpan.FromSeconds(d).ToString(@"m\:ss", CultureInfo.InvariantCulture)).Append("]\n");
        sb.Append("[id:lrclib-").Append(t.Id).Append("]\n");
        return sb.ToString();
    }

    public static string FileName(string artist, string title)
    {
        var raw = artist.Trim().Length > 0 ? $"{artist.Trim()} - {title.Trim()}" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw) sb.Append(invalid.Contains(ch) ? '_' : ch);
        var name = sb.ToString().Trim().TrimEnd('.');
        return name.Length > 150 ? name[..150] : name;
    }

    // ------------------------------------------------------------------ negative cache

    static string MissKey(string artist, string title) => $"{TitleCleaner.Loose(artist)}|{TitleCleaner.Loose(title)}";

    Dictionary<string, DateTime> LoadMisses()
    {
        if (_misses != null) return _misses;
        try
        {
            _misses = File.Exists(MissFile)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(MissFile)) ?? new()
                : new();
        }
        catch (Exception)
        {
            _misses = new();
        }
        return _misses;
    }

    bool IsRecentMiss(string key)
    {
        lock (_missLock)
            return LoadMisses().TryGetValue(key, out var when) && DateTime.UtcNow - when < MissTtl;
    }

    void RecordMiss(string key)
    {
        lock (_missLock)
        {
            var misses = LoadMisses();
            misses[key] = DateTime.UtcNow;
            foreach (var old in misses.Where(kv => DateTime.UtcNow - kv.Value > MissTtl).Select(kv => kv.Key).ToList())
                misses.Remove(old);
            try { File.WriteAllText(MissFile, JsonSerializer.Serialize(misses)); } catch (Exception) { }
        }
    }

    void ForgetMiss(string key)
    {
        if (key.EndsWith(NetEaseMiss, StringComparison.Ordinal)) key = key[..^NetEaseMiss.Length];
        lock (_missLock)
        {
            var misses = LoadMisses();
            if (misses.Remove(key) | misses.Remove(key + NetEaseMiss))
                try { File.WriteAllText(MissFile, JsonSerializer.Serialize(_misses)); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------ LRCLIB

    HttpClient Client() => Client("https://lrclib.net/");

    /// <summary>One client per site, made again when the proxy setting changes.</summary>
    HttpClient Client(string baseAddress)
    {
        var proxy = _settings.ProxyUrl;
        lock (_http)
        {
            if (proxy != _httpProxy)
            {
                foreach (var old in _http.Values) old.Dispose();
                _http.Clear();
                _httpProxy = proxy;
            }
            if (_http.TryGetValue(baseAddress, out var client)) return client;

            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            if (!string.IsNullOrWhiteSpace(proxy))
            {
                handler.Proxy = new WebProxy(proxy);
                handler.UseProxy = true;
            }
            client = new HttpClient(handler) { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(15) };
            // MusicBar says who it is, to every site.
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicBar/{UpdateService.Current.ToString(3)} (https://github.com/lvt1693/music-bar)");
            _http[baseAddress] = client;
            return client;
        }
    }

    async Task<LrclibTrack?> FetchAsync(TrackQuery q, string artist, string title, CancellationToken ct)
    {
        int duration = (int)Math.Round(q.Duration.TotalSeconds);
        var candidates = new List<LrclibTrack>();
        var variants = TitleCleaner.Variants(q.Artist, q.Title);
        LrclibTrack? Best() => Pick(candidates, variants, duration);
        static bool Good(LrclibTrack? t) => t != null && !string.IsNullOrWhiteSpace(t.SyncedLyrics);

        // 1) Exact signature lookup with what the player reported (it needs an artist).
        if (q.Artist.Trim().Length > 0)
        {
            var exact = await GetAsync<LrclibTrack>("api/get" + Query(
                ("track_name", q.Title), ("artist_name", q.Artist), ("album_name", q.Album),
                ("duration", duration > 0 ? duration.ToString(CultureInfo.InvariantCulture) : null)), ct);
            if (exact != null) candidates.Add(exact);
        }

        // 2) Same with the cleaned-up names.
        if ((title != q.Title || artist != q.Artist) && artist.Length > 0)
        {
            var cleaned = await GetAsync<LrclibTrack>("api/get" + Query(
                ("track_name", title), ("artist_name", artist),
                ("duration", duration > 0 ? duration.ToString(CultureInfo.InvariantCulture) : null)), ct);
            if (cleaned != null) candidates.Add(cleaned);
        }
        if (Good(Best())) return Best();

        // 3) Search by fields, then by free text.
        var byFields = await GetAsync<List<LrclibTrack>>("api/search" + Query(
            ("track_name", title), ("artist_name", artist.Length > 0 ? TitleCleaner.FirstArtist(artist) : null)), ct);
        if (byFields != null) candidates.AddRange(byFields);
        if (Good(Best())) return Best();

        var byText = await GetAsync<List<LrclibTrack>>("api/search" + Query(("q", $"{artist} {title}".Trim())), ct);
        if (byText != null) candidates.AddRange(byText);
        if (Good(Best())) return Best();

        // 4) Shorter forms of a long YouTube title, and the title without the channel's name: LRCLIB
        //    finds nothing for "Song (prod. X) | Album Y", but finds "Song". A few tries at most.
        //    LRCLIB being busy halfway through keeps what was already found.
        foreach (var (a, t) in variants.Skip(1).Take(4))
        {
            try
            {
                var more = await GetAsync<List<LrclibTrack>>("api/search" + Query(("q", $"{a} {t}".Trim())), ct);
                if (more != null) candidates.AddRange(more);
            }
            catch (HttpRequestException) when (candidates.Count > 0)
            {
                break;
            }
            if (Good(Best())) break;
        }
        return Best();
    }

    /// <summary>
    /// Scores what LRCLIB returned against every way of naming the song and keeps the best: a
    /// close name, the right artist, synced lyrics, a matching length.
    /// </summary>
    static LrclibTrack? Pick(List<LrclibTrack> candidates, IReadOnlyList<(string Artist, string Title)> variants, int duration)
    {
        // Everyone the song could be by: the player's artist, the channel, a name read from the title.
        var artists = variants.Select(v => v.Artist).Where(a => a.Length > 0).Distinct().ToList();
        LrclibTrack? best = null;
        double bestScore = double.MinValue;
        foreach (var c in candidates)
        {
            double titleSim = variants.Max(v => TitleCleaner.Similarity(c.TrackName ?? "", v.Title));
            if (titleSim < 0.45) continue;
            double? diff = duration > 0 && c.Duration is double d ? Math.Abs(d - duration) : null;

            // Who sings it counts as much as the name: a song of the same name by someone else is
            // the wrong song. One by another name is only taken at the very same length (the artist
            // written another way or in another script), and below any that matches.
            double artistSim = artists.Count == 0 ? 0.5 : artists.Max(a => TitleCleaner.ArtistMatch(c.ArtistName ?? "", a));
            bool otherArtist = artists.Count > 0 && artistSim < TitleCleaner.SameArtistAt;
            if (otherArtist && diff is not <= 3) continue;
            double score = titleSim * 40 + artistSim * 60 - (otherArtist ? 50 : 0);

            if (!string.IsNullOrWhiteSpace(c.SyncedLyrics)) score += c.HasWordSync ? 110 : 100;
            else if (c.Instrumental) score += 60;
            else if (!string.IsNullOrWhiteSpace(c.PlainLyrics)) score += 10;
            else continue;

            if (diff is double gap)
            {
                if (gap <= 2) score += 40;
                else if (gap <= 5) score += 20;
                else if (gap > 20) score -= 80;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

    async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await Client().GetAsync(url, ct);
                // Not found, or a query LRCLIB won't take (a missing field): nothing there.
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return null;
                // Too many requests: wait as long as LRCLIB asks, if that is short, else try again later.
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var wait = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(5));
                    if (attempt < 2 && wait <= TimeSpan.FromSeconds(12))
                    {
                        await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, ct);
                        continue;
                    }
                    throw new HttpRequestException($"LRCLIB asks to wait {wait.TotalSeconds:0} s", null, HttpStatusCode.TooManyRequests);
                }
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(json, Json);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                // LRCLIB answers "busy" (503) now and then: a little patience usually gets through.
                await Task.Delay(700 * (attempt + 1), ct);
            }
        }
    }

    static string Query(params (string Key, string? Value)[] pairs)
    {
        var parts = pairs.Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}");
        return "?" + string.Join("&", parts);
    }
}
