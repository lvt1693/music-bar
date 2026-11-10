using System;
using System.Text.RegularExpressions;

namespace MusicBar.Services;

/// <summary>
/// Whether what a media session is called is worth looking up as a song. Windows hands over whatever
/// plays: a browser tab's title ("Phan Bá Duy messaged you - Chat", "Daily Meeting — Pika"), a call, a
/// game launcher. Looking those up sends their names to lyric and catalogue sites for nothing, so
/// they are told apart here, from the title, the artist, the length and the app. When it cannot be
/// told, the answer is yes: a missed song costs more than one wasted request. Pure on purpose, so it
/// can be checked on its own.
/// </summary>
public static partial class SongGuess
{
    // A shorter piece is a jingle, an advertisement or a notification sound, not a song.
    static readonly TimeSpan ShortestSong = TimeSpan.FromSeconds(40);

    // A messenger's or a launcher's own player (a song shared in Telegram, music in a game launcher) has
    // tags and a real length; below that it is a chat notification or a call.
    static readonly TimeSpan ShortestShared = TimeSpan.FromSeconds(90);

    // Meeting and call apps: what they play is the meeting's name, never music. Matched inside the lower-cased
    // app id ("Slack.exe", "Skype", "Microsoft.SkypeApp_kzf8qxf38zg5c!App").
    static readonly string[] MeetingApps =
        ["slack", "skype", "webex", "gotomeeting", "bluejeans", "lync", "facetime", "meet.google", "dingtalk", "feishu", "mattermost"];

    // Short names, matched as whole words of the id so that a player whose id merely contains them is not
    // taken for them ("MSTeams_8wekyb3d8bbwe!MSTeams", "ms-teams.exe", "Zoom.exe").
    static readonly string[] MeetingWords = ["zoom", "teams", "msteams"];

    // Messengers and game launchers: usually chat, but their built-in players do publish real tracks.
    static readonly string[] ChatApps =
    [
        "messenger", "zalo", "telegram", "whatsapp", "viber", "wechat", "weixin", "kakaotalk", "discord",
        "riotclient", "riotgames", "epicgames",
    ];

    // This mirrors MediaService.Browsers (the list MediaService uses to tell browsers: one session that
    // follows whatever tab plays last) and must be kept in step with it by hand; MediaService's is private.
    // Chrome's own id is just "Chrome"; Firefox's is a hash of its install folder.
    static readonly string[] Browsers =
        ["chrome", "msedge", "firefox", "308046b0af4a39cb", "opera", "brave", "vivaldi", "coccoc", "thorium", "yandex", "floorp", "librewolf"];

    // A notification or a call shown as the tab's title.
    [GeneratedRegex(@"\b(messaged you|mentioned you|is calling|incoming call|missed call|wants to join)\b|nhắn tin cho bạn|đã nhắn tin|cuộc gọi (đến|nhỡ)", RegexOptions.IgnoreCase)]
    private static partial Regex ChatPhrase();

