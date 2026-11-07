using System;
using System.IO;

namespace MusicBar.Infrastructure;

public static class Log
{
    static readonly object Gate = new();

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
