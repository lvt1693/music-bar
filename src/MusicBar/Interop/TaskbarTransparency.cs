using System;
using System.Runtime.InteropServices;

namespace MusicBar.Interop;

/// <summary>
/// Makes Windows 10 style taskbars (Windows 10, ExplorerPatcher) fully
/// see-through with the DWM accent policy, the same way TranslucentTB's
/// "Clear" mode does. Explorer resets the accent on some events (opening
/// Start, theme changes), so callers re-apply it regularly.
/// The Windows 11 taskbar draws its background in XAML and ignores this.
/// </summary>
internal static class TaskbarTransparency
{
    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public int State;
        public int Flags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CompositionData
    {
        public int Attribute;
        public IntPtr Data;
        public int Size;
    }

    const int WCA_ACCENT_POLICY = 19;
    const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    const uint WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    [DllImport("user32.dll")]
    static extern bool SetWindowCompositionAttribute(IntPtr hWnd, ref CompositionData data);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>
    /// Clears one taskbar's background (only taskbars with MusicBar's layer behind them;
    /// a see-through taskbar with nothing there shows the wallpaper smeared through Windows' blur).
    /// </summary>
    public static void Apply(IntPtr taskbar) => Clear(taskbar);

    /// <summary>
    /// Asks Explorer to repaint a taskbar's own background. Only the taskbars are told: broadcast, every
    /// window redid its colors (each allowed half a second), which froze MusicBar for seconds and made a
    /// game that had just gone full screen stutter.
    /// </summary>
    public static void Restore(IntPtr taskbar)
    {
        var main = FindWindow("Shell_TrayWnd", null);
        var taskbars = taskbar == main || main == IntPtr.Zero ? new[] { taskbar } : new[] { taskbar, main };
        foreach (var h in taskbars)
        {
            PostMessage(h, WM_DWMCOLORIZATIONCOLORCHANGED, IntPtr.Zero, IntPtr.Zero);
            SendMessageTimeout(h, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet",
                0x0002 /* SMTO_ABORTIFHUNG */, 500, out _);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string? windowName);

    static void Clear(IntPtr hwnd)
    {
        var accent = new AccentPolicy { State = ACCENT_ENABLE_TRANSPARENTGRADIENT, Flags = 2, GradientColor = 0 };
        int size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new CompositionData { Attribute = WCA_ACCENT_POLICY, Data = ptr, Size = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
