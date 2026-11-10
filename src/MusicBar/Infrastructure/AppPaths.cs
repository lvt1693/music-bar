using System;
using System.IO;

namespace MusicBar.Infrastructure;

public static class AppPaths
{
    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBar"));

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    /// <summary>The settings as they were the last time the file loaded cleanly.</summary>
    public static string SettingsBackupFile => Path.Combine(DataDir, "settings.bak");

    /// <summary>Where an unreadable settings file is kept: <c>settings.bad-yyyyMMdd-HHmmss.json</c>.</summary>
    public static string BadSettingsFile(DateTime when) => Path.Combine(DataDir, $"settings.bad-{when:yyyyMMdd-HHmmss}.json");

    public const string BadSettingsPattern = "settings.bad-*.json";
    public static string LogFile => Path.Combine(DataDir, "musicbar.log");
    public static string OldLogFile => Path.Combine(DataDir, "musicbar.old.log");
    public static string DefaultLyricsDir => Path.Combine(DataDir, "Lyrics");

    static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
