using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MusicBar.Services;

public sealed record RecapSong(string Title, string Artist, int Plays, double Minutes);

public sealed record RecapArtist(string Name, double Minutes);

/// <summary>A year in music, worked out from what <see cref="ListeningStats"/> counted.</summary>
public sealed class YearRecap
{
    public required int Year { get; init; }
    public required double Minutes { get; init; }
    public required int Plays { get; init; }
    public required int Songs { get; init; }
    public required int Artists { get; init; }
    public required IReadOnlyList<RecapSong> TopSongs { get; init; }
    public required IReadOnlyList<RecapArtist> TopArtists { get; init; }
    public string? TopApp { get; init; }
    public double TopAppShare { get; init; }
    public DateTime? BiggestDay { get; init; }
    public double BiggestDayMinutes { get; init; }
    /// <summary>The hour of the day with the most music (its start, 0 to 23).</summary>
    public int PeakHour { get; init; }
    public int DaysListened { get; init; }
    /// <summary>The most days in a row with some music.</summary>
    public int LongestStreak { get; init; }

    /// <summary>The titles the year earned, strongest first (the card's "you are", then badges); never empty.</summary>
    public required IReadOnlyList<RecapTitle> Titles { get; init; }

    /// <summary>Enough to be worth a recap: an hour of music.</summary>
    public bool Worthwhile => Minutes >= 60;

    /// <param name="culture">How numbers and dates in the titles are written (the UI's language by default).</param>
    public static YearRecap From(YearTally t, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo(MusicBar.Infrastructure.Loc.Instance.Language == MusicBar.Models.AppLanguage.Vietnamese ? "vi-VN" : "en-US");
        // What was counted before the stats only took music (a "Facebook" tab, a video file): left out here, its
        // time taken off the year's minutes too.
        var notSongs = t.Songs.Values.Where(s => !MusicCheck.LooksLikeSongEntry(s.Title, s.Artist)).ToList();
        double notSongSeconds = notSongs.Sum(s => s.Seconds);
        var songs = t.Songs.Values.Where(s => s.Seconds >= 20 && MusicCheck.LooksLikeSongEntry(s.Title, s.Artist)).ToList();
        int plays = songs.Sum(s => s.Plays);
        var topSongs = songs.OrderByDescending(s => s.Plays).ThenByDescending(s => s.Seconds).Take(5)
            .Select(s => new RecapSong(s.Title, s.Artist, s.Plays, s.Seconds / 60)).ToList();

        // Every credited artist gets the song's time; the spelling heard first is the one shown.
        var artists = new Dictionary<string, (string Name, double Seconds)>();
        foreach (var s in songs.OrderBy(s => s.First))
        {
            foreach (var name in TitleCleaner.Artists(s.Artist))
            {
                var key = TitleCleaner.Loose(name);
                artists[key] = artists.TryGetValue(key, out var a) ? (a.Name, a.Seconds + s.Seconds) : (name, s.Seconds);
            }
        }
        var topArtists = artists.Values.OrderByDescending(a => a.Seconds).Take(5).Select(a => new RecapArtist(a.Name, a.Seconds / 60)).ToList();

        double total = Math.Max(0, t.TotalSeconds - notSongSeconds);
        var app = t.Apps.OrderByDescending(a => a.Value).FirstOrDefault();
        var days = t.Days.Where(d => d.Value >= 60)
            .Select(d => (Day: DateTime.TryParseExact(d.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : (DateTime?)null, Seconds: d.Value))
            .Where(d => d.Day != null).Select(d => (Day: d.Day!.Value, d.Seconds)).OrderBy(d => d.Day).ToList();
        var biggest = days.Count > 0 ? days.MaxBy(d => d.Seconds) : default;

        int streak = 0, run = 0;
        DateTime? previous = null;
        foreach (var (day, _) in days)
        {
            run = previous is { } p && (day - p).TotalDays == 1 ? run + 1 : 1;
            streak = Math.Max(streak, run);
            previous = day;
        }

        int peak = Enumerable.Range(0, 24).MaxBy(h => t.Hours[h]);
        var titles = RecapTitles.For(t, songs, total, plays, topArtists, artists.Count, app.Key,
            days.Count > 0 ? biggest.Day : null, biggest.Seconds / 60, days.Count, streak, culture);
        return new YearRecap
        {
            Year = t.Year,
            Minutes = total / 60,
            Plays = plays,
            Songs = songs.Count,
            Artists = artists.Count,
            TopSongs = topSongs,
            TopArtists = topArtists,
            TopApp = app.Key,
            TopAppShare = total > 0 ? app.Value / total : 0,
            BiggestDay = days.Count > 0 ? biggest.Day : null,
            BiggestDayMinutes = biggest.Seconds / 60,
            PeakHour = peak,
            DaysListened = days.Count,
            LongestStreak = streak,
            Titles = titles,
        };
    }

    /// <summary>Which year's recap the end of the year offers: this one until New Year, last year's in the first week of January.</summary>
    public static int SeasonYear(DateTime now) => now.Month == 1 ? now.Year - 1 : now.Year;

    /// <summary>The recap season: from Christmas Eve to the 7th of January.</summary>
    public static bool InSeason(DateTime now)
        => (now.Month == 12 && now.Day >= 24) || (now.Month == 1 && now.Day <= 7);
}
