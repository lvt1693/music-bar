using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>
/// A stretch of the taskbar along its length, in physical screen pixels: x on a
/// horizontal taskbar, y (top to bottom) on a vertical one.
/// </summary>
internal readonly record struct Span(int Start, int End)
{
    public int Width => End - Start;
}

/// <summary>The screen edge the taskbar is docked to.</summary>
internal enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>
/// Follows the Windows taskbar (Shell_TrayWnd): its rectangle, whether it is
/// on screen (auto-hide, fullscreen), and which parts of it are occupied by
/// the Start button, app icons and the notification area, so the widget can
/// sit in the empty space. Positions along the taskbar are <see cref="Span"/>s,
/// so the same layout works for a taskbar docked to any edge.
/// </summary>
internal sealed class TaskbarTracker : IDisposable
{
    static readonly TimeSpan SlowTick = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan FastTick = TimeSpan.FromMilliseconds(30);
    static readonly TimeSpan ScanEvery = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan PartialRetry = TimeSpan.FromSeconds(0.5);
    static readonly TimeSpan AutoHideCheckEvery = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Containers, never icons themselves. The notification area's (TrayNotifyWnd): its visible children
    /// cover the same ground, and when the control center folds tray items away its left part is actually
    /// empty. The old task band's (ReBarWindow32, MSTaskSwWClass): their buttons are listed on their own,
    /// and Windows 11 leaves an invisible MSTaskSwWClass at the taskbar's left end, about 90 px wide, which
    /// kept the bar off the screen's edge when nothing is shown there.
    /// </summary>
    static readonly HashSet<string> Containers = ["TrayNotifyWnd", "ReBarWindow32", "MSTaskSwWClass"];

    readonly DispatcherTimer _poll = new() { Interval = SlowTick };
    readonly Native.WinEventProc _foregroundProc;
    readonly Native.WinEventProc _moveProc;
    IntPtr _hook;
    IntPtr _moveHook;
    DateTime _fastUntil;
    DateTime _lastScan = DateTime.MinValue;
    DateTime _autoHideCheckedAt = DateTime.MinValue;
    bool _scanning;
    bool _checkingAutoHide;
    bool _ticking;
    int _partialScans;
    // A scan of this taskbar has found its Start button: from then on, a scan without it is a fragment.
    bool _startSeen;

    // The monitor (device name) whose secondary taskbar this follows; null follows the main taskbar.
    readonly string? _monitorDevice;

    public TaskbarTracker(string? monitorDevice = null)
    {
        _monitorDevice = monitorDevice;
        _foregroundProc = (_, _, _, _, _, _, _) => ZOrderCheck?.Invoke();
        _moveProc = (_, _, hwnd, idObject, idChild, _, _) =>
        {
            if (hwnd == Handle && idObject == 0 /* OBJID_WINDOW */ && idChild == 0) Tick();
        };
        _poll.Tick += (_, _) => Tick();
    }

    public IntPtr Handle { get; private set; }
    public Native.RECT Rect { get; private set; }
    public MonitorInfo? Monitor { get; private set; }
    public bool Shown { get; private set; }
    public IReadOnlyList<Span> Occupied { get; private set; } = [];

    /// <summary>The taskbar is set to hide itself, sliding off the screen's edge until the pointer comes.</summary>
    public bool AutoHides { get; private set; }

    /// <summary>Out of sight only because it hid itself: it slides back when the pointer reaches the edge.</summary>
    public bool SlidAway => AutoHides && !Shown && Handle != IntPtr.Zero;

    /// <summary>
    /// Windows 11's tray buttons that open Quick Settings (network, volume,
    /// battery), as one span; null on Windows 10 style taskbars.
    /// </summary>
    public Span? QuickSettings { get; private set; }

    /// <summary>At least one UI Automation scan finished for the current taskbar.</summary>
    public bool HasScanned { get; private set; }

    public bool IsHorizontal => Rect.Width >= Rect.Height;

    public bool AtTop => Edge == TaskbarEdge.Top;

