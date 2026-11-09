using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>
/// Makes Windows 11 taskbars see-through. Their background is a XAML element
/// inside Explorer, so MusicBar gets a small helper (MusicBar.TaskbarTap.dll)
/// loaded there: a hook on the taskbar's thread brings the DLL into Explorer,
/// where it attaches to Explorer's XAML through the public diagnostics API and
/// stays (the approach TranslucentTB takes). The helper clears exactly the
/// taskbars listed in a small shared-memory block; if MusicBar exits or crashes,
/// the block goes away and every taskbar gets its own background back (and
/// Windows its own light or dark mode, when MusicBar had changed it).
/// </summary>
internal static class XamlTaskbar
{
    // Must match the helper (TaskbarTap.cpp): a count, the Windows mode to put back, then up to 16 window handles.
    const string TargetsName = @"Local\MusicBar.TaskbarClear.v2";
    const int MaxTargets = 16;
    // Must match the helper; bumped whenever the helper changes, so a newer one gets loaded.
    const int HelperVersion = 13;
    static readonly string ReadyPrefix = $@"Local\MusicBar.TaskbarTap.Ready.v{HelperVersion}.";
    static readonly string WatchingPrefix = $@"Local\MusicBar.TaskbarTap.Watching.v{HelperVersion}.";
    static readonly string ClearedPrefix = $@"Local\MusicBar.TaskbarTap.Cleared.v{HelperVersion}.";
    // From v3 (MusicBar 1.2) on, a helper steps aside when a newer one asks, so updating needs no Explorer restart.
    const int FirstRetiringVersion = 3;
    // The helper of MusicBar 1.0-1.1 keeps Explorer's XAML tree until Explorer restarts, so a
    // newer helper loaded beside it sees nothing. It only knows "clear every taskbar, or none".
    const string LegacyReadyPrefix = @"Local\MusicBar.TaskbarTap.Ready.v2.";
    const string LegacyWantEvent = @"Local\MusicBar.TaskbarClear.v1";
    static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(40);
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(35);

    // Each overlay asks for its own taskbar; the helper gets the union.
    static readonly Dictionary<object, IntPtr> _wanted = [];
    static MemoryMappedFile? _targets;
    static MemoryMappedViewAccessor? _view;
    static string _published = "";
    static EventWaitHandle? _legacyWant;
    static bool _legacy;
    static DateTime _checkedAt = DateTime.MinValue;
    static uint _loadedIn;

    /// <summary>
    /// Only the older helper reaches the taskbar, and there is more than one taskbar: it would
    /// clear every monitor's, so none is cleared until Explorer restarts.
    /// </summary>
    public static bool Blocked { get; private set; }

