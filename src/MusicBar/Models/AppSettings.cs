using System;
using MusicBar.Infrastructure;

namespace MusicBar.Models;

/// <summary>Where the progress layer sits relative to the Windows taskbar.</summary>
public enum LayerMode { Auto, Under, Over }

public enum WidgetPlacement { Auto, Left, Right }

/// <summary>Where lyrics appear when shown: the biggest empty stretch of taskbar, or right beside the cover.</summary>
public enum LyricsPlacement { LargestGap, BesideCover }

/// <summary>
/// The taskbar background MusicBar paints. Auto follows the Windows taskbar's light or dark mode, so the
/// taskbar's own icons (black in light mode) stay readable.
/// </summary>
public enum BarBackground { Dark, Tinted, Transparent, Light, LightTinted, Auto, AutoTinted }

public enum AccentSource { AlbumArt, App, SystemAccent, Custom }

/// <summary>How the playback progress is drawn across the taskbar.</summary>
/// <summary>
/// How the taskbar's layer is drawn: the progress styles first, then the visualizers (from
/// Equalizer on), which only dance to the music and show no progress.
/// </summary>
public enum SeekStyle
{
    Classic, Aurora, Liquid, Neon, Spectrum, Waveform, Ribbon, Cinema, Mosaic, Mesh, Silk, Holo, Bokeh, Ink,
    Equalizer, Oscilloscope, Horizon, Aura, Fireflies, Led,
    LedRainbow, LedSpectrum, LedBreathe, LedRipple, LedScanner, LedStarlight, LedFire, LedMeteor, LedChase, LedVu,
}

/// <summary>
/// Which way the progress runs: from the start of the taskbar (left, or the bottom of a
/// vertical one), from the other end, out from the middle, or in from both ends.
/// </summary>
public enum SeekDirection { FromStart, FromEnd, FromCenter, FromEnds }

/// <summary>Which taskbars MusicBar shows on when there are several screens.</summary>
public enum DisplayTarget { Main, All, One }

/// <summary>Where the player and the lyrics go: with the progress bar, or on screens of their own.</summary>
public enum PlayerTarget { SameAsBar, Main, All, One }

/// <summary>What sits on the taskbar besides the progress bar.</summary>
public enum PlayerMode { Full, LyricsOnly, Off }

/// <summary>Where the face sits on the taskbar.</summary>
public enum FacePlacement { BesidePlayer, Start, BeforeIcons, AfterIcons }

/// <summary>The language of everything MusicBar shows.</summary>
public enum AppLanguage { English, Vietnamese }

/// <summary>How the previous / play / next buttons beside the cover look.</summary>
public enum ControlStyle { Glow, Minimal, Capsule, Ring, Vinyl, Keys, Bubbles }

public enum LyricEffect { SlideUp, Fade, Swipe, Zoom, Words, Typewriter, Ticker, None }

public enum LyricsAlign { Center, Left }

/// <summary>Korean, Japanese and Chinese lyrics in Latin letters on the bar: not at all, under each line, or instead of it.</summary>
public enum Romanization { Off, Below, Replace }

/// <summary>
/// Every user-tweakable option. Bound directly by the settings window and
/// observed by the taskbar overlay, so changes apply live.
/// </summary>
public sealed class AppSettings : ObservableObject
{
    /// <summary>
    /// What a reset leaves alone: the language, the proxy and the lyrics folder (how to reach things
    /// and where they are, not how MusicBar looks), and MusicBar's own notes: the Windows mode it holds
    /// aside, the face pictures' stamp, the update skipped, the recap already shown or asked about.
    /// </summary>
    static readonly System.Collections.Generic.HashSet<string> KeptOnReset =
    [
        nameof(Language), nameof(ProxyUrl), nameof(LyricsFolder),
        nameof(WindowsModeBefore), nameof(FacePackStamp), nameof(SkippedVersion), nameof(RecapDoneYear), nameof(RecapAskedOn),
        nameof(CommunityNickname), nameof(CommunityDevice),
    ];