    public TaskbarEdge Edge
    {
        get
        {
            if (Monitor == null) return IsHorizontal ? TaskbarEdge.Bottom : TaskbarEdge.Left;
            var b = Monitor.Bounds;
            if (IsHorizontal) return Rect.Top <= b.Top + 2 && Rect.Bottom < b.Bottom - 2 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
            return Rect.Left <= b.Left + 2 && Rect.Right < b.Right - 2 ? TaskbarEdge.Left : TaskbarEdge.Right;
        }
    }

    /// <summary>Where the taskbar begins along its length (its left, or its top when vertical).</summary>
    public int Begin => IsHorizontal ? Rect.Left : Rect.Top;

    public int End => IsHorizontal ? Rect.Right : Rect.Bottom;

    public int Length => End - Begin;

    /// <summary>The taskbar's short side, in physical pixels.</summary>
    public int Thickness => IsHorizontal ? Rect.Height : Rect.Width;

    public double Scale => Monitor?.Scale ?? 1;

    /// <summary>Rectangle, visibility or occupied areas changed.</summary>
    public event Action? LayoutChanged;

    /// <summary>Something may have reordered windows; overlays should re-assert their z-order.</summary>
    public event Action? ZOrderCheck;

    public void Start()
    {
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        _poll.Start();
        Tick();
    }

    /// <summary>Something rearranged the taskbar's items: look again on the next tick.</summary>
    public void Rescan() => _lastScan = DateTime.MinValue;

    public void Dispose()
    {
        _poll.Stop();
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        if (_moveHook != IntPtr.Zero) Native.UnhookWinEvent(_moveHook);
        _moveHook = IntPtr.Zero;
    }

    void Tick()
    {
        // Moves of the taskbar are heard as they happen, and could arrive while a tick is placing windows.
        if (_ticking) return;
        _ticking = true;
        try
        {
            Follow();
        }
        finally
        {
            _ticking = false;
        }
    }

    void Follow()
    {
        bool changed = false;
        // A secondary taskbar can end up on another monitor when screens are rearranged: find ours again.
        bool lost = Handle == IntPtr.Zero || !Native.IsWindow(Handle)
                    || (_monitorDevice != null && Monitor != null && Monitor.DeviceName != _monitorDevice);
        if (lost)
        {
            // The main taskbar's window died: Explorer restarted, perhaps after crashing.
            if (_monitorDevice == null && Handle != IntPtr.Zero && !Native.IsWindow(Handle)) CrashReport.TaskbarRestarted();
            Handle = _monitorDevice == null ? Native.FindWindow("Shell_TrayWnd", null) : Monitors.SecondaryTaskbarOn(_monitorDevice);
            Rect = default;
            Occupied = [];
            HasScanned = false;
            _startSeen = false;
            changed = true;
            HearMoves();
        }

        if (Handle == IntPtr.Zero)
        {
            if (Shown)
            {
                Shown = false;
                LayoutChanged?.Invoke();
            }
            return;
        }

        if (Native.GetWindowRect(Handle, out var rc) && !rc.Same(Rect))
        {
            Rect = rc;
            Monitor = Monitors.Get(Native.MonitorFromWindow(Handle, Native.MONITOR_DEFAULTTONEAREST));
            _fastUntil = DateTime.Now.AddSeconds(1);
            changed = true;
        }

        bool shown = Native.IsWindowVisible(Handle) && VisibleThickness() >= 6;
        if (shown != Shown)
        {
            Shown = shown;
            changed = true;
        }

        // Poll fast while the taskbar is sliding (auto-hide), in case its moves aren't heard.
        _poll.Interval = DateTime.Now < _fastUntil ? FastTick : SlowTick;

        if (!_checkingAutoHide && DateTime.Now - _autoHideCheckedAt > AutoHideCheckEvery) CheckAutoHide();
        if (changed) LayoutChanged?.Invoke();
        // Slid off the screen, the taskbar's buttons all read as off screen: there is nothing to learn until it
        // is back, and halfway there only some of them would be found.
        bool whole = VisibleThickness() >= Thickness;
        if (!_scanning && Shown && (changed && whole || DateTime.Now - _lastScan > ScanEvery)) Scan();
        ZOrderCheck?.Invoke();
    }

