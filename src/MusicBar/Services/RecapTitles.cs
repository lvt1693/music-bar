using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicBar.Services;

/// <summary>
/// One title the year earned: which one (its strings are RecT_{Key}_{Variant}, RecL_{Key}_{Variant} and
/// RecBadge_{Key}), how strongly it fits (the strongest is the card's "you are"), its group (one title per group),
/// and what goes in its {0}-style holes.
/// </summary>
public sealed record RecapTitle(string Key, int Variant, double Score, string Group, object[] Args);

/// <summary>
/// The year's titles, worked out from what was listened to: when (night owl, early bird, weekends), how
/// (on repeat, one artist, a hundred artists), how much (streaks, marathons, hours) and what (kids' songs,
/// League of Legends, K-pop, Vietnamese indie or rap, bolero, sad songs, remixes, sped-up, lo-fi, Christmas,
/// anime, soundtracks, acoustic), the "what" from the artists' names and the songs' titles. Each title has a
/// score; every group keeps its strongest; the card shows the best as "you are" and up to three more as badges.
/// The variant of a title's words follows the year, so the card stays the same all year and changes the next.
/// Pure on purpose, so it can be checked on its own.
/// </summary>
public static partial class RecapTitles
{
    /// <summary>How many wordings each title has (RecT_{Key}_0 .. n-1).</summary>
    public static readonly IReadOnlyDictionary<string, int> Variants = new Dictionary<string, int>
    {
        ["NightOwl"] = 2, ["Vampire"] = 2, ["EarlyBird"] = 2, ["NineToFive"] = 2, ["LunchBreak"] = 1, ["Weekend"] = 2,
        ["MondayBlues"] = 1, ["RentFree"] = 2, ["Replay"] = 2, ["Stan"] = 2, ["Fandom"] = 2, ["Explorer"] = 2,
        ["Buffet"] = 2, ["TinyLoop"] = 1, ["NoDaysOff"] = 2, ["EveryDay"] = 1, ["Marathon"] = 2, ["Headphones"] = 2,
        ["Sipper"] = 1, ["Kids"] = 2, ["Summoner"] = 2, ["Kpop"] = 2, ["VIndie"] = 2, ["VRap"] = 2, ["Bolero"] = 2,
        ["Sad"] = 2, ["Remix"] = 2, ["SpedUp"] = 2, ["Lofi"] = 2, ["Christmas"] = 1, ["Anime"] = 1, ["Ost"] = 1,
        ["Acoustic"] = 1, ["AllRounder"] = 2,
    };

    // Artists by their loose names (TitleCleaner.Loose: lower case, no accents, letters and digits only).
    static readonly Dictionary<string, string> Fandoms = new()
    {
        ["taylorswift"] = "Swiftie", ["bts"] = "ARMY", ["blackpink"] = "BLINK", ["justinbieber"] = "Belieber",
        ["arianagrande"] = "Arianator", ["ladygaga"] = "Little Monster", ["beyonce"] = "Beyhive", ["nickiminaj"] = "Barb",
        ["onedirection"] = "Directioner", ["demilovato"] = "Lovatic", ["seventeen"] = "CARAT", ["twice"] = "ONCE",
        ["straykids"] = "STAY", ["aespa"] = "MY", ["newjeans"] = "Bunnies", ["sontungmtp"] = "Sky", ["mytam"] = "Mỹ Nhân",
        ["harrystyles"] = "Harrie", ["edsheeran"] = "Sheerio", ["bigbang"] = "VIP", ["exo"] = "EXO-L", ["ive"] = "DIVE",
        ["lesserafim"] = "FEARNOT", ["oliviarodrigo"] = "Livie",
    };

