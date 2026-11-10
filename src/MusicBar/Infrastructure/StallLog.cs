using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MusicBar.Infrastructure;

/// <summary>
/// Notes in the log when the UI thread was held up long enough for the bar and the lyrics to stop moving
/// visibly. A stutter "every 7 or 8 seconds" (#17) left nothing in a log sent in; now the times show, and how
/// regular they are points at what did it. A watcher thread asks the UI thread to answer four times a second.
/// The line also says what held it up, so a log sent in points at the code: the dispatcher's hooks keep what the
/// UI thread is running where the watcher can see it, and the watcher looks there while an answer is late.
/// </summary>
public static class StallLog
{
    const int CheckEveryMs = 250;
    const int StallMs = 400;
    // While an answer is late, the watcher looks this often at what the UI thread is running.
    const int LookEveryMs = 50;
    // Woken this much later than it asked to be, the watcher did not run at all, and neither did the UI thread: the PC
    // slept or froze, or the process was paused. That time is not MusicBar's doing and is not counted.
    const int PausedMs = 1000;
    // At most a line this often; the ones in between are counted into the next.
    static readonly TimeSpan Quiet = TimeSpan.FromSeconds(20);

    const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Outside = "outside any dispatcher operation (a window message, such as input or a window hook)";

    // What the UI thread is running: written by the dispatcher's hooks on that thread, read by the watcher on its own.
    // Operations nest (a modal dialog, an Invoke waiting on the same thread), so it is a small stack. One array write
    // per operation and no allocation; a read that races a write can at worst put the wrong name on a line.
    const int Deepest = 16;
    static readonly DispatcherOperation?[] Running = new DispatcherOperation?[Deepest];
    static int _depth;
    static long _answeredAt;

    // WPF's private fields the names are read from. One renamed in a later WPF gives a vaguer name, never an error.
    static readonly FieldInfo? OperationMethod = typeof(DispatcherOperation).GetField("_method", AnyInstance);
    static readonly FieldInfo? OperationArgs = typeof(DispatcherOperation).GetField("_args", AnyInstance);
    static readonly FieldInfo? TimerTick = typeof(DispatcherTimer).GetField("Tick", AnyInstance);
    static readonly FieldInfo? TaskAction = typeof(Task).GetField("m_action", AnyInstance);
    // The IL opcodes by their bytes, for reading what a lambda calls; built on the first stall, by the watcher only.
    static OpCode[]? _oneByte, _twoByte;

    public static void Start(Dispatcher dispatcher)
    {
        // Never unhooked: they live as long as the dispatcher and hold only what is running at the moment.
        var hooks = dispatcher.Hooks;
        hooks.OperationStarted += OnStarted;
        hooks.OperationCompleted += OnCompleted;
        // Above normal: a watcher starved by a busy PC would take its own lateness for the PC freezing. It runs for a
        // few microseconds four times a second, which takes nothing from anyone.
        new Thread(() => Guarded(dispatcher)) { IsBackground = true, Name = "MusicBar.StallLog", Priority = ThreadPriority.AboveNormal }.Start();
    }

    /// <summary>The watcher is a help, never a need: whatever goes wrong in it ends the watching, not MusicBar.</summary>
    static void Guarded(Dispatcher dispatcher)
    {
        try
        {
            Watch(dispatcher);
        }
        catch (Exception ex)
        {
            Log.Error("Watching the UI thread for hold-ups stopped", ex);
        }
    }

    static void OnStarted(object? sender, DispatcherHookEventArgs e)
    {
        int depth = _depth;
        if (depth < Deepest) Running[depth] = e.Operation;
        Volatile.Write(ref _depth, depth + 1);
    }

