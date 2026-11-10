using System;
using System.Runtime.InteropServices;
using System.Threading;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>
/// Win + A opens MusicBar's control center instead of Windows' Quick Settings, once MusicBar's has
/// taken the tray button's place. A low-level keyboard hook on its own thread (so a busy UI never
/// delays typing) swallows the A while a Windows key is down and asks for the panel. Swallowed, the
/// key leaves Windows seeing a Windows key pressed and released alone, which opens Start; a key
/// nobody uses (0xFF) is sent in between, as PowerToys does, so Start stays shut.
/// </summary>
internal sealed class WinAHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int WM_QUIT = 0x0012, WM_TIMER = 0x0113;
    const uint RearmMilliseconds = 3 * 60 * 1000;
    const int VK_A = 0x41, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const ushort VK_DUMMY = 0xFF;
    // Marks the keys this hook sends, so it never reacts to its own.
    static readonly IntPtr Mine = new(0x4D42_4148);

    readonly Action _pressed;
    readonly Thread _thread;
    readonly HookProc _proc;
    uint _threadId;
    IntPtr _hook;
    volatile bool _stop;
    // The A that opened the panel: its repeats and its release are swallowed too.
    bool _holdingA;

    /// <param name="pressed">Called on the hook's thread; hand it to the UI thread.</param>
    public WinAHook(Action pressed)
    {
        _pressed = pressed;
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "MusicBar.WinAHook" };
        _thread.Start();
    }

    void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            // The thread and its timer carry on: the next re-arm tries again.
            Log.Error($"Win + A hook not set (error {Marshal.GetLastWin32Error()})");
        }
        // Windows drops a low-level hook for good when one call of it is ever too late (a stalled machine, a
        // resume from sleep) and says nothing, so the hook is put in again from time to time.
        var timer = SetTimer(IntPtr.Zero, IntPtr.Zero, RearmMilliseconds, IntPtr.Zero);
        while (!_stop && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.Message == WM_TIMER && msg.Hwnd == IntPtr.Zero)
            {
                Rearm();
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (timer != IntPtr.Zero) KillTimer(IntPtr.Zero, timer);
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>
    /// The new hook goes in before the old one comes out: if Windows refuses the new one, the old keeps working.
    /// Keys are never handled twice or missed meanwhile: the hook's calls arrive on this thread only while it
    /// waits in GetMessage, and this method does not, so no key is seen between the two steps, and a key typed
    /// during them waits microseconds (far below the limit) and meets only the new hook.
    /// </summary>
    void Rearm()
    {
        var fresh = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (fresh == IntPtr.Zero)
        {
            Log.Error($"Win + A hook not set again (error {Marshal.GetLastWin32Error()})");
            return;
        }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        else Log.Info("Win + A hook set after all");
        _hook = fresh;
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if (key.ExtraInfo == Mine || key.VkCode != VK_A) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        int message = wParam.ToInt32();
        bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool up = message is WM_KEYUP or WM_SYSKEYUP;
        if (up && _holdingA)
        {
            _holdingA = false;
            return new IntPtr(1);
        }
        if (!down) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        if (_holdingA) return new IntPtr(1);

        bool win = Down(VK_LWIN) || Down(VK_RWIN);
        bool other = Down(VK_SHIFT) || Down(VK_CONTROL) || Down(VK_MENU);
        if (!win || other) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

        _holdingA = true;
        SendDummy();
        try
        {
            _pressed();
        }
        catch (Exception ex)
        {
            Log.Error("Win + A failed", ex);
        }
        return new IntPtr(1);
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    static void SendDummy()
    {
        var inputs = new Input[2];
        for (int i = 0; i < 2; i++)
        {
            inputs[i].Type = 1; // INPUT_KEYBOARD
            inputs[i].Key = new KeybdInput { Vk = VK_DUMMY, Flags = i == 0 ? 0u : 2u, ExtraInfo = Mine };
        }
        SendInput(2, inputs, Marshal.SizeOf<Input>());
    }

    public void Dispose()
    {
        // Set first: a thread that has not got as far as its message loop sees it and ends there.
        _stop = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KbdLlHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT with the keyboard member; padded to the size of the largest member (MOUSEINPUT).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint milliseconds, IntPtr proc);

    [DllImport("user32.dll")]
    static extern bool KillTimer(IntPtr hwnd, IntPtr id);

    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);
}
