using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Views;

namespace MusicBar.Services;

/// <summary>A look someone shared: one of MusicBar's styles, three colors and a few settings. No code.</summary>
public sealed record LookPreset(string File, string Name, string Kind, SeekStyle Style, Color[] Colors, double Intensity,
    SeekDirection Direction, bool IdleMotion, string Description, string Author);

/// <summary>
/// Presets from the online gallery, kept in %LOCALAPPDATA%\MusicBar\Presets as the small JSON files the
/// gallery serves ("musicbar-look/1"). Read as strictly as the server writes them: anything else is ignored.
/// </summary>
public static partial class LookPresets
{
    public const string Format = "musicbar-look/1";
    public static string Folder => Path.Combine(AppPaths.DataDir, "Presets");

    public static IReadOnlyList<LookPreset> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<LookPreset>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                if (Parse(File.ReadAllText(path), Path.GetFileName(path)) is { } preset) list.Add(preset);
            }
            catch (IOException)
            {
            }
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The preset in this text, or null when it isn't one MusicBar can use.</summary>
    public static LookPreset? Parse(string json, string file, string author = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "format") != Format) return null;
            if (!Enum.TryParse<SeekStyle>(Text(root, "style"), out var style) || !Enum.IsDefined(style)) return null;
            if (!root.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Array || colors.GetArrayLength() != 3) return null;
            var palette = new Color[3];
            for (int i = 0; i < 3; i++)
            {
                var hex = colors[i].ValueKind == JsonValueKind.String ? colors[i].GetString() ?? "" : "";
                if (!HexColor().IsMatch(hex)) return null;
                palette[i] = (Color)ColorConverter.ConvertFromString(hex);
            }
            double intensity = root.TryGetProperty("intensity", out var k) && k.ValueKind == JsonValueKind.Number ? Math.Clamp(k.GetDouble(), 0.05, 1) : 0.8;
            var direction = Enum.TryParse<SeekDirection>(Text(root, "direction"), out var d) && Enum.IsDefined(d) ? d : SeekDirection.FromStart;
            bool idle = !root.TryGetProperty("idleMotion", out var m) || m.ValueKind != JsonValueKind.False;
            string name = Clip(Text(root, "name"), 40);
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(file);
            return new LookPreset(file, name, Clip(Text(root, "kind"), 12), style, palette, intensity, direction, idle,
                Clip(Text(root, "description"), 200), author);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves a downloaded preset (checked first) under the gallery's name for it.</summary>
    public static LookPreset? Save(string slug, string json)
    {
        var file = SafeName(slug) + ".json";
        if (Parse(json, file) is not { } preset) return null;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, file), json, new UTF8Encoding(false));
        Log.Info($"Preset saved: {file}");
        return preset;
    }

    public static void Delete(string file)
    {
        var path = Path.Combine(Folder, Path.GetFileName(file));
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Puts the preset on the bar: its style (no skin), its colors for that style, its strength and direction.</summary>
    public static void Apply(LookPreset p, AppSettings s)
    {
        s.Skin = "";
        s.SeekStyle = p.Style;
        StylePalettes.Set(s, StylePalettes.KeyFor(p.Style), string.Join(",", p.Colors.Select(Hex)));
        s.FillOpacity = p.Intensity;
        s.SeekDirection = p.Direction;
        s.SeekIdleMotion = p.IdleMotion;
        Log.Info($"Preset applied: {p.Name} ({p.Style})");
    }

    public static bool IsOn(LookPreset p, AppSettings s) =>
        s.Skin.Length == 0 && s.SeekStyle == p.Style
        && StylePalettes.Saved(s, StylePalettes.KeyFor(p.Style)).Equals(string.Join(",", p.Colors.Select(Hex)), StringComparison.OrdinalIgnoreCase);

    /// <summary>The look on the bar now, as a preset to share; null while a skin is on (the skin itself is shared then).</summary>
    public static string? FromSettings(AppSettings s, string name, string description, Color fallbackA, Color fallbackB)
    {
        if (s.Skin.Length > 0) return null;
        var palette = StylePalettes.For(s, StylePalettes.KeyFor(s.SeekStyle));
        var colors = palette is { } c ? new[] { c.A, c.B, c.C } : [fallbackA, fallbackB, ColorUtil.ShiftHue(fallbackA, 28, -0.04)];
        var kind = SeekRenderer.IsLedStrip(s.SeekStyle) ? "led" : SeekRenderer.IsVisualizer(s.SeekStyle) ? "visualizer" : "progress";
        var value = new Dictionary<string, object>
        {
            ["format"] = Format, ["name"] = name, ["kind"] = kind, ["style"] = s.SeekStyle.ToString(),
            ["colors"] = colors.Select(Hex).ToArray(), ["intensity"] = Math.Round(s.FillOpacity, 3),
            ["direction"] = s.SeekDirection.ToString(), ["idleMotion"] = s.SeekIdleMotion,
        };
        if (description.Length > 0) value["description"] = description;
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>A file name from a gallery slug: letters, digits and dashes only.</summary>
    public static string SafeName(string slug)
    {
        var name = Regex.Replace(slug ?? "", "[^a-zA-Z0-9-]", "-").Trim('-');
        return name.Length == 0 ? "preset" : name[..Math.Min(48, name.Length)];
    }

    static string Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string Clip(string text, int max)
    {
        text = Regex.Replace(text, @"[\p{C}]", " ").Trim();
        return text.Length > max ? text[..max] : text;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
