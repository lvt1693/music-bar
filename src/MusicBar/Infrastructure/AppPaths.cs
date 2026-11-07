using System;
using System.IO;

namespace MusicBar.Infrastructure;

public static class AppPaths
{
    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBar"));

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "musicbar.log");
    public static string OldLogFile => Path.Combine(DataDir, "musicbar.old.log");
    public static string DefaultLyricsDir => Path.Combine(DataDir, "Lyrics");

    static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
