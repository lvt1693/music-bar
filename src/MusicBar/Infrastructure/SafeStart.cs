using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace MusicBar.Infrastructure;

/// <summary>
/// Safe start: Shift held while MusicBar starts, or --safe. For that run nothing of MusicBar's goes into Explorer and
/// Windows' own light or dark mode is left alone, so it is the way back in when Explorer keeps crashing. The bar
/// still plays, tinted over the taskbar.
/// </summary>
public static class SafeStart
{
    public static bool On { get; private set; }

    public static void Detect(string[] args)
    {
        On = args.Any(a => a.Equals("--safe", StringComparison.OrdinalIgnoreCase)) || (GetAsyncKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0;
        if (On) Log.Info("Safe start (Shift held, or --safe): no taskbar helper in Explorer and no Windows mode changes this run");
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}
