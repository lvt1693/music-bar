using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>
/// Windows as it was before MusicBar first ran, for the settings MusicBar can change: the taskbar's
/// light or dark mode, and the taskbar's own size, alignment and ExplorerPatcher style. Taken once,
/// at the very first start, before anything is touched. Each time MusicBar writes one of them the
/// value is noted, and when MusicBar is uninstalled every value it changed goes back to how it was,
/// unless it was changed again since (then that later choice stays).
/// What MusicBar changes only while it runs (the see-through taskbar, the folded tray, the mode it
/// matches) is handed back at every exit; this covers what would otherwise outlast it.
/// </summary>
public static class SystemSnapshot
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string ExplorerPatcher = @"Software\ExplorerPatcher";

    /// <summary>The values kept: where, and whether Windows shows a change without Explorer restarting.</summary>
    static readonly (string Key, string Name, bool Live)[] Kept =
    [
        (Personalize, "SystemUsesLightTheme", true),
        (Advanced, "TaskbarAl", true),
        (Advanced, "TaskbarSize", true),
        (Advanced, "IconSizePreference", false),
        (Advanced, "TaskbarSmallIcons", false),
        (ExplorerPatcher, "OldTaskbar", false),
        (ExplorerPatcher, "OldTaskbarAl", false),
    ];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static readonly object Gate = new();

    static string FilePath => Path.Combine(AppPaths.DataDir, "windows-before.json");

    /// <summary>Before: each value as Windows had it (null: not set). Written: the last value MusicBar wrote there.</summary>
    sealed class Snapshot
    {
        public DateTime Taken { get; set; }
        public Dictionary<string, int?> Before { get; set; } = [];
        public Dictionary<string, int> Written { get; set; } = [];
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>
    /// Records Windows as it is now, unless that was done before. <paramref name="heldMode"/> is the
    /// user's own taskbar mode (1 light, 0 dark, -1 none) if a run that ended badly still holds it.
    /// </summary>
    public static void TakeOnce(int heldMode)
    {
        lock (Gate)
        {
            if (File.Exists(FilePath)) return;
            var snapshot = new Snapshot { Taken = DateTime.Now };
            foreach (var (key, name, _) in Kept) snapshot.Before[Id(key, name)] = Read(key, name);
            if (heldMode >= 0) snapshot.Before[Id(Personalize, "SystemUsesLightTheme")] = heldMode;
            Save(snapshot);
            Log.Info("Windows as it was before MusicBar: " + string.Join(", ", snapshot.Before.Select(p => $"{Short(p.Key)}={p.Value?.ToString() ?? "unset"}")));
        }
    }

    /// <summary>Notes a value MusicBar just wrote, so uninstalling can tell its changes from the user's own.</summary>
    public static void Wrote(string key, string name, int value)
    {
        try
        {
            lock (Gate)
            {
                if (Load() is not { } snapshot) return;
                snapshot.Written[Id(key, name)] = value;
                Save(snapshot);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Noting a changed Windows setting failed", ex);
        }
    }

    /// <summary>
    /// Puts back every value MusicBar changed and nobody changed again since; returns what it put
    /// back, and whether Explorer has to restart for all of it to show.
    /// </summary>
    public static (List<string> PutBack, bool RestartExplorer) PutBack()
    {
        var done = new List<string>();
        bool restart = false;
        lock (Gate)
        {
            if (Load() is not { } snapshot) return (done, false);
            foreach (var (key, name, live) in Kept)
            {
                string id = Id(key, name);
                if (!snapshot.Written.TryGetValue(id, out int written) || !snapshot.Before.TryGetValue(id, out var before)) continue;
                int? now = Read(key, name);
                // Changed again since MusicBar wrote it, or already as it was: not MusicBar's to touch.
                if (now != written || now == before) continue;
                if (!Write(key, name, before)) continue;
                done.Add($"{Short(id)} {now} → {before?.ToString() ?? "unset"}");
                restart |= !live;
            }
        }
        if (done.Count > 0)
        {
            Broadcast("ImmersiveColorSet");
            Broadcast("TraySettings");
            Log.Info("Put Windows back as it was before MusicBar: " + string.Join(", ", done));
        }
        return (done, restart);
    }

    /// <summary>Forgets the snapshot (after uninstalling): a later install takes a fresh one.</summary>
    public static void Forget()
    {
        lock (Gate)
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Removing the Windows snapshot failed", ex);
            }
        }
    }

    static string Id(string key, string name) => key + "\\" + name;

    static string Short(string id) => id[(id.LastIndexOf('\\') + 1)..];

    static int? Read(string key, string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(key);
            return k?.GetValue(name) is int v ? v : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Sets a value, or removes it when it wasn't set before. Never makes a key that isn't there (ExplorerPatcher's means "installed").</summary>
    static bool Write(string key, string name, int? value)
    {
        try
        {
            using var k = value == null ? Registry.CurrentUser.OpenSubKey(key, writable: true) : Registry.CurrentUser.CreateSubKey(key);
            if (k == null) return true;
            if (value is int v) k.SetValue(name, v, RegistryValueKind.DWord);
            else k.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Putting {name} back failed", ex);
            return false;
        }
    }

    static Snapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath), Json) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Reading the Windows snapshot failed", ex);
            return null;
        }
    }

    static void Save(Snapshot snapshot) => File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, Json));

    static void Broadcast(string area)
        => SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, area, 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);
}
