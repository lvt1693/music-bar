using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>One song's year: how long it played and how many times.</summary>
public sealed class SongTally
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Seconds { get; set; }
    public int Plays { get; set; }
    public DateTime First { get; set; }
}

/// <summary>Everything counted in one year, as saved in stats\{year}.json.</summary>
public sealed class YearTally
{
    public int Year { get; set; }

    /// <summary>By song, keyed by its loose artist and title.</summary>
    public Dictionary<string, SongTally> Songs { get; set; } = new();

    /// <summary>Seconds listened per day ("2026-11-04").</summary>
    public Dictionary<string, double> Days { get; set; } = new();

    /// <summary>Seconds listened in each hour of the day, 0 to 23.</summary>
    public double[] Hours { get; set; } = new double[24];

    /// <summary>Seconds listened per player app, by its name.</summary>
    public Dictionary<string, double> Apps { get; set; } = new();

    public double TotalSeconds => Days.Values.Sum();
}

/// <summary>
/// Counts what is listened to, for the year in music: every few seconds while music plays, the song, its
/// artist, the day, the hour and the app get the time. Only music: <see cref="MusicCheck"/> leaves out pages,
/// videos and launchers; time it can't place yet (a browser tab waiting for the catalogue's answer) is held
/// and counted once it turns out to be a song, or dropped. A song counts as played once it has been heard
/// for half a minute (or half of a short one), again on each repeat. Kept on this computer only, a small
/// JSON file per year.
/// </summary>
public sealed class ListeningStats : IDisposable
{
    static readonly TimeSpan Step = TimeSpan.FromSeconds(5);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    readonly AppSettings _s;
    readonly MediaService _media;
    // Whether the iTunes catalogue knows a song (true, false), or hasn't been asked (null): the song card's cache.
    readonly Func<string, string, bool?> _inCatalogue;
    readonly DispatcherTimer _timer = new() { Interval = Step };
    readonly Dictionary<int, YearTally> _years = new();
    DateTime _lastTick = DateTime.Now;
    DateTime _lastSave = DateTime.Now;
    bool _dirty;
    string _songKey = "";
    double _stint;
    bool _counted;

    // Time held while it isn't known whether this is music, with what it would be counted as.
    readonly List<(DateTime When, double Seconds)> _held = [];
    (string Title, string Artist, string RawArtist, string App, TimeSpan Duration) _heldFor;

    public ListeningStats(AppSettings settings, MediaService media, Func<string, string, bool?>? inCatalogue = null)
    {
        _s = settings;
        _media = media;
        _inCatalogue = inCatalogue ?? ((_, _) => null);
        _timer.Tick += (_, _) => Tick();
        _media.TrackChanged += (_, _) => NewStint();
        _media.Restarted += (_, _) => NewStint();
        _timer.Start();
    }

    public static string Folder => Path.Combine(AppPaths.DataDir, "Stats");

    /// <summary>The year's counts so far (empty when nothing was counted).</summary>
    public YearTally Year(int year)
    {
        if (_years.TryGetValue(year, out var known)) return known;
        var tally = Load(year);
        _years[year] = tally;
        return tally;
    }

    void NewStint()
    {
        SettleHeld();
        _stint = 0;
        _counted = false;
        _songKey = "";
    }

    /// <summary>The song is over and still unplaced: a song-length piece with an artist from the player counts.</summary>
    void SettleHeld()
    {
        if (_held.Count == 0) return;
        if (MusicCheck.CountsWhenUnsure(_heldFor.RawArtist, _heldFor.Duration)) Release();
        _held.Clear();
    }

    void Release()
    {
        var (title, artist, _, app, duration) = _heldFor;
        foreach (var (when, seconds) in _held) Add(when, seconds, title, artist, app, duration);
        _held.Clear();
    }

    void Tick()
    {
        var now = DateTime.Now;
        double seconds = Math.Min((now - _lastTick).TotalSeconds, Step.TotalSeconds * 2);
        _lastTick = now;
        if (_dirty && now - _lastSave > TimeSpan.FromMinutes(1)) Save();
        if (!_s.KeepListeningStats || !_media.HasSession || !_media.IsPlaying || _media.IsDemo || _media.Title.Length == 0) return;

        var (artist, title) = TitleCleaner.Clean(_media.Artist, _media.Title);
        var key = $"{TitleCleaner.Loose(artist)}|{TitleCleaner.Loose(title)}";
        if (key != _songKey)
        {
            SettleHeld();
            _songKey = key;
            _stint = 0;
            _counted = false;
        }
        var app = _media.AppName.Length > 0 ? _media.AppName : "?";
        var verdict = MusicCheck.Judge(_media.AppId, _media.Title, _media.Artist, _media.Album, _media.Duration, _media.Kind,
            _inCatalogue(_media.Artist, _media.Title));
        switch (verdict)
        {
            case ListenVerdict.NotMusic:
                _held.Clear();
                return;
            case ListenVerdict.Unsure:
                // Held (at most a few hours of it) until the catalogue answers or the song ends.
                _heldFor = (title, artist, _media.Artist, app, _media.Duration);
                if (_held.Count < 3000) _held.Add((now, seconds));
                return;
        }
        if (_held.Count > 0) Release();
        Add(now, seconds, title, artist, app, _media.Duration);
    }

    void Add(DateTime when, double seconds, string title, string artist, string app, TimeSpan duration)
    {
        var key = $"{TitleCleaner.Loose(artist)}|{TitleCleaner.Loose(title)}";
        var year = Year(when.Year);
        if (!year.Songs.TryGetValue(key, out var song))
        {
            song = new SongTally { Title = title, Artist = artist, First = when };
            year.Songs[key] = song;
        }
        song.Seconds += seconds;
        _stint += seconds;
        double needed = duration > TimeSpan.Zero ? Math.Min(30, duration.TotalSeconds / 2) : 30;
        if (!_counted && _stint >= needed)
        {
            _counted = true;
            song.Plays++;
        }
        var day = when.ToString("yyyy-MM-dd");
        year.Days[day] = year.Days.GetValueOrDefault(day) + seconds;
        year.Hours[when.Hour] += seconds;
        year.Apps[app] = year.Apps.GetValueOrDefault(app) + seconds;
        _dirty = true;
    }

    static string FileFor(int year) => Path.Combine(Folder, $"{year}.json");

    static YearTally Load(int year)
    {
        try
        {
            var path = FileFor(year);
            if (File.Exists(path) && JsonSerializer.Deserialize<YearTally>(File.ReadAllText(path), Json) is { } tally)
            {
                if (tally.Hours.Length != 24) tally.Hours = new double[24];
                return tally;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Reading listening stats for {year} failed", ex);
        }
        return new YearTally { Year = year };
    }

    public void Save()
    {
        _lastSave = DateTime.Now;
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var tally in _years.Values.Where(y => y.Days.Count > 0))
            {
                var path = FileFor(tally.Year);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(tally, Json));
                File.Move(path + ".tmp", path, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Saving listening stats failed", ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Save();
    }
}
