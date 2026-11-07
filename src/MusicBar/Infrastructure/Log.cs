using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBar.Infrastructure;

public static class Log
{
    static readonly object Gate = new();

    // Song titles go into the log as 'Artist - Title' after these words. A title may hold quotes of its own
    // (That's Why...), so the mask runs to the quote that ends it, the one before ':', ' (' or ' from'.
    static readonly Regex SongTitle = new(
        @"(?<=(?:Lyrics for|lookup failed for|Lyrics from NetEase:|Lyrics shared on LRCLIB:|Lyrics picked by hand:) )'.*?'(?=:|\s\(|\s+from\s|$)",
        RegexOptions.Compiled);

    /// <summary>
    /// One file to send with a bug report: the older log, this one and the taskbar helper's, with every song
    /// title masked and the Windows user name taken out of paths, so it can go on a public issue without
    /// showing what someone listens to (#17).
    /// </summary>
    public static string ShareCopy()
    {
        var path = Path.Combine(AppPaths.DataDir, "musicbar-share.log");
        var helper = Path.Combine(AppPaths.DataDir, "taskbar-helper.log");
        string user = Environment.UserName;
        var text = new StringBuilder();
        lock (Gate)
        {
            foreach (var file in new[] { AppPaths.OldLogFile, AppPaths.LogFile, helper })
            {
                if (!File.Exists(file)) continue;
                text.AppendLine($"----- {Path.GetFileName(file)} -----");
                foreach (var line in File.ReadLines(file))
                {
                    var safe = SongTitle.Replace(line, "'…'");
                    if (user.Length > 1) safe = safe.Replace($@"\Users\{user}\", @"\Users\<user>\", StringComparison.OrdinalIgnoreCase);
                    text.AppendLine(safe);
                }
            }
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return path;
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                // A full log moves aside instead of going: the lines before a crash are often the ones wanted.
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > 1_000_000) file.MoveTo(AppPaths.OldLogFile, overwrite: true);
                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