    /// <summary>When the helper was last found or loaded in an Explorer (MinValue: never this run), for the failsafe.</summary>
    public static DateTime LoadedAt { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Whether the helper in this taskbar's Explorer has a background hidden right now; null when
    /// it can't say (no helper of this version there yet, still loading).
    /// </summary>
    public static bool? Cleared(IntPtr taskbar)
    {
        Native.GetWindowThreadProcessId(taskbar, out uint pid);
        if (pid == 0 || !EventWaitHandle.TryOpenExisting(ClearedPrefix + pid, out var cleared)) return null;
        using (cleared) return cleared.WaitOne(0);
    }

    /// <summary>Notes which helper is in charge of this Explorer (throttled; call it on every tick).</summary>
    public static void CheckHelper(IntPtr taskbar)
    {
        if (taskbar == IntPtr.Zero || DateTime.Now - _checkedAt < CheckEvery) return;
        _checkedAt = DateTime.Now;
        Native.GetWindowThreadProcessId(taskbar, out uint pid);
        _legacy = pid != 0 && !Exists(WatchingPrefix + pid) && Exists(LegacyReadyPrefix + pid);
        bool blocked = _legacy && TaskbarCount() > 1;
        if (blocked != Blocked) Log.Info(blocked
            ? "An older taskbar helper holds Explorer and there are several taskbars: not clearing any until Explorer restarts"
            : "Taskbar clearing available again");
        Blocked = blocked;
        PublishLegacy();
    }

    static bool Exists(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle)) return false;
        handle.Dispose();
        return true;
    }

    static int TaskbarCount()
    {
        int n = Native.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero ? 1 : 0;
        for (var h = IntPtr.Zero; (h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero;) n++;
        return n;
    }

    /// <summary>With only the older helper in place, a single taskbar can still be cleared its way.</summary>
    static void PublishLegacy()
    {
        bool want = _legacy && !Blocked && _wanted.Values.Any(h => h != IntPtr.Zero);
        if (want) (_legacyWant ??= new EventWaitHandle(false, EventResetMode.ManualReset, LegacyWantEvent)).Set();
        else _legacyWant?.Reset();
    }
    static DateTime _lastAttempt = DateTime.MinValue;
    static int _loading;

    /// <summary>Asks for <paramref name="owner"/>'s taskbar to be clear; loads the helper into Explorer if needed. Cheap to repeat.</summary>
    public static void Clear(object owner, IntPtr taskbar)
    {
        _wanted[owner] = taskbar;
        Publish();
        PublishLegacy();
        if (taskbar == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(taskbar, out uint pid);
        if (pid == 0 || pid == _loadedIn) return;
        if (IsLoaded(pid))
        {
            _loadedIn = pid;
            LoadedAt = DateTime.Now;
            return;
        }
        if (DateTime.Now - _lastAttempt < RetryEvery || Interlocked.CompareExchange(ref _loading, 1, 0) != 0) return;
        _lastAttempt = DateTime.Now;
        Task.Run(() =>
        {
            try
            {
                Load(taskbar, pid);
            }
            finally
            {
                Volatile.Write(ref _loading, 0);
            }
        });
    }

    /// <summary>Gives <paramref name="owner"/>'s taskbar its own background back.</summary>
    public static void Restore(object owner)
    {
        if (_wanted.Remove(owner)) Publish();
        PublishLegacy();
    }

    /// <summary>Writes the taskbars to clear for the helper, the count last so it never reads a half-written list.</summary>
    static void Publish()
    {
        var windows = _wanted.Values.Where(h => h != IntPtr.Zero).Distinct().Take(MaxTargets).ToList();
        string key = string.Join(",", windows);
        if (key == _published) return;
        _published = key;
        var view = View();
        for (int i = 0; i < windows.Count; i++) view.Write(8 + 8 * i, (long)windows[i]);
        view.Write(0, (uint)windows.Count);
    }

    static MemoryMappedViewAccessor View()
    {
        if (_view == null)
        {
            _targets = MemoryMappedFile.CreateOrOpen(TargetsName, 8 + 8 * MaxTargets);
            _view = _targets.CreateViewAccessor();
        }
        return _view;
    }

    /// <summary>
    /// The Windows taskbar mode the helper puts back should MusicBar end without doing it (ended
    /// from Task Manager, or a crash): null for none. The helper notices the block going away.
    /// </summary>
    public static void PutBackOnEnd(bool? light)
    {
        try
        {
            View().Write(4, light switch { null => 0u, false => 1u, true => 2u });
        }
        catch (Exception ex)
        {
            Log.Error("Telling the taskbar helper the mode to put back failed", ex);
        }
    }

    static bool IsLoaded(uint pid)
    {
        if (!EventWaitHandle.TryOpenExisting(ReadyPrefix + pid, out var ready)) return false;
        ready.Dispose();
        return true;
    }

    /// <summary>
    /// Hooks the taskbar's thread so Windows loads the helper into Explorer, waits
    /// until the helper reports itself attached, then removes the hook (the helper
    /// keeps itself loaded from then on).
    /// </summary>
    static void Load(IntPtr taskbar, uint pid)
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            string dll = WriteHelper();
            RetirePrevious(pid);
            var module = LoadLibrary(dll);
            var proc = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "TapHookProc");
            uint thread = Native.GetWindowThreadProcessId(taskbar, out _);
            if (proc == IntPtr.Zero || thread == 0)
            {
                Log.Error($"Taskbar helper not usable (error {Marshal.GetLastWin32Error()})");
                return;
            }
            hook = SetWindowsHookEx(WH_CALLWNDPROC, proc, module, thread);
            if (hook == IntPtr.Zero)
            {
                Log.Error($"Hooking the taskbar failed (error {Marshal.GetLastWin32Error()})");
                return;
            }
            // Any message through the taskbar's thread loads the DLL there.
            PostMessage(taskbar, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!IsLoaded(pid) && waited.Elapsed < ReadyTimeout) Thread.Sleep(200);
            if (IsLoaded(pid))
            {
                _loadedIn = pid;
                LoadedAt = DateTime.Now;
                Log.Info($"Taskbar helper attached to Explorer ({pid}) in {waited.ElapsedMilliseconds} ms");
            }
            else
            {
                Log.Error($"Taskbar helper did not attach to Explorer ({pid}) within {ReadyTimeout.TotalSeconds:0} s");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Loading the taskbar helper failed", ex);
        }
        finally
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }
    }

    /// <summary>
    /// Asks an older helper still watching in this Explorer to give the taskbars back and stop
    /// (Explorer lets one helper watch at a time, and two would fight over the same taskbar), and
    /// waits until it has. The 1.2-1.5 helper swapped the taskbar's brush, which could go stale.
    /// </summary>
    static void RetirePrevious(uint pid)
    {
        for (int version = FirstRetiringVersion; version < HelperVersion; version++)
        {
            string watching = $@"Local\MusicBar.TaskbarTap.Watching.v{version}.{pid}";
            if (!Exists(watching)) continue;
            using var retire = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\MusicBar.TaskbarTap.Retire.v{version}");
            retire.Set();
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (Exists(watching) && waited.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(100);
            if (Exists(watching)) Log.Error($"The taskbar helper v{version} did not step aside");
            else Log.Info($"The taskbar helper v{version} stepped aside in {waited.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>
    /// Whether an Explorer still holds a helper other than this build's: the one of the copy just
    /// updated over. A helper of the same version is never replaced while its Explorer lives (the
    /// newer one sees it ready and stays out), so only restarting Explorer brings the new one in.
    /// </summary>
    public static bool OlderHelperInExplorer()
    {
        string current = HelperFileName(EmbeddedHelper());
        foreach (var explorer in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            using (explorer)
            {
                try
                {
                    foreach (System.Diagnostics.ProcessModule module in explorer.Modules)
                    {
                        string name = module.ModuleName;
                        if (name.StartsWith("MusicBar.TaskbarTap", StringComparison.OrdinalIgnoreCase)
                            && !name.Equals(current, StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Info($"Explorer ({explorer.Id}) holds the taskbar helper {name}; this build's is {current}");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Listing what Explorer ({explorer.Id}) has loaded failed", ex);
                }
            }
        }
        return false;
    }

    static byte[] EmbeddedHelper()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MusicBar.TaskbarTap.dll")
                             ?? throw new InvalidOperationException("The taskbar helper is missing from this build.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    static string HelperFileName(byte[] bytes)
        => $"MusicBar.TaskbarTap.{Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant()}.dll";

    /// <summary>
    /// Writes the embedded helper next to MusicBar's data. The file name carries a
    /// hash of its contents: a copy Explorer still holds open is never overwritten.
    /// </summary>
    static string WriteHelper()
    {
        var bytes = EmbeddedHelper();
        string folder = Path.Combine(AppPaths.DataDir, "TaskbarHelper");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, HelperFileName(bytes));
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        // Older copies go once no Explorer holds them any more.
        foreach (var old in Directory.EnumerateFiles(folder, "MusicBar.TaskbarTap.*.dll"))
        {
            if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(old);
            }
            catch (Exception)
            {
            }
        }
        return path;
    }

    const int WH_CALLWNDPROC = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int type, IntPtr proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