    static readonly HashSet<string> Kpop =
    [
        "bts", "blackpink", "twice", "newjeans", "straykids", "seventeen", "aespa", "ive", "lesserafim", "gidle", "itzy",
        "tomorrowxtogether", "txt", "enhypen", "exo", "bigbang", "redvelvet", "nct", "nct127", "nctdream", "iu", "jennie",
        "lisa", "rose", "jisoo", "jungkook", "jimin", "taeyeon", "babymonster", "illit", "kissoflife", "zerobaseone",
        "ateez", "mamamoo", "riize", "boynextdoor", "nmixx", "fiftyfifty", "kep1er", "treasure", "monstax", "shinee",
    ];

    static readonly HashSet<string> League = ["kda", "truedamage", "pentakill", "heartsteel", "leagueoflegends", "riotgames", "riotgamesmusic"];

    static readonly HashSet<string> VIndie =
    [
        "vu", "thaidinh", "chillies", "dalab", "madihu", "ngot", "cahoihoang", "hoangdung", "kaidinh", "lecattrongly",
        "vuthanhvan", "phungkhanhlinh", "thinhsuy", "thecassette", "mo", "trangdoan", "buitruonglinh", "ngocdai",
    ];

    static readonly HashSet<string> VRap =
    [
        "den", "denvau", "binz", "rhymastic", "wxrdie", "lowg", "mck", "rptmck", "tlinh", "hieuthuhai", "obito", "karik",
        "suboi", "andree", "andreerighthand", "bray", "seachains", "langld", "robber", "16typh", "masonnguyen", "dangrangto",
        "manbo", "justatee", "gonzo", "blacka", "phaos",
    ];

    static readonly HashSet<string> Bolero =
    [
        "quangle", "nhuquynh", "lequyen", "dannguyen", "phinhung", "manhquynh", "truongvu", "tuanvu", "camly", "hoailam",
        "quanglap", "thienquang", "chelinh", "ngocson", "duongngocthai",
    ];

    static readonly HashSet<string> Japan =
    [
        "yoasobi", "lisa", "aimer", "kenshiyonezu", "radwimps", "officialhigedandism", "vaundy", "ado", "kinggnu",
        "mrsgreenapple", "eve", "zutomayo", "fujiikaze", "lisaarisa", "milet", "reona",
    ];

    // Kids' channels and nursery-rhyme makers, matched inside the loose artist.
    static readonly string[] KidsArtistParts =
        ["pinkfong", "cocomelon", "supersimplesongs", "littlebabybum", "babybus", "kidsong", "nursery", "rhymes", "toddler",
         "heybear", "mothergoose", "bebefinn", "chuchutv", "looloo", "kingenglishkids", "littleangel", "babyshark"];

    [GeneratedRegex(@"baby shark|nursery|wheels on the bus|twinkle twinkle|old macdonald|abc song|bingo|\bthiếu nhi\b|cả nhà thương nhau|con cò bé bé|chú ếch con", RegexOptions.IgnoreCase)]
    private static partial Regex KidsTitle();