    /// <summary>
    /// Every other option back to how MusicBar comes. Each one changes through its setter, so
    /// everything on screen (and the saved file) follows at once.
    /// </summary>
    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (p.GetSetMethod() == null || p.GetGetMethod() == null || KeptOnReset.Contains(p.Name)) continue;
            // Views of another setting (the lyrics delay is the offset, read the other way round).
            if (p.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true)) continue;
            p.SetValue(this, p.GetValue(defaults));
        }
    }

    /// <summary>New notifications on the control center's button as a red dot rather than their number.</summary>
    bool _notificationDot;
    public bool NotificationDot { get => _notificationDot; set => Set(ref _notificationDot, value); }

    /// <summary>Follow VLC through its web interface (VLC 3 publishes no Windows media session).</summary>
    bool _readVlc;
    public bool ReadVlc { get => _readVlc; set => Set(ref _readVlc, value); }

    // ---------- Taskbar ----------
    LayerMode _layerMode = LayerMode.Auto;
    public LayerMode LayerMode { get => _layerMode; set => Set(ref _layerMode, value); }

    /// <summary>MusicBar's own see-through taskbar, for machines without TranslucentTB.</summary>
    bool _transparentTaskbar = true;
    public bool TransparentTaskbar { get => _transparentTaskbar; set => Set(ref _transparentTaskbar, value); }

    /// <summary>
    /// Explorer crashed with the taskbar helper loaded, so MusicBar no longer loads it and tints over the taskbar
    /// instead. Turning the see-through taskbar off and on again allows it again.
    /// </summary>
    bool _helperQuarantined;
    public bool HelperQuarantined { get => _helperQuarantined; set => Set(ref _helperQuarantined, value); }

    /// <summary>Draw on the graphics card (from the next start). Off draws everything on the CPU, for drivers that crash.</summary>
    bool _hardwareAcceleration = true;
    public bool HardwareAcceleration { get => _hardwareAcceleration; set => Set(ref _hardwareAcceleration, value); }

    /// <summary>Win + A opens MusicBar's control center (while it is on) instead of Windows' Quick Settings.</summary>
    bool _winAOpensControlCenter = true;
    public bool WinAOpensControlCenter { get => _winAOpensControlCenter; set => Set(ref _winAOpensControlCenter, value); }

    /// <summary>Fold the tray's volume icon and notification button into MusicBar's control center.</summary>
    bool _controlCenter = true;
    public bool ControlCenter { get => _controlCenter; set => Set(ref _controlCenter, value); }

    WidgetPlacement _widgetPlacement = WidgetPlacement.Right;
    public WidgetPlacement WidgetPlacement
    {
        get => _widgetPlacement;
        // "Auto" from older settings files means the default spot, beside the notification area.
        set => Set(ref _widgetPlacement, value == WidgetPlacement.Auto ? WidgetPlacement.Right : value);
    }

    /// <summary>Widget width with lyrics expanded.</summary>
    double _widgetMaxWidth = 620;
    public double WidgetMaxWidth { get => _widgetMaxWidth; set => Set(ref _widgetMaxWidth, Math.Clamp(Math.Round(value), 240, 1200)); }

    bool _lyricsExpanded = true;
    public bool LyricsExpanded { get => _lyricsExpanded; set => Set(ref _lyricsExpanded, value); }

    LyricsPlacement _lyricsPlacement = LyricsPlacement.BesideCover;
    public LyricsPlacement LyricsPlacement { get => _lyricsPlacement; set => Set(ref _lyricsPlacement, value); }

    bool _showInfoPopup = true;
    public bool ShowInfoPopup { get => _showInfoPopup; set => Set(ref _showInfoPopup, value); }

    bool _onlineTrackInfo = true;
    public bool OnlineTrackInfo { get => _onlineTrackInfo; set => Set(ref _onlineTrackInfo, value); }

    bool _hideWhenIdle;
    public bool HideWhenIdle { get => _hideWhenIdle; set => Set(ref _hideWhenIdle, value); }

    bool _startWithWindows = true;
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    /// <summary>Look for a newer MusicBar on GitHub when it starts (and once a day while it runs).</summary>
    bool _checkForUpdates = true;
    public bool CheckForUpdates { get => _checkForUpdates; set => Set(ref _checkForUpdates, value); }

    /// <summary>A release the user chose to skip ("v1.4.0"); newer ones are offered again.</summary>
    string _skippedVersion = "";
    public string SkippedVersion { get => _skippedVersion; set => Set(ref _skippedVersion, value ?? ""); }

    /// <summary>Count what is listened to (on this computer only), for the year in music.</summary>
    bool _keepListeningStats = true;
    public bool KeepListeningStats { get => _keepListeningStats; set => Set(ref _keepListeningStats, value); }

    /// <summary>The year whose recap the user has seen or turned down; its end-of-year banner stays away.</summary>
    int _recapDoneYear;
    public int RecapDoneYear { get => _recapDoneYear; set => Set(ref _recapDoneYear, value); }

    /// <summary>The day the banner last asked ("2026-12-24"): it asks at most once a day.</summary>
    string _recapAskedOn = "";
    public string RecapAskedOn { get => _recapAskedOn; set => Set(ref _recapAskedOn, value ?? ""); }

    string _pinnedAppId = "";
    public string PinnedAppId { get => _pinnedAppId; set => Set(ref _pinnedAppId, value ?? ""); }

    // ---------- Appearance ----------
    BarBackground _background = BarBackground.Transparent;
    public BarBackground Background { get => _background; set => Set(ref _background, value); }

    /// <summary>Windows' taskbar mode follows the bar, so the tray icons and the clock stay readable on it.</summary>
    bool _matchWindowsMode = true;
    public bool MatchWindowsMode { get => _matchWindowsMode; set => Set(ref _matchWindowsMode, value); }

    /// <summary>The user's own Windows taskbar mode while MusicBar has changed it (1 light, 0 dark), -1 when untouched.</summary>
    int _windowsModeBefore = -1;
    public int WindowsModeBefore { get => _windowsModeBefore; set => Set(ref _windowsModeBefore, value); }

    double _backgroundOpacity;
    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, Math.Clamp(value, 0, 1)); }

    AccentSource _accentSource = AccentSource.AlbumArt;
    public AccentSource AccentSource { get => _accentSource; set => Set(ref _accentSource, value); }

    string _customColor = "#FF6B6B";
    public string CustomColor { get => _customColor; set => SetColor(ref _customColor, value); }

    /// <summary>Stores a valid color as #RRGGBB; anything else puts the old value back in the box.</summary>
    void SetColor(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (ColorUtil.TryParse(value, out var c)) Set(ref field, ColorUtil.ToHex(c), name);
        else OnPropertyChanged(name);
    }

    SeekStyle _seekStyle = SeekStyle.Aurora;
    public SeekStyle SeekStyle { get => _seekStyle; set => Set(ref _seekStyle, value); }

    /// <summary>A skin's file in the Skins folder, drawn instead of <see cref="SeekStyle"/>; empty for none.</summary>
    string _skin = "";
    public string Skin { get => _skin; set => Set(ref _skin, value ?? ""); }

    /// <summary>Colors each look keeps instead of the song's (see Services.StylePalettes), by style name or "skin:file".</summary>
    System.Collections.Generic.Dictionary<string, string> _palettes = new() { ["Spectrum"] = "preset:neon" };
    public System.Collections.Generic.Dictionary<string, string> Palettes { get => _palettes; set => Set(ref _palettes, value ?? []); }

    SeekDirection _seekDirection = SeekDirection.FromStart;
    public SeekDirection SeekDirection { get => _seekDirection; set => Set(ref _seekDirection, value); }

    /// <summary>Let the seekbar follow the music (reads the system audio mix locally).</summary>
    bool _audioReactive = true;
    public bool AudioReactive { get => _audioReactive; set => Set(ref _audioReactive, value); }

    /// <summary>Paused, the animated styles keep drifting slowly in place instead of standing still.</summary>
    bool _seekIdleMotion = true;
    public bool SeekIdleMotion { get => _seekIdleMotion; set => Set(ref _seekIdleMotion, value); }

    /// <summary>The bar keeps clear of Windows' icons and runs on through the empty stretches between them.</summary>
    bool _seekAvoidIcons = true;
    public bool SeekAvoidIcons { get => _seekAvoidIcons; set => Set(ref _seekAvoidIcons, value); }

    double _fillOpacity = 0.9;
    public double FillOpacity { get => _fillOpacity; set => Set(ref _fillOpacity, Math.Clamp(value, 0.05, 1)); }

    bool _showAlbumArt = true;
    public bool ShowAlbumArt { get => _showAlbumArt; set => Set(ref _showAlbumArt, value); }

    bool _showAppIcon = true;
    public bool ShowAppIcon { get => _showAppIcon; set => Set(ref _showAppIcon, value); }

    bool _showControls = true;
    public bool ShowControls { get => _showControls; set => Set(ref _showControls, value); }

    /// <summary>The player (cover and buttons) on the taskbar, only its lyrics, or neither (just the progress bar).</summary>
    PlayerMode _playerMode = PlayerMode.Full;
    public PlayerMode PlayerMode { get => _playerMode; set => Set(ref _playerMode, value); }

    DisplayTarget _displayTarget = DisplayTarget.Main;
    public DisplayTarget DisplayTarget { get => _displayTarget; set => Set(ref _displayTarget, value); }

    // The screen for DisplayTarget.One, by its device name (\\.\DISPLAY2).
    string _displayMonitor = "";
    public string DisplayMonitor { get => _displayMonitor; set => Set(ref _displayMonitor, value ?? ""); }

    PlayerTarget _playerTarget = PlayerTarget.SameAsBar;
    public PlayerTarget PlayerTarget { get => _playerTarget; set => Set(ref _playerTarget, value); }

    // The screen for PlayerTarget.One.
    string _playerMonitor = "";
    public string PlayerMonitor { get => _playerMonitor; set => Set(ref _playerMonitor, value ?? ""); }

    AppLanguage _language = AppLanguage.English;
    public AppLanguage Language { get => _language; set => Set(ref _language, value); }

    ControlStyle _controlStyle = ControlStyle.Minimal;
    public ControlStyle ControlStyle { get => _controlStyle; set => Set(ref _controlStyle, value); }

    string _fontFamily = "Segoe UI Variable Text";
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value); }

    // ---------- Face ----------
    /// <summary>The little face that lives on the taskbar, as in the promo film.</summary>
    bool _faceEnabled;
    public bool FaceEnabled { get => _faceEnabled; set => Set(ref _faceEnabled, value); }

    Views.Face.FaceStyle _faceStyle = Views.Face.FaceStyle.Classic;
    public Views.Face.FaceStyle FaceStyle { get => _faceStyle; set => Set(ref _faceStyle, value); }

    /// <summary>Moves on whenever the face's pictures change, so every face reads them again.</summary>
    long _facePackStamp;
    public long FacePackStamp { get => _facePackStamp; set => Set(ref _facePackStamp, value); }

    FacePlacement _facePlacement = FacePlacement.BeforeIcons;
    public FacePlacement FacePlacement { get => _facePlacement; set => Set(ref _facePlacement, value); }

    /// <summary>The face's size against the taskbar's thickness.</summary>
    double _faceScale = 1.0;
    public double FaceScale { get => _faceScale; set => Set(ref _faceScale, Math.Clamp(value, 0.6, 1.0)); }

    /// <summary>Sleepy at night, waking up in the morning, a yawn after lunch.</summary>
    bool _faceFollowsClock = true;
    public bool FaceFollowsClock { get => _faceFollowsClock; set => Set(ref _faceFollowsClock, value); }

    bool _faceReactsToMusic = true;
    public bool FaceReactsToMusic { get => _faceReactsToMusic; set => Set(ref _faceReactsToMusic, value); }

    bool _faceWatchesPointer = true;
    public bool FaceWatchesPointer { get => _faceWatchesPointer; set => Set(ref _faceWatchesPointer, value); }

    /// <summary>With nothing playing, MusicBar steps aside but the face stays on the taskbar (and naps).</summary>
    bool _faceStaysWhenIdle = true;
    public bool FaceStaysWhenIdle { get => _faceStaysWhenIdle; set => Set(ref _faceStaysWhenIdle, value); }

    Views.Face.FaceHat _faceHat = Views.Face.FaceHat.None;
    public Views.Face.FaceHat FaceHat { get => _faceHat; set => Set(ref _faceHat, value); }

    Views.Face.FaceGlasses _faceGlasses = Views.Face.FaceGlasses.None;
    public Views.Face.FaceGlasses FaceGlasses { get => _faceGlasses; set => Set(ref _faceGlasses, value); }

    bool _faceHeadphones;
    public bool FaceHeadphones { get => _faceHeadphones; set => Set(ref _faceHeadphones, value); }

    /// <summary>A Santa hat at Christmas, apricot blossom at Tết, rabbit ears at Mid-Autumn, a witch hat at Halloween.</summary>
    bool _faceSeasonal = true;
    public bool FaceSeasonal { get => _faceSeasonal; set => Set(ref _faceSeasonal, value); }

    /// <summary>Holds a microphone while the music plays.</summary>
    bool _faceHoldsMic = true;
    public bool FaceHoldsMic { get => _faceHoldsMic; set => Set(ref _faceHoldsMic, value); }

    /// <summary>Notices the computer: a busy processor, the network dropping, the charger.</summary>
    bool _faceReactsToSystem = true;
    public bool FaceReactsToSystem { get => _faceReactsToSystem; set => Set(ref _faceReactsToSystem, value); }

    /// <summary>Wanders along the empty taskbar now and then when nothing is going on.</summary>
    bool _faceStrolls = true;
    public bool FaceStrolls { get => _faceStrolls; set => Set(ref _faceStrolls, value); }

    int _focusMinutes = 25;
    public int FocusMinutes { get => _focusMinutes; set => Set(ref _focusMinutes, Math.Clamp(value, 5, 90)); }

    int _breakMinutes = 5;
    public int BreakMinutes { get => _breakMinutes; set => Set(ref _breakMinutes, Math.Clamp(value, 1, 30)); }

    // ---------- Lyrics ----------
    bool _lyricsEnabled = true;
    public bool LyricsEnabled { get => _lyricsEnabled; set => Set(ref _lyricsEnabled, value); }

    LyricEffect _lyricEffect = LyricEffect.SlideUp;
    public LyricEffect LyricEffect { get => _lyricEffect; set => Set(ref _lyricEffect, value); }

    double _animationSpeed = 1.0;
    public double AnimationSpeed { get => _animationSpeed; set => Set(ref _animationSpeed, Math.Clamp(value, 0.4, 2.5)); }

    bool _karaoke = true;
    public bool Karaoke { get => _karaoke; set => Set(ref _karaoke, value); }

    bool _showNextLine;
    public bool ShowNextLine { get => _showNextLine; set => Set(ref _showNextLine, value); }

    double _lyricScale = 1.156;
    public double LyricScale { get => _lyricScale; set => Set(ref _lyricScale, Math.Clamp(value, 0.6, 2.0)); }

    string _lyricColor = "#FFFFFF";
    public string LyricColor { get => _lyricColor; set => SetColor(ref _lyricColor, value); }

    /// <summary>How solid the lyric text is; the next line and the unsung part follow it, fainter.</summary>
    double _lyricOpacity = 1;
    public double LyricOpacity { get => _lyricOpacity; set => Set(ref _lyricOpacity, Math.Clamp(value, 0.2, 1)); }

    /// <summary>A halo around the lyric text, so it stands off whatever runs underneath.</summary>
    bool _lyricStroke = true;
    public bool LyricStroke { get => _lyricStroke; set => Set(ref _lyricStroke, value); }

    string _lyricStrokeColor = "#000000";
    public string LyricStrokeColor { get => _lyricStrokeColor; set => SetColor(ref _lyricStrokeColor, value); }

    double _lyricStrokeOpacity = 0.75;
    public double LyricStrokeOpacity { get => _lyricStrokeOpacity; set => Set(ref _lyricStrokeOpacity, Math.Clamp(value, 0, 1)); }

    /// <summary>Halo size in pixels: small is a tight outline, large a soft glow.</summary>
    double _lyricStrokeRadius = 12;
    public double LyricStrokeRadius { get => _lyricStrokeRadius; set => Set(ref _lyricStrokeRadius, Math.Clamp(value, 1, 24)); }

    LyricsAlign _lyricsAlign = LyricsAlign.Center;
    public LyricsAlign LyricsAlign { get => _lyricsAlign; set => Set(ref _lyricsAlign, value); }

    /// <summary>Global timing in LRC's sense: positive shows lyrics earlier.</summary>
    int _lyricsOffsetMs;
    public int LyricsOffsetMs
    {
        get => _lyricsOffsetMs;
        set
        {
            if (Set(ref _lyricsOffsetMs, Math.Clamp(value, -5000, 5000))) OnPropertyChanged(nameof(LyricsDelayMs));
        }
    }

    /// <summary>The same timing as people read it, a subtitle delay: positive shows lyrics later. Not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int LyricsDelayMs { get => -_lyricsOffsetMs; set => LyricsOffsetMs = -value; }

    Romanization _romanization = Romanization.Replace;
    public Romanization Romanization { get => _romanization; set => Set(ref _romanization, value); }

    /// <summary>The lyrics panel shows the romanization under each line.</summary>
    bool _lyricsPanelRomanized = true;
    public bool LyricsPanelRomanized { get => _lyricsPanelRomanized; set => Set(ref _lyricsPanelRomanized, value); }

    bool _fetchOnline = true;
    public bool FetchOnline { get => _fetchOnline; set => Set(ref _fetchOnline, value); }

    /// <summary>Ask NetEase Cloud Music (an unofficial source) when LRCLIB has no synced lyrics.</summary>
    bool _lyricsFromNetEase;
    public bool LyricsFromNetEase { get => _lyricsFromNetEase; set => Set(ref _lyricsFromNetEase, value); }

    string _proxyUrl = "";
    public string ProxyUrl { get => _proxyUrl; set => Set(ref _proxyUrl, (value ?? "").Trim()); }

    string _lyricsFolder = "";
    public string LyricsFolder { get => _lyricsFolder; set => Set(ref _lyricsFolder, (value ?? "").Trim()); }

    /// <summary>The name shown on the online gallery next to what this person shares.</summary>
    string _communityNickname = "";
    public string CommunityNickname { get => _communityNickname; set => Set(ref _communityNickname, (value ?? "").Trim()); }

    /// <summary>A random id for this copy, sent with what it shares so the gallery can slow down floods.</summary>
    string _communityDevice = "";
    public string CommunityDevice { get => _communityDevice; set => Set(ref _communityDevice, value ?? ""); }
}