    // A meeting's name, or Google Meet's own tab ("Meet - abc-defg-hij").
    [GeneratedRegex(@"\b(meeting|standup|huddle|webinar|retrospective)\b|cuộc họp|buổi họp|họp (nhóm|team|giao ban)|^meet\s*[-–—|:]", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingTitle();

    // A preview page of some tool ("Xem trước — Taro tập bay — Superhero Powers (1:1)").
    [GeneratedRegex(@"^(xem trước|preview)\s*[-–—:|]", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewTitle();

    // A tab named for a site after a pipe ("... | Facebook"), which no "Artist - Song" title uses.
    [GeneratedRegex(@"\s[|·•]\s(facebook|messenger|zalo|gmail|inbox|outlook|google (meet|chat|docs|sheets|slides|drive|calendar)|meet|microsoft teams|teams|zoom|slack|discord|whatsapp|telegram|linkedin|instagram|twitter|tiktok|reddit|github|jira|confluence|notion|figma|canva|chat)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PipeSiteTitle();

    // After a dash it could be "Artist - Song", so only names that are not song titles count; and the two
    // pages the owner's log shows alone, since a tab called just that is the site itself.
    [GeneratedRegex(@"(^|\s[-–—]\s)(facebook|gmail)\s*$|\s[-–—]\s(messenger|zalo|inbox|outlook|google (meet|chat|docs|sheets|slides|drive|calendar)|microsoft teams|whatsapp|linkedin|github|jira|confluence|figma|canva|chat)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DashSiteTitle();

    // An address as a title, which is what a tab without a name is called.
    [GeneratedRegex(@"^(https?://|www\.)|^localhost(:\d+)?\b|^[\w-]+(\.[\w-]+)*\.(com|net|org|io|vn|dev|app)(/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressTitle();

    // A URL slug or a project name: lower case, no spaces, with a letter in it and three or more parts
    // ("daily-standup-notes") or two with a digit or underscore ("sprint_12"); "bad-idea" is a song.
    [GeneratedRegex(@"^(?=.*[a-z])([a-z0-9]+([-_][a-z0-9]+){2,}|(?=.*[0-9_])[a-z0-9]+([-_][a-z0-9]+)+)$")]
    private static partial Regex Slug();

    // Windows shows the leading "(3)" of a web app's unread count in the title; it says nothing about the rest.
    [GeneratedRegex(@"^\(\d+\)\s*")]
    private static partial Regex UnreadCount();

    /// <summary>
    /// True when the track may be a song and is worth a lyric and catalogue lookup. <paramref name="appId"/>
    /// may be empty when the caller does not know it; the page checks then apply as if the app were a browser.
    /// </summary>
    public static bool IsLikelySong(string title, string artist, TimeSpan duration, string appId)
    {
        title = (title ?? "").Trim();
        artist = (artist ?? "").Trim();
        var id = (appId ?? "").ToLowerInvariant();

        if (title.Length == 0) return false;
        // With an artist a short length alone proves nothing: a player's timeline can lag the title (a video's
        // ad before it) and the lookup is not asked again.
        if (artist.Length == 0 && duration > TimeSpan.Zero && duration < ShortestSong) return false;

        foreach (var app in MeetingApps)
            if (id.Contains(app, StringComparison.Ordinal)) return false;
        if (HasWord(id, MeetingWords)) return false;

        foreach (var app in ChatApps)
            if (id.Contains(app, StringComparison.Ordinal))
                return artist.Length > 0 && duration >= ShortestShared;

        // Spotify's advertisements: the artist is Spotify and the title says what they are.
        if (id.Contains("spotify", StringComparison.Ordinal)
            && (title.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) || title.Equals("Spotify", StringComparison.OrdinalIgnoreCase)))
            return false;

        // A browser names its session after the tab when the page gives no artist; YouTube and the music
        // sites do give one (the channel), so a title like "Artist - Song" with no artist still passes.
        if (artist.Length == 0 && (id.Length == 0 || IsBrowser(id)) && LooksLikePage(title)) return false;
        return true;
    }

    static bool HasWord(string id, string[] words)
    {
        foreach (var part in id.Split(['.', '_', '-', '!', ' ', '\\', '/', ':'], StringSplitOptions.RemoveEmptyEntries))
            if (Array.IndexOf(words, part) >= 0) return true;
        return false;
    }

    static bool IsBrowser(string id)
    {
        foreach (var name in Browsers)
            if (id.Contains(name, StringComparison.Ordinal)) return true;
        return false;
    }

    static bool LooksLikePage(string title)
    {
        var t = UnreadCount().Replace(title, "");
        return t.Length == 0 || ChatPhrase().IsMatch(t) || MeetingTitle().IsMatch(t) || PreviewTitle().IsMatch(t)
            || PipeSiteTitle().IsMatch(t) || DashSiteTitle().IsMatch(t) || AddressTitle().IsMatch(t) || Slug().IsMatch(t);
    }
}