    [GeneratedRegex(@"(?<!\p{L})(buồn|nhớ|cô đơn|tan nát|chia tay|đau|khóc|lỡ|xa nhau|không em|vắng em|mất em|tổn thương|nước mắt|tiếc nuối|quên|người cũ|một mình|sad|cry|crying|cried|lonely|alone|miss you|goodbye|broken|heartbreak|heartbroken|tears|without you|hurt)(?!\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex SadTitle();

    [GeneratedRegex(@"remix|vinahouse|\bedm\b|bass boosted|nonstop|mashup|bootleg|\bhouse lak\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemixTitle();

    [GeneratedRegex(@"sped ?up|speed ?up|nightcore|slowed|\+ ?reverb", RegexOptions.IgnoreCase)]
    private static partial Regex SpedUpTitle();

    [GeneratedRegex(@"lo-?fi|lo fi|chillhop|chill beats|study beats", RegexOptions.IgnoreCase)]
    private static partial Regex LofiTitle();

    [GeneratedRegex(@"christmas|giáng sinh|noel|jingle|santa|xmas|last christmas|feliz navidad", RegexOptions.IgnoreCase)]
    private static partial Regex ChristmasTitle();

    [GeneratedRegex(@"\bost\b|soundtrack|nhạc phim|\(from |\bfrom the motion picture\b|\btheme\b", RegexOptions.IgnoreCase)]
    private static partial Regex OstTitle();

    [GeneratedRegex(@"\banime\b|\bopening\b|\bending\b|\bop\s?\d|\bed\s?\d", RegexOptions.IgnoreCase)]
    private static partial Regex AnimeTitle();

    [GeneratedRegex(@"acoustic|\bcover\b|unplugged|\bmộc\b|[\(\[\-–].*\blive\b", RegexOptions.IgnoreCase)]
    private static partial Regex AcousticTitle();

    [GeneratedRegex(@"\bbolero\b", RegexOptions.IgnoreCase)]
    private static partial Regex BoleroTitle();

    [GeneratedRegex(@"league of legends|\barcane\b|\bworlds\b.*\b20\d\d\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeagueTitle();

    /// <summary>The year's titles, strongest first, one per group; at least one (the all-rounder).</summary>
    public static IReadOnlyList<RecapTitle> For(YearTally t, IReadOnlyList<SongTally> songs, double totalSeconds, int plays,
                                                IReadOnlyList<RecapArtist> topArtists, int artistCount, string? topApp,
                                                DateTime? biggestDay, double biggestDayMinutes, int daysListened, int streak,
                                                CultureInfo culture)
    {
        var found = new List<RecapTitle>();
        int year = t.Year;
        void Add(string key, double score, string group, params object[] args)
        {
            if (score <= 0) return;
            int n = Variants.TryGetValue(key, out var v) ? v : 1;
            found.Add(new RecapTitle(key, StableHash(key, year) % n, score, group, args));
        }
        string Pct(double share) => Math.Round(share * 100).ToString("0", culture);
        string Num(double value) => Math.Round(value).ToString("N0", culture);

        double total = Math.Max(totalSeconds, 1);
        double Share(params int[] hours) => hours.Sum(h => t.Hours[h]) / total;

        // ------------------------------------------------------------------ when
        double night = Share(22, 23, 0, 1, 2, 3), late = Share(1, 2, 3, 4), early = Share(5, 6, 7, 8);
        double office = Share(9, 10, 11, 12, 13, 14, 15, 16, 17), lunch = Share(11, 12, 13);
        if (late >= 0.10) Add("Vampire", 0.62 + (late - 0.10) * 2, "when", Num(late * total / 3600));
        if (night >= 0.30) Add("NightOwl", 0.48 + (night - 0.30) * 1.5, "when", Pct(night));
        if (early >= 0.22) Add("EarlyBird", 0.52 + (early - 0.22) * 1.5, "when", Pct(early));
        if (office >= 0.60) Add("NineToFive", 0.42 + (office - 0.60) * 1.2, "when", topApp ?? "Spotify");
        if (lunch >= 0.30) Add("LunchBreak", 0.44 + (lunch - 0.30), "when");
        double weekend = 0, monday = 0;
        foreach (var (key, seconds) in t.Days)
        {
            if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) weekend += seconds;
            if (day.DayOfWeek == DayOfWeek.Monday) monday += seconds;
        }
        weekend /= total;
        monday /= total;
        if (t.Days.Count >= 14)
        {
            if (weekend >= 0.45) Add("Weekend", 0.46 + (weekend - 0.45) * 1.5, "when", Pct(weekend));
            if (monday >= 0.24) Add("MondayBlues", 0.42 + (monday - 0.24), "when", Pct(monday));
        }

        // ------------------------------------------------------------------ how: one song, one artist
        var top = songs.OrderByDescending(s => s.Plays).FirstOrDefault();
        if (top != null && plays >= 20)
        {
            if (top.Plays >= 100) Add("Replay", 0.86 + Math.Min(top.Plays / 2000.0, 0.1), "loyal", top.Title, Num(top.Plays));
            else if (top.Plays >= 25 && top.Plays >= plays * 0.06) Add("RentFree", 0.52 + Math.Min(top.Plays / 250.0, 0.3), "loyal", top.Title, Num(top.Plays));
        }
        if (topArtists.Count > 0)
        {
            var a = topArtists[0];
            double share = a.Minutes * 60 / total;
            if (Fandoms.TryGetValue(TitleCleaner.Loose(a.Name), out var fandom) && share >= 0.12)
                Add("Fandom", 0.64 + (share - 0.12) * 1.5, "loyal", fandom, a.Name, Pct(share));
            else if (share >= 0.20)
                Add("Stan", 0.48 + (share - 0.20) * 1.5, "loyal", a.Name, Pct(share));
        }

        // ------------------------------------------------------------------ how wide
        if (songs.Count >= 80 && songs.Count >= plays * 0.55) Add("Explorer", 0.50, "wide", Num(songs.Count));
        if (artistCount >= 100) Add("Buffet", 0.40 + Math.Min(artistCount / 1500.0, 0.3), "wide", Num(artistCount));
        if (songs.Count <= 25 && plays >= 60) Add("TinyLoop", 0.55, "wide", Num(songs.Count));

        // ------------------------------------------------------------------ how much
        double hours = total / 3600;
        if (streak >= 21) Add("NoDaysOff", 0.46 + Math.Min((streak - 21) / 120.0, 0.4), "much", Num(streak));
        if (daysListened >= 200) Add("EveryDay", 0.44 + Math.Min((daysListened - 200) / 600.0, 0.2), "much", Num(daysListened));
        if (biggestDay is { } big && biggestDayMinutes >= 300)
            Add("Marathon", 0.40 + Math.Min((biggestDayMinutes - 300) / 600.0, 0.3), "much",
                big.ToString(culture.Name.StartsWith("vi", StringComparison.Ordinal) ? "dd/MM" : "MMM d", culture),
                (biggestDayMinutes / 60).ToString("0.#", culture));
        if (hours >= 500) Add("Headphones", 0.50 + Math.Min((hours - 500) / 3000.0, 0.3), "much", Num(hours), Num(hours / 24));
        else if (hours < 20) Add("Sipper", 0.25, "much", Num(Math.Max(hours, 1)));

        // ------------------------------------------------------------------ what (by the time each kind took)
        double kids = 0, league = 0, kpop = 0, vindie = 0, vrap = 0, bolero = 0, sad = 0, remix = 0, sped = 0, lofi = 0;
        double xmas = 0, anime = 0, ost = 0, acoustic = 0;
        SongTally? topKids = null, topLeague = null, topIndie = null, topRap = null;
        var byTime = songs.OrderByDescending(s => s.Seconds).ToList();
        foreach (var s in byTime)
        {
            var names = TitleCleaner.Artists(s.Artist).Select(TitleCleaner.Loose).ToList();
            var text = $"{s.Title} {s.Artist}";
            bool Has(HashSet<string> set) => names.Any(set.Contains);
            if (names.Any(n => KidsArtistParts.Any(p => n.Contains(p, StringComparison.Ordinal))) || KidsTitle().IsMatch(s.Title))
            {
                kids += s.Seconds;
                topKids ??= s;
            }
            if (Has(League) || LeagueTitle().IsMatch(text)) { league += s.Seconds; topLeague ??= s; }
            if (Has(Kpop)) kpop += s.Seconds;
            if (Has(VIndie)) { vindie += s.Seconds; topIndie ??= s; }
            if (Has(VRap)) { vrap += s.Seconds; topRap ??= s; }
            if (Has(Bolero) || BoleroTitle().IsMatch(text)) bolero += s.Seconds;
            if (Has(Japan) || AnimeTitle().IsMatch(s.Title)) anime += s.Seconds;
            if (SadTitle().IsMatch(s.Title)) sad += s.Seconds;
            if (RemixTitle().IsMatch(s.Title)) remix += s.Seconds;
            if (SpedUpTitle().IsMatch(s.Title)) sped += s.Seconds;
            if (LofiTitle().IsMatch(text)) lofi += s.Seconds;
            if (ChristmasTitle().IsMatch(s.Title)) xmas += s.Seconds;
            if (OstTitle().IsMatch(s.Title)) ost += s.Seconds;
            if (AcousticTitle().IsMatch(s.Title)) acoustic += s.Seconds;
        }
        double songSeconds = Math.Max(songs.Sum(s => s.Seconds), 1);
        double S(double seconds) => seconds / songSeconds;
        // a kids' song in the top five counts even when kids' songs are a small share (it's the meme)
        bool kidsTop = songs.OrderByDescending(s => s.Plays).Take(5).Any(s => ReferenceEquals(s, topKids));
        if (topKids != null && (S(kids) >= 0.03 || kidsTop))
            Add("Kids", 0.80 + Math.Min(S(kids), 0.15), "what", topKids.Title);
        if (topLeague != null && S(league) >= 0.03)
            Add("Summoner", 0.70 + Math.Min(S(league), 0.2), "what", TitleCleaner.FirstArtist(topLeague.Artist), Pct(S(league)));
        if (S(kpop) >= 0.15) Add("Kpop", 0.58 + S(kpop) * 0.4, "what", Pct(S(kpop)));
        if (topIndie != null && S(vindie) >= 0.15) Add("VIndie", 0.56 + S(vindie) * 0.4, "what", TitleCleaner.FirstArtist(topIndie.Artist));
        if (topRap != null && S(vrap) >= 0.12) Add("VRap", 0.58 + S(vrap) * 0.4, "what", TitleCleaner.FirstArtist(topRap.Artist));
        if (S(bolero) >= 0.10) Add("Bolero", 0.64 + S(bolero) * 0.3, "what", Pct(S(bolero)));
        if (S(sad) >= 0.18) Add("Sad", 0.55 + (S(sad) - 0.18) * 1.5, "what", Pct(S(sad)));
        if (S(remix) >= 0.15) Add("Remix", 0.60 + S(remix) * 0.3, "what", Pct(S(remix)));
        if (S(sped) >= 0.08) Add("SpedUp", 0.62 + S(sped) * 0.3, "what", Pct(S(sped)));
        if (S(lofi) >= 0.12) Add("Lofi", 0.56 + S(lofi) * 0.3, "what", Pct(S(lofi)));
        if (S(xmas) >= 0.05) Add("Christmas", 0.60 + S(xmas) * 0.5, "what", Pct(S(xmas)));
        if (S(anime) >= 0.10) Add("Anime", 0.60 + S(anime) * 0.3, "what", Pct(S(anime)));
        if (S(ost) >= 0.10) Add("Ost", 0.50 + S(ost) * 0.3, "what", Pct(S(ost)));
        if (S(acoustic) >= 0.15) Add("Acoustic", 0.45 + S(acoustic) * 0.3, "what", Pct(S(acoustic)));

        // one per group (each kind of music is a group of its own: a Baby Shark parent can be a summoner too),
        // strongest first; the all-rounder when nothing stands out
        var titles = found.GroupBy(x => x.Group == "what" ? x.Key : x.Group).Select(g => g.MaxBy(x => x.Score)!)
            .OrderByDescending(x => x.Score).ToList();
        if (titles.Count == 0 || titles[0].Score < 0.30)
            titles.Insert(0, new RecapTitle("AllRounder", StableHash("AllRounder", year) % Variants["AllRounder"], 0.3, "all", []));
        return titles;
    }

    // A hash that stays the same from run to run (string.GetHashCode doesn't).
    static int StableHash(string key, int year)
    {
        uint h = 2166136261u ^ (uint)year;
        foreach (var c in key) h = (h ^ c) * 16777619u;
        return (int)(h % 1000);
    }
}
