using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

public enum TaskbarKind { Windows11, Windows10, ExplorerPatcher }

/// <summary>
/// Reads and changes the Windows taskbar's own size and alignment.
/// Each taskbar flavour keeps these in different registry values:
/// <list type="bullet">
/// <item>Windows 11: TaskbarAl (0 left, 1 center; the Start menu follows) and IconSizePreference (0 always small, 1 never).
/// From build 26220.8754 / 26300.8758 Windows 11 has a real taskbar size setting as well, TaskbarSize
/// (0 medium, 1 small), which also makes the bar thinner and applies without restarting Explorer.</item>
/// <item>Windows 10 taskbar (Windows 10 or ExplorerPatcher): TaskbarSmallIcons (1 small).</item>
/// <item>ExplorerPatcher: OldTaskbarAl (0 at edge, 3 centered with Start) and TaskbarAl for the Start menu position.</item>
/// </list>
/// Values for the other flavours are harmless, so both sets are written.
/// </summary>
public static class TaskbarTweaks
{
    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string ExplorerPatcherKey = @"Software\ExplorerPatcher";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    static bool HasExplorerPatcher
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ExplorerPatcherKey);
            return key != null;
        }
    }

    /// <summary>
    /// Which taskbar is on screen. ExplorerPatcher's "Taskbar style" (OldTaskbar:
    /// 0 Windows 11, 1 Windows 10, 2 Windows 10 (ExplorerPatcher)) decides it when
    /// installed; the window structure is not reliable across Windows builds.
    /// </summary>
    public static TaskbarKind Kind
    {
        get
        {
            if (!IsWindows11) return TaskbarKind.Windows10;
            if (HasExplorerPatcher && Read(ExplorerPatcherKey, "OldTaskbar", 0) != 0) return TaskbarKind.ExplorerPatcher;
            return TaskbarKind.Windows11;
        }
    }

    /// <summary>
    /// The Windows 11 taskbar paints its background in XAML (the rectangles the helper in Explorer hides) since
    /// 22H2's March 2023 update (22621.1413). Before that, on 21H2 (22000) and early 22H2, the window's accent
    /// paints it as on Windows 10, and the accent is what makes it see-through: the helper finds nothing to
    /// hide there (#17).
    /// </summary>
    public static bool XamlBackground
    {
        get
        {
            if (Kind != TaskbarKind.Windows11) return false;
            int build = Environment.OSVersion.Version.Build;
            if (build < 22621) return false;
            if (build > 22623) return true;
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return (key?.GetValue("UBR") is int ubr ? ubr : 0) >= 1413;
        }
    }

    /// <summary>Windows 11 builds with the "Taskbar size" setting (Small makes the bar thinner too).</summary>
    public static bool HasTaskbarSizeSetting
    {
        get
        {
            if (!IsWindows11) return false;
            if (Read(Advanced, "TaskbarSize", -1) >= 0) return true;
            int build = Environment.OSVersion.Version.Build;
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            int revision = key?.GetValue("UBR") is int ubr ? ubr : 0;
            return build > 26300 || (build == 26300 && revision >= 8758) || (build == 26220 && revision >= 8754);
        }
    }

    /// <summary>The Windows 10 style taskbars get thinner; Windows 11 only with its own size setting.</summary>
    public static bool CanShrinkHeight => Kind != TaskbarKind.Windows11 || HasTaskbarSizeSetting;

    /// <summary>ExplorerPatcher is installed, its own (thin-capable) taskbar is not in use, and Windows 11 cannot thin its own.</summary>
    public static bool CanSwitchToExplorerPatcherTaskbar =>
        IsWindows11 && HasExplorerPatcher && Kind == TaskbarKind.Windows11 && !HasTaskbarSizeSetting;

    /// <summary>Switches to ExplorerPatcher's Windows 10 taskbar, which supports a thin bar. Needs an Explorer restart.</summary>
    public static void UseExplorerPatcherTaskbar()
    {
        Write(ExplorerPatcherKey, "OldTaskbar", 2);
        Write(Advanced, "TaskbarSmallIcons", 1);
        if (IsCentered) Write(ExplorerPatcherKey, "OldTaskbarAl", 3);
    }

    public static string KindLabel => Kind switch
    {
        TaskbarKind.Windows11 => "Windows 11",
        TaskbarKind.ExplorerPatcher => Loc.T("Taskbar_ExplorerPatcher"),
        _ => "Windows 10",
    };

    /// <summary>Windows 10's own taskbar cannot center its icons.</summary>
    public static bool CanCenter => Kind != TaskbarKind.Windows10;

    public static bool IsSmall => Kind == TaskbarKind.Windows11
        ? HasTaskbarSizeSetting ? Read(Advanced, "TaskbarSize", 0) == 1 : Read(Advanced, "IconSizePreference", 1) == 0
        : Read(Advanced, "TaskbarSmallIcons", 0) == 1;

    public static bool IsCentered => Kind switch
    {
        TaskbarKind.Windows11 => Read(Advanced, "TaskbarAl", 1) == 1,
        TaskbarKind.ExplorerPatcher => (Read(ExplorerPatcherKey, "OldTaskbarAl", 0) & 1) == 1,
        _ => false,
    };

    /// <summary>Windows 11 picks alignment (and its own size setting) up live; everything else needs Explorer restarted.</summary>
    public static bool AppliesLive(bool sizeChange) => Kind == TaskbarKind.Windows11 && (!sizeChange || HasTaskbarSizeSetting);

    public static void SetSmall(bool small)
    {
        Write(Advanced, "TaskbarSmallIcons", small ? 1 : 0);
        if (IsWindows11)
        {
            Write(Advanced, "IconSizePreference", small ? 0 : 1);
            if (HasTaskbarSizeSetting) Write(Advanced, "TaskbarSize", small ? 1 : 0);
        }
        Broadcast();
    }

    public static void SetCentered(bool centered)
    {
        Write(Advanced, "TaskbarAl", centered ? 1 : 0);
        if (HasExplorerPatcher) Write(ExplorerPatcherKey, "OldTaskbarAl", centered ? 3 : 0);
        Broadcast();
    }

    /// <summary>Restarts Explorer so the taskbar reloads its settings. Open File Explorer windows close.</summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                using (p)
                {
                    p.Kill();
                    p.WaitForExit(4000);
                }
            }
            // Windows usually brings the shell back by itself; start it only if it did not.
            for (int i = 0; i < 30 && FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; i++) Thread.Sleep(100);
            if (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Restarting Explorer failed", ex);
        }
    }

    static void Broadcast()
        => SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "TraySettings",
            0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);

    static int Read(string path, string name, int fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) is int v ? v : fallback;
    }

    static void Write(string path, string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue(name, value, RegistryValueKind.DWord);
            Log.Info($"Taskbar tweak: {path}\\{name} = {value}");
            SystemSnapshot.Wrote(path, name, value);
        }
        catch (Exception ex)
        {
            Log.Error($"Writing {name} failed", ex);
        }
    }
}
