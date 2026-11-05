using System.Collections.Generic;
using System.Linq;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>A choice in a list; its label is looked up in <see cref="Strings"/> whenever it is shown.</summary>
public sealed record Option(object Value, string LabelKey)
{
    public string Label => Loc.T(LabelKey);
}

public sealed record SeekStyleOption(SeekStyle Value, string LabelKey, string DescriptionKey)
{
    public string Label => Loc.T(LabelKey);
    public string Description => Loc.T(DescriptionKey);
}

public sealed record FaceStyleOption(Face.FaceStyle Value, string LabelKey, string DescriptionKey)
{
    public string Label => Loc.T(LabelKey);
    public string Description => Loc.T(DescriptionKey);
}

/// <summary>Display labels for every enum the settings window exposes.</summary>
public static class SettingsOptions
{
    /// <summary>Each language in its own words, so it can be found whatever language is showing.</summary>
    public static IReadOnlyList<Option> Languages { get; } =
    [
        new(AppLanguage.English, "Lang_English"),
        new(AppLanguage.Vietnamese, "Lang_Vietnamese"),
    ];

    public static IReadOnlyList<Option> LayerModes { get; } =
    [
        new(LayerMode.Auto, "Opt_Layer_Auto"),
        new(LayerMode.Under, "Opt_Layer_Under"),
        new(LayerMode.Over, "Opt_Layer_Over"),
    ];

    public static IReadOnlyList<Option> Placements { get; } =
    [
        new(WidgetPlacement.Right, "Opt_Place_Right"),
        new(WidgetPlacement.Left, "Opt_Place_Left"),
    ];

    public static IReadOnlyList<Option> LyricsPlacements { get; } =
    [
        new(LyricsPlacement.BesideCover, "Opt_Lyrics_BesideCover"),
        new(LyricsPlacement.LargestGap, "Opt_Lyrics_LargestGap"),
    ];

    public static IReadOnlyList<Option> TaskbarSizes { get; } =
    [
        new(true, "Opt_Size_Small"),
        new(false, "Opt_Size_Medium"),
    ];

    public static IReadOnlyList<Option> Backgrounds { get; } =
    [
        new(BarBackground.Auto, "Opt_Bg_Auto"),
        new(BarBackground.AutoTinted, "Opt_Bg_AutoTinted"),
        new(BarBackground.Dark, "Opt_Bg_Dark"),
        new(BarBackground.Tinted, "Opt_Bg_Tinted"),
        new(BarBackground.Light, "Opt_Bg_Light"),
        new(BarBackground.LightTinted, "Opt_Bg_LightTinted"),
        new(BarBackground.Transparent, "Opt_Bg_Transparent"),
    ];

    public static IReadOnlyList<Option> ControlStyles { get; } =
    [
        new(ControlStyle.Glow, "Opt_Ctl_Glow"),
        new(ControlStyle.Minimal, "Opt_Ctl_Minimal"),
        new(ControlStyle.Capsule, "Opt_Ctl_Capsule"),
        new(ControlStyle.Ring, "Opt_Ctl_Ring"),
        new(ControlStyle.Vinyl, "Opt_Ctl_Vinyl"),
        new(ControlStyle.Keys, "Opt_Ctl_Keys"),
        new(ControlStyle.Bubbles, "Opt_Ctl_Bubbles"),
    ];

    public static IReadOnlyList<FaceStyleOption> FaceStyles { get; } =
    [
        new(Face.FaceStyle.Classic, "Face_Classic", "Face_Classic_Desc"),
        new(Face.FaceStyle.ClassicGirl, "Face_ClassicGirl", "Face_ClassicGirl_Desc"),
        new(Face.FaceStyle.AnimeGirl, "Face_AnimeGirl", "Face_AnimeGirl_Desc"),
        new(Face.FaceStyle.AnimeBoy, "Face_AnimeBoy", "Face_AnimeBoy_Desc"),
        new(Face.FaceStyle.Alien, "Face_Alien", "Face_Alien_Desc"),
        new(Face.FaceStyle.Robot, "Face_Robot", "Face_Robot_Desc"),
        new(Face.FaceStyle.Cat, "Face_Cat", "Face_Cat_Desc"),
        new(Face.FaceStyle.Derp, "Face_Derp", "Face_Derp_Desc"),
        new(Face.FaceStyle.Custom, "Face_Custom", "Face_Custom_Desc"),
    ];

