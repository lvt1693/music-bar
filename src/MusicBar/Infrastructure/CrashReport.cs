using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Threading.Tasks;

namespace MusicBar.Infrastructure;

/// <summary>
/// Leaves a mark in the data folder while MusicBar runs and takes it away on a clean exit. A mark still there
/// at the next start means the last run ended without closing: a crash, a forced quit or a power cut. A native
/// crash (a driver, UI Automation, the audio stack) kills the process before it can write a line, so the crash
/// reports Windows wrote for MusicBar.exe since then (faulting module, error code, a .NET stack) are copied
/// into musicbar.log: the one file users send holds the whole story.
/// </summary>
public static class CrashReport
{
    static string Mark => Path.Combine(AppPaths.DataDir, "running.mark");

    // Only the copy that set the mark takes it away: a second copy handing over a link exits cleanly too.
    static bool _marked;

    public static void Started()
    {
        try
        {
            if (File.Exists(Mark))
            {
                var since = File.GetLastWriteTime(Mark);
                Log.Info($"The last run (started {since:yyyy-MM-dd HH:mm:ss}) ended without closing: a crash, a forced quit or a power cut");
                Task.Run(() =>
                {
                    if (CopyWindowsReports(since, "MusicBar").Count == 0)
                        Log.Info("Windows has no crash report for MusicBar since then (closed from Task Manager, or the PC went off)");
                });
            }
            File.WriteAllText(Mark, Environment.ProcessId.ToString());
            _marked = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Writing the running mark failed", ex);
        }
    }

    public static void Closed()
    {
        if (!_marked) return;
        try
        {
            File.Delete(Mark);
            _marked = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Removing the running mark failed", ex);
        }
    }

    /// <summary>
    /// The taskbar window went away and came back: Explorer restarted. When Explorer crashed (users then say
    /// "the taskbar crashed"), Windows' report names the module; a moment later it is copied into the log.
    /// </summary>
    public static void TaskbarRestarted()
    {
        Log.Info("The taskbar window went away (Explorer restarted)");
        var since = DateTime.Now.AddMinutes(-2);
        Task.Run(async () =>
        {
            await Task.Delay(8000);
            if (CopyWindowsReports(since, "explorer.exe", 1) is [var report, ..]) ExplorerCrashed?.Invoke(report);
        });
    }

    /// <summary>Explorer crashed (Windows' report as text): raised on a pool thread, for the taskbar helper's failsafe.</summary>
    public static event Action<string>? ExplorerCrashed;

    /// <summary>
    /// Windows' own reports (Application Error, .NET Runtime, Application Hang) naming a program since a moment,
    /// newest first, into the log. Returns the ones copied.
    /// </summary>
    static System.Collections.Generic.List<string> CopyWindowsReports(DateTime since, string program, int most = 3)
    {
        var reports = new System.Collections.Generic.List<string>();
        try
        {
            long ms = (long)Math.Max(60_000, (DateTime.Now - since).TotalMilliseconds + 60_000);
            var query = new EventLogQuery("Application", PathType.LogName,
                "*[System[Provider[@Name='Application Error' or @Name='.NET Runtime' or @Name='Application Hang']" +
                $" and TimeCreated[timediff(@SystemTime) <= {ms}]]]")
            {
                // Newest first: after a run of days, reading from the oldest worked through every other program's reports.
                ReverseDirection = true,
            };
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record != null && reports.Count < most; record = reader.ReadEvent())
            {
                using (record)
                {
                    var text = record.FormatDescription() ?? "";
                    if (!text.Contains(program, StringComparison.OrdinalIgnoreCase)) continue;
                    if (text.Length > 4000) text = text[..4000] + " ...";
                    reports.Add(text);
                    Log.Info($"Windows report ({record.ProviderName} {record.Id}, {record.TimeCreated:yyyy-MM-dd HH:mm:ss}): {text.Trim()}");
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Reading Windows' crash reports failed", ex);
        }
        return reports;
    }
}
