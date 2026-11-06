using System.Windows;
using System.Windows.Interop;

namespace MusicBar.Views;

/// <summary>
/// The windows that live on the taskbar draw on the CPU. Drawn by the graphics card, WPF reads every
/// frame of a see-through window back from it: while music played, the driver's buffers for that
/// swung between 65 and 225 MB, taking MusicBar to 315 MB, against 73 MB in all on the CPU for about
/// 4 % more of one core. Only a skin (a ps_3_0 pixel shader, which the CPU renderer can't run) needs the card.
/// </summary>
internal static class CpuRendering
{
    // For measuring: MUSICBAR_RENDER=hardware leaves every window on the graphics card, as before.
    static readonly bool Off = System.Environment.GetEnvironmentVariable("MUSICBAR_RENDER") == "hardware";

    /// <summary>The window draws on the CPU from the moment it has a handle.</summary>
    public static void Use(Window window) => window.SourceInitialized += (_, _) => Set(window, true);

    /// <summary>Switches a window between the CPU and the graphics card; nothing until it has a handle.</summary>
    public static void Set(Window window, bool cpu)
    {
        if (Off) return;
        // From the handle: while SourceInitialized runs, the window isn't the source's root visual yet.
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != System.IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.RenderMode = cpu ? RenderMode.SoftwareOnly : RenderMode.Default;
    }
}