    public static IReadOnlyList<Option> FacePlacements { get; } =
    [
        new(FacePlacement.BesidePlayer, "Opt_Face_BesidePlayer"),
        new(FacePlacement.Start, "Opt_Face_Start"),
        new(FacePlacement.BeforeIcons, "Opt_Face_BeforeIcons"),
        new(FacePlacement.AfterIcons, "Opt_Face_AfterIcons"),
    ];

    public static IReadOnlyList<Option> FaceHats { get; } =
    [
        new(Face.FaceHat.None, "Opt_Hat_None"),
        new(Face.FaceHat.Cap, "Opt_Hat_Cap"),
        new(Face.FaceHat.Beanie, "Opt_Hat_Beanie"),
        new(Face.FaceHat.Crown, "Opt_Hat_Crown"),
        new(Face.FaceHat.Santa, "Opt_Hat_Santa"),
        new(Face.FaceHat.Witch, "Opt_Hat_Witch"),
        new(Face.FaceHat.Bunny, "Opt_Hat_Bunny"),
        new(Face.FaceHat.Mai, "Opt_Hat_Mai"),
    ];

    public static IReadOnlyList<Option> FaceGlasses { get; } =
    [
        new(Face.FaceGlasses.None, "Opt_Glasses_None"),
        new(Face.FaceGlasses.Round, "Opt_Glasses_Round"),
        new(Face.FaceGlasses.Shades, "Opt_Glasses_Shades"),
        new(Face.FaceGlasses.Pixel, "Opt_Glasses_Pixel"),
    ];

    public static IReadOnlyList<Option> Romanizations { get; } =
    [
        new(Romanization.Off, "Opt_Roman_Off"),
        new(Romanization.Below, "Opt_Roman_Below"),
        new(Romanization.Replace, "Opt_Roman_Replace"),
    ];

    public static IReadOnlyList<Option> PlayerModes { get; } =
    [
        new(PlayerMode.Full, "Opt_Player_Full"),
        new(PlayerMode.LyricsOnly, "Opt_Player_LyricsOnly"),
        new(PlayerMode.Off, "Opt_Player_Off"),
    ];

    public static IReadOnlyList<Option> SeekDirections { get; } =
    [
        new(SeekDirection.FromStart, "Opt_Dir_FromStart"),
        new(SeekDirection.FromEnd, "Opt_Dir_FromEnd"),
        new(SeekDirection.FromCenter, "Opt_Dir_FromCenter"),
        new(SeekDirection.FromEnds, "Opt_Dir_FromEnds"),
    ];

    public static IReadOnlyList<Option> AccentSources { get; } =
    [
        new(AccentSource.AlbumArt, "Opt_Accent_Art"),
        new(AccentSource.App, "Opt_Accent_App"),
        new(AccentSource.SystemAccent, "Opt_Accent_System"),
        new(AccentSource.Custom, "Opt_Accent_Custom"),
    ];

    public static IReadOnlyList<SeekStyleOption> SeekStyles { get; } =
    [
        new(SeekStyle.Aurora, "Seek_Aurora", "Seek_Aurora_Desc"),
        new(SeekStyle.Mesh, "Seek_Mesh", "Seek_Mesh_Desc"),
        new(SeekStyle.Silk, "Seek_Silk", "Seek_Silk_Desc"),
        new(SeekStyle.Holo, "Seek_Holo", "Seek_Holo_Desc"),
        new(SeekStyle.Bokeh, "Seek_Bokeh", "Seek_Bokeh_Desc"),
        new(SeekStyle.Ink, "Seek_Ink", "Seek_Ink_Desc"),
        new(SeekStyle.Ribbon, "Seek_Ribbon", "Seek_Ribbon_Desc"),
        new(SeekStyle.Cinema, "Seek_Cinema", "Seek_Cinema_Desc"),
        new(SeekStyle.Neon, "Seek_Neon", "Seek_Neon_Desc"),
        new(SeekStyle.Liquid, "Seek_Liquid", "Seek_Liquid_Desc"),
        new(SeekStyle.Spectrum, "Seek_Spectrum", "Seek_Spectrum_Desc"),
        new(SeekStyle.Waveform, "Seek_Waveform", "Seek_Waveform_Desc"),
        new(SeekStyle.Mosaic, "Seek_Mosaic", "Seek_Mosaic_Desc"),
        new(SeekStyle.Classic, "Seek_Classic", "Seek_Classic_Desc"),
    ];