    static void OnCompleted(object? sender, DispatcherHookEventArgs e)
    {
        int depth = _depth;
        if (depth > Deepest)
        {
            Volatile.Write(ref _depth, depth - 1);
            return;
        }
        // Back to where this operation was put: a nested one whose end an exception skipped goes with it. One that
        // never ran is not on the stack and changes nothing.
        for (int i = depth - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(Running[i], e.Operation)) continue;
            for (int j = i; j < depth; j++) Running[j] = null;
            Volatile.Write(ref _depth, i);
            return;
        }
    }

    static void Watch(Dispatcher dispatcher)
    {
        var lastLine = DateTime.MinValue;
        int unlogged = 0;
        long worstUnlogged = 0;
        string worstWhere = "";
        var seen = new List<(DispatcherOperation? Op, string? Name, int Times)>();
        using var answered = new ManualResetEventSlim();
        // The UI thread notes when it answered, so a watcher woken late does not stretch the stall.
        Action answer = () =>
        {
            Volatile.Write(ref _answeredAt, Stopwatch.GetTimestamp());
            answered.Set();
        };
        while (!dispatcher.HasShutdownStarted)
        {
            Thread.Sleep(CheckEveryMs);
            answered.Reset();
            seen.Clear();
            long asked = Stopwatch.GetTimestamp(), paused = 0;
            bool held = false, locked = false;
            // Input priority: ahead of timers and data binding, behind what is already running.
            dispatcher.BeginInvoke(DispatcherPriority.Input, answer);
            while (true)
            {
                long waiting = Stopwatch.GetTimestamp();
                bool done = answered.Wait(LookEveryMs);
                long now = Stopwatch.GetTimestamp();
                // Only the wait counts, and only up to the answer: the watcher's own looking is not the PC freezing,
                // and a sleep after the UI thread answered held nothing up. So paused never exceeds the stall.
                long late = Ms((done ? Math.Min(now, Volatile.Read(ref _answeredAt)) : now) - waiting) - LookEveryMs;
                if (late >= PausedMs) paused += late;
                if (done) break;
                if (dispatcher.HasShutdownStarted) return;
                Look(seen);
                if (!held && Ms(now - asked) - paused >= StallMs)
                {
                    held = true;
                    locked = SessionLocked();
                }
                // Named once the wait is a stall, while the operation still runs: a task lets go of its work once done.
                if (held) NameAll(seen);
            }
            long ms = Ms(Volatile.Read(ref _answeredAt) - asked) - paused;
            if (ms < StallMs) continue;
            // Behind the lock screen nobody saw it; asked when it began and when it ended, as either may be the lock's.
            string where = Where(seen) + (locked || SessionLocked() ? ", while Windows was locked" : "")
                           + (paused > 0 ? $"; MusicBar as a whole stood still {paused} ms more (the PC slept or froze), not counted" : "");
            seen.Clear();
            if (DateTime.Now - lastLine < Quiet)
            {
                unlogged++;
                if (ms > worstUnlogged)
                {
                    worstUnlogged = ms;
                    worstWhere = where;
                }
                continue;
            }
            Log.Info($"The UI thread was held up {ms} ms {where}"
                     + (unlogged > 0 ? $" (and {unlogged} more times since the last note, up to {worstUnlogged} ms {worstWhere})" : ""));
            lastLine = DateTime.Now;
            unlogged = 0;
            worstUnlogged = 0;
            worstWhere = "";
        }
    }

    static long Ms(long ticks) => ticks * 1000 / Stopwatch.Frequency;

    /// <summary>What the UI thread runs right now, merged with the last look while it is the same operation.</summary>
    static void Look(List<(DispatcherOperation? Op, string? Name, int Times)> seen)
    {
        int depth = Math.Min(Volatile.Read(ref _depth), Deepest);
        var op = depth > 0 ? Running[depth - 1] : null;
        if (seen.Count > 0 && ReferenceEquals(seen[^1].Op, op)) seen[^1] = seen[^1] with { Times = seen[^1].Times + 1 };
        else seen.Add((op, null, 1));
    }

    /// <summary>Names what was seen and is not named yet. Only ever called once a wait has turned out to be a stall.</summary>
    static void NameAll(List<(DispatcherOperation? Op, string? Name, int Times)> seen)
    {
        for (int i = 0; i < seen.Count; i++)
            if (seen[i].Name == null) seen[i] = seen[i] with { Name = seen[i].Op is { } op ? "in " + Name(op) : Outside };
    }

    /// <summary>
    /// "in MediaService.Poll (timer)" when one thing held the thread, or what took turns, the one seen longest first.
    /// Something seen only once is left out next to the rest: between two operations the thread is briefly in none.
    /// </summary>
    static string Where(List<(DispatcherOperation? Op, string? Name, int Times)> seen)
    {
        if (seen.Count == 0) return "(nothing seen running: the watcher was late too)";
        NameAll(seen);
        var times = new Dictionary<string, int>();
        foreach (var (_, name, n) in seen) times[name!] = times.GetValueOrDefault(name!) + n;
        var ranked = times.OrderByDescending(t => t.Value).ToList();
        var also = ranked.Skip(1).Where(t => t.Value > 1).Select(t => t.Key).ToList();
        if (also.Count == 0) return ranked[0].Key;
        return $"mostly {ranked[0].Key}, also {string.Join(" and ", also.Take(2))}" + (also.Count > 2 ? $" and {also.Count - 2} more" : "");
    }

    /// <summary>
    /// The app's method behind an operation. The delegate an operation runs is often the framework's own (a timer's, an
    /// await's) with the app's in its argument, so those are looked through.
    /// </summary>
    static string Name(DispatcherOperation op)
    {
        try
        {
            var notes = new List<string>();
            var args = OperationArgs?.GetValue(op);
            // A tick runs the timer's own callback with the timer as its argument: its Tick handlers are what ran.
            string? name = args is DispatcherTimer timer
                ? Handlers(timer, notes)
                : Name(OperationMethod?.GetValue(op) as Delegate, args, notes, 0);
            if (name != null) return notes.Count == 0 ? name : $"{name} ({string.Join(", ", notes.Distinct())})";
        }
        catch
        {
            // The name is a help, not a need: the line goes out with a vaguer one.
        }
        return $"an operation at {op.Priority} priority";
    }

    static string? Handlers(DispatcherTimer timer, List<string> notes)
    {
        notes.Add("timer");
        if (TimerTick?.GetValue(timer) is not EventHandler tick) return null;
        return string.Join(" + ", tick.GetInvocationList().Select(h => Name(h, null, notes, 0)).Distinct());
    }

    static string? Name(Delegate? work, object? args, List<string> notes, int depth)
    {
        if (work == null || depth > 3) return null;
        var method = work.Method;
        if (method.DeclaringType?.Assembly == typeof(StallLog).Assembly) return Describe(method.DeclaringType, method.Name, method, notes);
        var target = work.Target;
        // An await's continuation runs the framework's MoveNext; the async method is the state machine in the box's type.
        if (target != null && target.GetType().IsGenericType
            && target.GetType().GetGenericArguments().FirstOrDefault(typeof(IAsyncStateMachine).IsAssignableFrom) is { } machine)
        {
            notes.Add("after an await");
            return Describe(machine.DeclaringType, machine.Name, machine.GetMethod("MoveNext", AnyInstance), notes);
        }
        if (target != null && target.GetType().FullName == "System.Windows.Media.MediaContext") return Rendering(target, notes);
        // Posted through the synchronization context or a task scheduler: what runs is the argument.
        var inner = args as Delegate ?? (args is Task task ? TaskAction?.GetValue(task) as Delegate : null);
        if (Name(inner, null, notes, depth + 1) is { } name) return name;
        notes.Add("framework");
        return $"{TypeName(method.DeclaringType)}.{Member(method.Name, out _)}";
    }

    /// <summary>WPF's layout and drawing, which also run whatever the app hooked to every frame.</summary>
    static string Rendering(object mediaContext, List<string> notes)
    {
        if (mediaContext.GetType().GetField("Rendering", AnyInstance)?.GetValue(mediaContext) is EventHandler frame)
        {
            var own = frame.GetInvocationList()
                .Where(h => h.Method.DeclaringType?.Assembly == typeof(StallLog).Assembly)
                .Select(h => Describe(h.Method.DeclaringType, h.Method.Name, h.Method, new List<string>()))
                .Distinct().ToList();
            if (own.Count > 0) notes.Add("each frame also runs " + string.Join(" + ", own));
        }
        return "WPF rendering and layout";
    }

    /// <summary>
    /// Type.Method with the compiler's names undone. A lambda's own name says little, so it goes by the app's methods
    /// it calls, read from its IL, and where it was written becomes a note.
    /// </summary>
    static string Describe(Type? owner, string member, MethodInfo? body, List<string> notes)
    {
        string name = $"{TypeName(owner)}.{Member(member, out bool lambda)}";
        if (!lambda) return name;
        var calls = body == null ? new List<string>() : Calls(body);
        if (calls.Count == 0) return $"a lambda in {name}";
        notes.Add($"lambda in {name}");
        return string.Join(" + ", calls.Take(3)) + (calls.Count > 3 ? " and more" : "");
    }

    /// <summary>The app's methods a method calls, in order, read from its IL; anything unreadable is left out.</summary>
    static List<string> Calls(MethodInfo method)
    {
        var calls = new List<string>();
        // An async lambda's body is its state machine's.
        if (method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType.GetMethod("MoveNext", AnyInstance) is { } moveNext)
            method = moveNext;
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return calls;
        if (_oneByte == null || _twoByte == null)
        {
            var one = new OpCode[256];
            var two = new OpCode[256];
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode code) (code.Size == 1 ? one : two)[code.Value & 0xFF] = code;
            _oneByte = one;
            _twoByte = two;
        }
        var typeArgs = method.DeclaringType is { IsGenericType: true } generic ? generic.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (int at = 0; at < il.Length;)
        {
            var code = il[at] == 0xFE && at + 1 < il.Length ? _twoByte[il[++at]] : _oneByte[il[at]];
            at++;
            // Not an opcode: stop rather than read operands as code.
            if (code.Size == 0) break;
            int operand = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => at + 4 <= il.Length ? 4 + 4 * Math.Clamp(BitConverter.ToInt32(il, at), 0, il.Length) : il.Length,
                _ => 4,
            };
            if ((code.Value == OpCodes.Call.Value || code.Value == OpCodes.Callvirt.Value) && at + 4 <= il.Length)
            {
                try
                {
                    if (method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArgs, methodArgs) is MethodInfo callee
                        && callee.DeclaringType?.Assembly == typeof(StallLog).Assembly
                        // Getters and event wiring are rarely what holds a thread, and would crowd out what does.
                        && !(callee.IsSpecialName && (callee.Name.StartsWith("get_") || callee.Name.StartsWith("add_") || callee.Name.StartsWith("remove_"))))
                    {
                        string name = $"{TypeName(callee.DeclaringType)}.{Member(callee.Name, out _)}";
                        if (!calls.Contains(name)) calls.Add(name);
                    }
                }
                catch
                {
                    // A call this cannot resolve is left out.
                }
            }
            at += operand;
        }
        return calls;
    }

    static string TypeName(Type? type)
    {
        // Closures and state machines are the compiler's: the type that wrote them is the one meant.
        while (type != null && type.Name.StartsWith('<')) type = type.DeclaringType;
        if (type == null) return "?";
        int tick = type.Name.IndexOf('`');
        string name = tick > 0 ? type.Name[..tick] : type.Name;
        return type.DeclaringType == null ? name : $"{TypeName(type.DeclaringType)}.{name}";
    }

    /// <summary>
    /// A member's name with the compiler's undone: &lt;Load&gt;d__12 is async Load's state machine, &lt;Load&gt;b__12_0 a
    /// lambda in Load, &lt;Load&gt;g__Parse|12_0 a local function Parse in Load, &lt;&lt;Load&gt;b__12_0&gt;d an async lambda's.
    /// </summary>
    static string Member(string name, out bool lambda)
    {
        lambda = false;
        while (name.StartsWith('<'))
        {
            int depth = 0, end = -1;
            for (int i = 0; i < name.Length && end < 0; i++)
            {
                if (name[i] == '<') depth++;
                else if (name[i] == '>' && --depth == 0) end = i;
            }
            if (end < 0) break;
            string kind = name[(end + 1)..];
            name = name[1..end];
            if (kind.StartsWith("b__")) lambda = true;
            else if (kind.StartsWith("g__"))
            {
                int bar = kind.IndexOf('|');
                return Member(name, out _) + "." + (bar > 3 ? kind[3..bar] : kind[3..]);
            }
        }
        // .ctor and .cctor read better without the dot.
        return name.TrimStart('.');
    }

    /// <summary>Whether Windows shows its lock screen. Asked only on a stall; any failure is "not locked".</summary>
    static bool SessionLocked()
    {
        try
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, WtsCurrentSession, WtsSessionInfoEx, out var info, out int bytes)) return false;
            try
            {
                // WTSINFOEX: Level (1), then WTSINFOEX_LEVEL1 at 8 (it holds 64-bit fields): SessionId, SessionState,
                // SessionFlags, where 0 is locked. Windows 7 had the flag the wrong way round; MusicBar needs Windows 10.
                return bytes >= 20 && Marshal.ReadInt32(info, 0) == 1 && Marshal.ReadInt32(info, 16) == WtsSessionStateLock;
            }
            finally
            {
                WTSFreeMemory(info);
            }
        }
        catch
        {
            return false;
        }
    }

    const int WtsCurrentSession = -1;
    const int WtsSessionInfoEx = 25;
    const int WtsSessionStateLock = 0;

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr memory);
}
