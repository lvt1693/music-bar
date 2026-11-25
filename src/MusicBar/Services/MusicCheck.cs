using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBar.Services;

/// <summary>What a player says it is playing (Windows' MediaPlaybackType).</summary>
public enum MediaKind { Unknown, Music, Video, Image }

/// <summary>Whether something played counts as music for the year in music.</summary>
public enum ListenVerdict { Music, NotMusic, Unsure }

/// <summary>
/// Whether what plays is music, for the listening stats and the year's recap. Windows hands over every media session:
/// a browser tab ("TikTok - Make Your Day", "Facebook"), a video in Media Player, a game launcher. A tab with no song
/// details is named after the page, and a page title like "TikTok - Make Your Day" reads as "Artist - Song", so the
/// title alone can't tell. What tells: a music player, an artist the player itself gave (pages have none), an album,
/// Video as the kind, and the iTunes catalogue (the song card's lookup) knowing the song. Stricter than
/// <see cref="SongGuess"/>, which errs toward looking a song up; a recap errs toward leaving a page out.
/// Pure on purpose, so it can be checked on its own.
/// </summary>
public static partial class MusicCheck
{
    // Music players and music services, matched inside the lower-cased app id. Media Player (ZuneMusic) plays videos
    // too: those say Video and are left out before this list is asked.
    static readonly string[] MusicApps =
    [
        "spotify", "applemusic", "itunes", "foobar", "musicbee", "aimp", "winamp", "tidal", "deezer", "amazonmusic",
        "amazon music", "qobuz", "soundcloud", "youtube-music", "youtube music", "youtubemusic", "zingmp3", "zing mp3",
        "nhaccuatui", "cider", "plexamp", "mediamonkey", "dopamine", "zunemusic", "audacious", "clementine",
        "strawberry", "musicolet", "resonance", "harmonoid",
    ];

    // Sites and apps that end up as the "artist" when a page title is split ("TikTok - Make Your Day", "Xem trước —
    // ...", "Messenger | Facebook"), or that a player names itself as (an advertisement).
    static readonly string[] NotArtists =
    [
        "facebook", "messenger", "tiktok", "instagram", "youtube", "twitter", "x", "threads", "zalo", "netflix", "gmail",
        "google", "chrome", "discord", "reddit", "twitch", "linkedin", "shopee", "lazada", "spotify", "riot client",
        "xem trước", "preview", "advertisement", "quảng cáo",
    ];

    // A page of a music site with no song details in it ("Lạc Trôi - Sơn Tùng M-TP | Zing MP3"): worth checking in
    // the catalogue rather than leaving out.
    [GeneratedRegex(@"\s[|\-–—·]\s*(zing ?mp3|nhaccuatui|soundcloud|bandcamp|apple music|spotify|deezer|tidal)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MusicSitePage();

    // A file or project name: no spaces, with dashes or underscores ("musicbar-promo-v2-draft", "pika_usp_16x9").
    [GeneratedRegex(@"^[\p{L}\p{N}]+([-_.][\p{L}\p{N}]+)+$")]
    private static partial Regex FileName();

    /// <summary>
    /// Music, not music, or not yet known (the catalogue hasn't answered). <paramref name="rawArtist"/> is the
    /// artist as the player gave it, before a title is split into one; <paramref name="inCatalogue"/> is true when the
    /// iTunes catalogue found the song, false when it looked and didn't, null when it hasn't looked (yet, or at all).
    /// </summary>
    public static ListenVerdict Judge(string appId, string title, string rawArtist, string album, TimeSpan duration,
                                      MediaKind kind, bool? inCatalogue)
    {
        title = (title ?? "").Trim();
        rawArtist = (rawArtist ?? "").Trim();
        album = (album ?? "").Trim();
        var id = (appId ?? "").ToLowerInvariant();

        if (title.Length == 0) return ListenVerdict.NotMusic;
        // Meetings, calls, chat notifications, page titles, jingles: never a song.
        if (!SongGuess.IsLikelySong(title, rawArtist, duration, appId ?? "")) return ListenVerdict.NotMusic;

        bool musicApp = MusicApps.Any(a => id.Contains(a, StringComparison.Ordinal));
        if (kind is MediaKind.Video or MediaKind.Image && !(musicApp && rawArtist.Length > 0)) return ListenVerdict.NotMusic;
        if (IsNotArtist(rawArtist)) return ListenVerdict.NotMusic;
        var (cleanArtist, _) = TitleCleaner.Clean(rawArtist, title);
        if (IsNotArtist(cleanArtist)) return ListenVerdict.NotMusic;

        if (rawArtist.Length == 0)
        {
            // No artist from the player: a page (a browser names the session after the tab), a video file, a launcher.
            // Only an untagged file in a music player, or a music site's page, may still be a song: the catalogue says.
            if (FileName().IsMatch(title)) return ListenVerdict.NotMusic;
            if (!musicApp && !MusicSitePage().IsMatch(title)) return ListenVerdict.NotMusic;
            return inCatalogue switch { true => ListenVerdict.Music, false => ListenVerdict.NotMusic, _ => ListenVerdict.Unsure };
        }

        // An artist the player gave: a music player is enough, so is an album (YouTube Music, Spotify's and Apple's web
        // players give one; YouTube doesn't); otherwise (a YouTube video by a channel, a video player) the catalogue
        // has to know the song.
        if (musicApp || album.Length > 0) return ListenVerdict.Music;
        return inCatalogue switch { true => ListenVerdict.Music, false => ListenVerdict.NotMusic, _ => ListenVerdict.Unsure };
    }

    /// <summary>
    /// What to do with time still unsure when the song ends (no catalogue answer: offline, lookups off, a network
    /// error): a song-length piece with an artist from the player counts, anything else is left out.
    /// </summary>
    public static bool CountsWhenUnsure(string rawArtist, TimeSpan duration)
        => (rawArtist ?? "").Trim().Length > 0
           && (duration <= TimeSpan.Zero || (duration >= TimeSpan.FromSeconds(60) && duration <= TimeSpan.FromMinutes(15)));

    /// <summary>
    /// For a song already in the stats (counted before this check, by title and artist only): false for what was
    /// plainly not a song: no artist (a page, a file, a launcher) or a site or app as the artist. A title with dashes
    /// is fine once there is an artist ("B-I-N-G-O", "Peek-a-Zoo").
    /// </summary>
    public static bool LooksLikeSongEntry(string title, string artist)
    {
        title = (title ?? "").Trim();
        artist = (artist ?? "").Trim();
        if (title.Length == 0 || artist.Length == 0) return false;
        return !IsNotArtist(artist);
    }

    static bool IsNotArtist(string artist)
    {
        var a = Regex.Replace((artist ?? "").Trim().ToLowerInvariant(), @"^\(\d+\)\s*", "");
        return a.Length > 0 && Array.IndexOf(NotArtists, a) >= 0;
    }
}