    /// <summary>The styles that only dance to the music, in their own row of the gallery.</summary>
    public static IReadOnlyList<SeekStyleOption> Visualizers { get; } =
    [
        new(SeekStyle.Equalizer, "Seek_Equalizer", "Seek_Equalizer_Desc"),
        new(SeekStyle.Horizon, "Seek_Horizon", "Seek_Horizon_Desc"),
        new(SeekStyle.Oscilloscope, "Seek_Oscilloscope", "Seek_Oscilloscope_Desc"),
        new(SeekStyle.Aura, "Seek_Aura", "Seek_Aura_Desc"),
        new(SeekStyle.Fireflies, "Seek_Fireflies", "Seek_Fireflies_Desc"),
        new(SeekStyle.Led, "Seek_Led", "Seek_Led_Desc"),
    ];

    /// <summary>The RGB LED strip's effects: visualizers too, in a gallery tab of their own.</summary>
    public static IReadOnlyList<SeekStyleOption> LedStrips { get; } =
    [
        new(SeekStyle.LedRainbow, "Seek_LedRainbow", "Seek_LedRainbow_Desc"),
        new(SeekStyle.LedSpectrum, "Seek_LedSpectrum", "Seek_LedSpectrum_Desc"),
        new(SeekStyle.LedBreathe, "Seek_LedBreathe", "Seek_LedBreathe_Desc"),
        new(SeekStyle.LedRipple, "Seek_LedRipple", "Seek_LedRipple_Desc"),
        new(SeekStyle.LedScanner, "Seek_LedScanner", "Seek_LedScanner_Desc"),
        new(SeekStyle.LedStarlight, "Seek_LedStarlight", "Seek_LedStarlight_Desc"),
        new(SeekStyle.LedFire, "Seek_LedFire", "Seek_LedFire_Desc"),
        new(SeekStyle.LedMeteor, "Seek_LedMeteor", "Seek_LedMeteor_Desc"),
        new(SeekStyle.LedChase, "Seek_LedChase", "Seek_LedChase_Desc"),
        new(SeekStyle.LedVu, "Seek_LedVu", "Seek_LedVu_Desc"),
    ];

    /// <summary>Every style in gallery order: progress styles, visualizers, the LED strip.</summary>
    public static IEnumerable<SeekStyleOption> AllStyles => SeekStyles.Concat(Visualizers).Concat(LedStrips);

    public static IReadOnlyList<Option> LyricEffects { get; } =
    [
        new(LyricEffect.SlideUp, "Fx_SlideUp"),
        new(LyricEffect.Fade, "Fx_Fade"),
        new(LyricEffect.Swipe, "Fx_Swipe"),
        new(LyricEffect.Zoom, "Fx_Zoom"),
        new(LyricEffect.Words, "Fx_Words"),
        new(LyricEffect.Typewriter, "Fx_Typewriter"),
        new(LyricEffect.Ticker, "Fx_Ticker"),
        new(LyricEffect.None, "Fx_None"),
    ];

    public static IReadOnlyList<Option> Alignments { get; } =
    [
        new(LyricsAlign.Left, "Opt_Align_Left"),
        new(LyricsAlign.Center, "Opt_Align_Center"),
    ];

    public static IReadOnlyList<string> Swatches { get; } =
    [
        "#FF6B6B", "#FF4D8D", "#F72585", "#9B5DE5", "#4CC9F0", "#3A86FF",
        "#2EC4B6", "#1ED760", "#A3E635", "#FFD166", "#FF9F1C", "#E5E5E5",
    ];

    /// <summary>Light colors that read on a dark taskbar.</summary>
    public static IReadOnlyList<string> LyricSwatches { get; } =
    [
        "#FFFFFF", "#FFF4D6", "#FFE066", "#FFC2A1", "#FFB3C7", "#E0C3FF", "#B5E8FF", "#B9F6CA",
    ];

    /// <summary>Dark colors for the stroke, plus white for dark text.</summary>
    public static IReadOnlyList<string> StrokeSwatches { get; } =
    [
        "#000000", "#14141C", "#1B2440", "#2B1B3D", "#3D0C11", "#0B3D2E", "#FFFFFF",
    ];
}