    /// <summary>
    /// Hears the taskbar move as it moves. An auto-hiding taskbar slides in a fifth of a second:
    /// polled, the bar would pop in once the taskbar is already up and trail it on the way down.
    /// </summary>
    void HearMoves()
    {
        if (_moveHook != IntPtr.Zero) Native.UnhookWinEvent(_moveHook);
        _moveHook = IntPtr.Zero;
        if (Handle == IntPtr.Zero) return;
        uint thread = Native.GetWindowThreadProcessId(Handle, out uint pid);
        _moveHook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _moveProc, pid, thread, Native.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Whether the taskbar hides itself. Asking Explorer could wait on it, so that happens off the UI thread.</summary>
    async void CheckAutoHide()
    {
        _checkingAutoHide = true;
        _autoHideCheckedAt = DateTime.Now;
        try
        {
            bool autoHides = await Task.Run(Native.TaskbarAutoHides);
            if (autoHides == AutoHides) return;
            AutoHides = autoHides;
            if (_monitorDevice == null)
                Log.Info(autoHides ? "The taskbar hides itself: the bar comes and goes with it" : "The taskbar stays put (auto-hide off)");
            LayoutChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Asking whether the taskbar hides itself failed", ex);
        }
        finally
        {
            _checkingAutoHide = false;
        }
    }

    int VisibleThickness()
    {
        if (Monitor == null) return Rect.Height;
        var b = Monitor.Bounds;
        int h = Math.Min(Rect.Bottom, b.Bottom) - Math.Max(Rect.Top, b.Top);
        int w = Math.Min(Rect.Right, b.Right) - Math.Max(Rect.Left, b.Left);
        return Math.Min(h, w);
    }

    async void Scan()
    {
        _scanning = true;
        _lastScan = DateTime.Now;
        var hwnd = Handle;
        var rc = Rect;
        bool horizontal = IsHorizontal;
        try
        {
            var (occupied, quickSettings, hasStart) = await Task.Run(() => FindOccupied(hwnd, rc, horizontal));
            // A taskbar sliding in or out moves across its length only: what was found along it still holds.
            if (hwnd != Handle || !SameAlong(rc, Rect))
            {
                _lastScan = DateTime.MinValue;
                return;
            }

            // UI Automation on the taskbar sometimes answers with nothing, or with a fragment: at
            // startup, and for as long as Windows' Quick Settings is open. The notification area
            // always ends the taskbar (its right, or its bottom when vertical), so a scan without it
            // is not the taskbar's layout. Once a layout with it is known, keep that one until a scan
            // finds the tray again; before that (startup, Explorer restarting), look again every half
            // second and settle for what there is after about five (a secondary taskbar may have no
            // clock at its end). The same goes for a scan that lost the Start button once it has been seen:
            // while Windows' Search opens from hovering the search box, or UI Automation times out and only
            // the old-style child windows answer, the Start and Search buttons drop out, and laying out on
            // that put the lyrics over them (#17).
            if (hasStart) _startSeen = true;
            bool lostStart = _startSeen && !hasStart;
            if (!Complete(occupied) || lostStart)
            {
                if (++_partialScans == 1)
                    Log.Info($"Taskbar scan without the {(lostStart ? "Start button" : "tray")} ignored: [{Describe(occupied)}]");
                if (HasScanned && Complete(Occupied)) return;
                if (_partialScans < 10)
                {
                    _lastScan = DateTime.Now - ScanEvery + PartialRetry;
                    return;
                }
            }
            else
            {
                _partialScans = 0;
            }

            if (quickSettings != QuickSettings && (quickSettings != null || occupied.Count > 0))
            {
                QuickSettings = quickSettings;
                LayoutChanged?.Invoke();
            }

            if (!HasScanned || !occupied.SequenceEqual(Occupied))
            {
                Occupied = occupied;
                HasScanned = true;
                LayoutChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Scanning the taskbar failed", ex);
        }
        finally
        {
            _scanning = false;
        }
    }

    static bool SameAlong(Native.RECT a, Native.RECT b) => a.Width >= a.Height == b.Width >= b.Height
        && (a.Width >= a.Height
            ? a.Left == b.Left && a.Right == b.Right && a.Height == b.Height
            : a.Top == b.Top && a.Bottom == b.Bottom && a.Width == b.Width);

    /// <summary>The spans reach the far end of the taskbar, where the notification area is.</summary>
    bool Complete(IEnumerable<Span> spans) => spans.Any(s => s.End >= End - Length / 20);

    public static string Describe(IEnumerable<Span> spans) => string.Join(", ", spans.Select(o => $"{o.Start}-{o.End}"));

    /// <summary>A screen rectangle as a stretch along the taskbar, and across it.</summary>
    readonly record struct Along(double Start, double End, double CrossStart, double CrossEnd)
    {
        public double Length => End - Start;

        public static Along Of(System.Windows.Rect r, bool horizontal) => horizontal
            ? new Along(r.Left, r.Right, r.Top, r.Bottom)
            : new Along(r.Top, r.Bottom, r.Left, r.Right);

        public static Along Of(Native.RECT r, bool horizontal) => horizontal
            ? new Along(r.Left, r.Right, r.Top, r.Bottom)
            : new Along(r.Top, r.Bottom, r.Left, r.Right);

        public Span ToSpan() => new((int)Math.Floor(Start), (int)Math.Ceiling(End));
    }

    /// <summary>
    /// Walks the taskbar's UI Automation tree and collects the stretches along
    /// it covered by real controls. Containers that span most of the bar are
    /// ignored. When icons are centered, the group is assumed symmetric around
    /// the middle, starting at the Start button.
    /// </summary>
    static (List<Span> Occupied, Span? QuickSettings, bool HasStart) FindOccupied(IntPtr tray, Native.RECT rc, bool horizontal)
    {
        var bar = Along.Of(rc, horizontal);
        var spans = new List<Span>();
        var quick = new List<Span>();
        var power = new List<Span>();
        double? startAt = null;
        try
        {
            CollectFromAutomation(tray, bar, horizontal, spans, quick, power, ref startAt);
        }
        catch (Exception ex)
        {
            Log.Error("UI Automation scan of the taskbar failed", ex);
        }
        if (spans.Count == 0) CollectFromChildWindows(tray, bar, horizontal, spans, ref startAt);

        if (startAt is double start && start - bar.Start > bar.Length * 0.1)
            spans.Add(new Span((int)start, (int)(bar.Start + bar.End - start)));

        return (Merge(spans), QuickSettingsSpan(quick, power), startAt.HasValue);
    }

    /// <summary>
    /// Windows 11 tray buttons that open Quick Settings, recognised by their
    /// accessible names (English and Vietnamese Windows).
    /// </summary>
    static readonly string[] QuickSettingsNames =
        ["Network", "Wi-Fi", "Ethernet", "Volume", "Speakers", "Headphones", "Battery", "Mạng", "Âm lượng", "Loa", "Pin"];

    static bool IsQuickSettingsButton(string cls, string name) =>
        cls.StartsWith("SystemTray.", StringComparison.Ordinal)
        && QuickSettingsNames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Battery and power words in a few languages. A tray button naming one of them, right next to the
    /// Quick Settings buttons, is the battery (its name differs between Windows builds and languages);
    /// anywhere else it is some app's icon and stays out.
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex PowerWords = new(
        @"\b(battery|batterie|bateria|batería|akku|power|charging|plugged|pin|sạc|nguồn)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The Quick Settings buttons as one span, grown over battery buttons that sit right beside them.</summary>
    static Span? QuickSettingsSpan(List<Span> quick, List<Span> power)
    {
        if (quick.Count == 0) return null;
        int start = quick.Min(q => q.Start), end = quick.Max(q => q.End);
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var p in power)
            {
                if (p.Start >= start && p.End <= end) continue;
                if (p.End < start - 6 || p.Start > end + 6) continue;
                start = Math.Min(start, p.Start);
                end = Math.Max(end, p.End);
                grew = true;
            }
        }
        return new Span(start, end);
    }

    /// <summary>
    /// Fallback for when UI Automation returns nothing: the Windows 10 style
    /// taskbar's own child windows (Start button, notification area, ...).
    /// </summary>
    static void CollectFromChildWindows(IntPtr tray, Along bar, bool horizontal, List<Span> spans, ref double? startAt)
    {
        var found = new List<(string Class, Native.RECT Rect)>();
        Native.EnumWindowsProc callback = (h, _) =>
        {
            if (Native.IsWindowVisible(h) && Native.GetWindowRect(h, out var r)) found.Add((Native.ClassName(h), r));
            return true;
        };
        Native.EnumChildWindows(tray, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        foreach (var (cls, r) in found)
        {
            if (Containers.Contains(cls)) continue;
            var a = Along.Of(r, horizontal);
            if (r.Width < 2 || r.Height < 2 || a.Length > bar.Length * 0.3) continue;
            if (a.CrossEnd <= bar.CrossStart || a.CrossStart >= bar.CrossEnd) continue;
            if (cls == "Start") startAt = startAt.HasValue ? Math.Min(startAt.Value, a.Start) : a.Start;
            spans.Add(a.ToSpan());
        }
    }

    static void CollectFromAutomation(IntPtr tray, Along bar, bool horizontal, List<Span> spans, List<Span> quick, List<Span> power, ref double? startAt)
    {
        // The managed UI Automation client only where Windows' COM one can't be had: it keeps memory from every scan.
        var controls = TaskbarAutomation.Works ? TaskbarAutomation.Read(tray) : ReadManaged(tray);

        foreach (var c in controls)
        {
            if (c.Offscreen || c.Bounds.Width <= 0 || c.Bounds.Height <= 0) continue;
            var b = new System.Windows.Rect(c.Bounds.Left, c.Bounds.Top, c.Bounds.Width, c.Bounds.Height);
            string cls = c.ClassName, id = c.AutomationId, name = c.Name;
            var a = Along.Of(b, horizontal);
            if (IsQuickSettingsButton(cls, name)) quick.Add(a.ToSpan());
            else if (cls.StartsWith("SystemTray.", StringComparison.Ordinal) && PowerWords.IsMatch(name)) power.Add(a.ToSpan());

            if (b.Width < 2 || b.Height < 2) continue;
            if (a.Length > bar.Length * 0.3 || Containers.Contains(cls)) continue;
            if (a.CrossEnd <= bar.CrossStart || a.CrossStart >= bar.CrossEnd) continue;

            if (cls == "Start" || id == "StartButton")
                startAt = startAt.HasValue ? Math.Min(startAt.Value, a.Start) : a.Start;
            spans.Add(a.ToSpan());
        }
    }

    static List<TaskbarAutomation.Control> ReadManaged(IntPtr tray)
    {
        var controls = new List<TaskbarAutomation.Control>();
        foreach (AutomationElement e in AutomationElement.FromHandle(tray).FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            try
            {
                var c = e.Current;
                var b = c.BoundingRectangle;
                var bounds = b.IsEmpty ? default : new Native.RECT((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom);
                controls.Add(new TaskbarAutomation.Control(bounds, c.IsOffscreen, c.ClassName ?? "", c.AutomationId ?? "", c.Name ?? ""));
            }
            catch (Exception)
            {
                // The control went away while being read.
            }
        }
        return controls;
    }

    static List<Span> Merge(List<Span> spans)
    {
        var merged = new List<Span>();
        foreach (var s in spans.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && s.Start <= merged[^1].End + 2)
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, s.End) };
            else
                merged.Add(s);
        }
        return merged;
    }

    /// <summary>Empty stretches of the taskbar.</summary>
    public List<Span> FreeSpans()
    {
        var free = new List<Span>();
        int at = Begin, end = End;
        foreach (var o in Occupied)
        {
            if (o.End <= Begin || o.Start >= end) continue;
            if (o.Start > at) free.Add(new Span(at, o.Start));
            at = Math.Max(at, o.End);
        }
        if (at < end) free.Add(new Span(at, end));
        return free;
    }
}
