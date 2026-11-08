using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace MusicBar.Infrastructure;

/// <summary>
/// Notes in the log when the UI thread was held up long enough for the bar and the lyrics to stop moving
/// visibly. A stutter "every 7 or 8 seconds" (#17) left nothing in a log sent in; now the times show, and how
/// regular they are points at what did it. A watcher thread asks the UI thread to answer four times a second.
/// </summary>
public static class StallLog
{
    const int CheckEveryMs = 250;
    const int StallMs = 400;
    // At most a line this often; the ones in between are counted into the next.
    static readonly TimeSpan Quiet = TimeSpan.FromSeconds(20);

    public static void Start(Dispatcher dispatcher)
    {
        new Thread(() => Watch(dispatcher)) { IsBackground = true, Name = "MusicBar.StallLog", Priority = ThreadPriority.BelowNormal }.Start();
    }

    static void Watch(Dispatcher dispatcher)
    {
        var lastLine = DateTime.MinValue;
        int unlogged = 0;
        long worstUnlogged = 0;
        using var answered = new ManualResetEventSlim();
        while (!dispatcher.HasShutdownStarted)
        {
            Thread.Sleep(CheckEveryMs);
            answered.Reset();
            var asked = Stopwatch.StartNew();
            // Input priority: ahead of timers and data binding, behind what is already running.
            dispatcher.BeginInvoke(DispatcherPriority.Input, answered.Set);
            if (answered.Wait(StallMs)) continue;
            while (!answered.Wait(1000))
                if (dispatcher.HasShutdownStarted) return;
            long ms = asked.ElapsedMilliseconds;
            if (DateTime.Now - lastLine < Quiet)
            {
                unlogged++;
                worstUnlogged = Math.Max(worstUnlogged, ms);
                continue;
            }
            Log.Info($"The UI thread was held up {ms} ms"
                     + (unlogged > 0 ? $" (and {unlogged} more times since the last note, up to {worstUnlogged} ms)" : ""));
            lastLine = DateTime.Now;
            unlogged = 0;
            worstUnlogged = 0;
        }
    }
}
