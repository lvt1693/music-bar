using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>
/// Windows draws its own taskbar icons (tray, clock, Wi-Fi, volume, battery) white in dark mode and
/// black in light mode, whatever MusicBar paints behind them. This puts Windows' taskbar mode
/// (Settings → Personalization → Colors → "Choose your default Windows mode") in line with the bar,
/// keeps the user's own mode in the settings, and puts it back when MusicBar lets go. A crash puts
/// it back from the crash handler; an end from Task Manager, through the taskbar helper in Explorer
/// (when it is there), or else at the next exit. A change made elsewhere while MusicBar holds the
/// mode wins: MusicBar leaves it alone until the bar's look changes again.
/// </summary>
internal sealed class WindowsModeSync(AppSettings settings)
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    readonly AppSettings _s = settings;
    // The mode MusicBar set this run, while it holds Windows' mode.
    bool? _wrote;
    // The user's mode while MusicBar holds Windows' (1 light, 0 dark, -1 not holding), for a crash.
    static int _putBack = -1;
    // The bar's look when someone else changed the mode back: not pressed again until the look changes.
    bool? _declined;

    /// <summary>The mode the user chose: the one kept aside while MusicBar holds Windows', else Windows' own.</summary>
    public bool UserLight => _s.WindowsModeBefore >= 0 ? _s.WindowsModeBefore == 1 : ColorUtil.WindowsTaskbarIsLight();

    /// <summary>The bar MusicBar paints is light (dark icons wanted) or dark (white icons wanted).</summary>
    public void Want(bool light)
    {
        bool now = ColorUtil.WindowsTaskbarIsLight();
        if (_wrote is bool wrote && now != wrote)
        {
            Log.Info($"Windows taskbar mode changed to {Name(now)} outside MusicBar; leaving it");
            _s.WindowsModeBefore = -1;
            _wrote = null;
            _declined = light;
            Holding(null);
        }
        if (_declined is bool declined)
        {
            if (declined == light) return;
            _declined = null;
        }

        bool user = UserLight;
        if (light == user)
        {
            Release(wait: false);
            return;
        }
        if (_s.WindowsModeBefore < 0) _s.WindowsModeBefore = user ? 1 : 0;
        if (_wrote == null) Holding(user);
        _wrote = light;
        if (now != light) Write(light, wait: false);
    }

    /// <summary>Puts the user's own mode back, unless it was changed elsewhere since MusicBar set it.</summary>
    /// <param name="wait">Wait until the apps have heard of it, as on the way out; else they hear in the background.</param>
    public void Release(bool wait = true)
    {
        _declined = null;
        if (_s.WindowsModeBefore < 0) return;
        bool user = _s.WindowsModeBefore == 1;
        bool now = ColorUtil.WindowsTaskbarIsLight();
        bool changedElsewhere = _wrote is bool wrote && now != wrote;
        if (!changedElsewhere && now != user) Write(user, wait);
        _s.WindowsModeBefore = -1;
        _wrote = null;
        Holding(null);
    }

    /// <summary>
    /// Notes the mode to put back should MusicBar end without doing it: the crash handler
    /// (<see cref="PutBackNow"/>) and, for an end from Task Manager, the taskbar helper in Explorer.
    /// </summary>
    static void Holding(bool? user)
    {
        Interlocked.Exchange(ref _putBack, user is bool light ? (light ? 1 : 0) : -1);
        XamlTaskbar.PutBackOnEnd(user);
    }

    /// <summary>For a crash: puts the user's mode back without touching the settings (any thread).</summary>
    public static void PutBackNow()
    {
        int mode = Interlocked.Exchange(ref _putBack, -1);
        if (mode >= 0 && ColorUtil.WindowsTaskbarIsLight() != (mode == 1)) Write(mode == 1, wait: true);
    }

    static void Write(bool light, bool wait)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
            key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Log.Error("Setting Windows' taskbar mode failed", ex);
            return;
        }
        SystemSnapshot.Wrote(PersonalizeKey, "SystemUsesLightTheme", light ? 1 : 0);
        Log.Info($"Windows taskbar mode set to {Name(light)}");
        if (wait) Broadcast();
        else Task.Run(Broadcast);
    }

    /// <summary>
    /// The same broadcast Settings sends: Explorer redraws the taskbar and its icons. Every window gets
    /// it, each allowed half a second, so on the UI thread a PC full of windows froze MusicBar for seconds.
    /// </summary>
    static void Broadcast() => SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero,
        "ImmersiveColorSet", 0x0002 /* SMTO_ABORTIFHUNG */, 500, out _);

    static string Name(bool light) => light ? "light" : "dark";
}
