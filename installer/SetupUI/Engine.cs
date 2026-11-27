using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MusicBar.Setup;

/// <summary>
/// What the window drives: the Inno Setup installer carried inside this exe (run hidden, its progress posted back to
/// the window), the copy already on this machine (found by its uninstall entry), its uninstaller, and the app itself.
/// </summary>
static class Engine
{
    /// <summary>
    /// MusicBar.iss's AppId (Inno names the per-user uninstall entry after it) and folder name (Programs\MusicBar,
    /// and the app's data in LocalAppData\MusicBar). A test build carries its own in setup.txt, to stay clear of
    /// the MusicBar installed on the machine.
    /// </summary>
    static string AppId = "{6C1F7D5E-3B7A-4F0E-9A51-7D2C3E8B4A90}";
    static string Folder = "MusicBar";
    static string UninstallKey => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId + "_is1";

    /// <summary>The message MusicBar.iss posts with the install's progress, in thousandths (WM_APP + 0x51).</summary>
    public const int ProgressMessage = 0x8051;

    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string DefaultDir => Path.Combine(LocalAppData, "Programs", Folder);
    /// <summary>The app's own data: settings, downloaded skins, logs (AppPaths.DataDir).</summary>
    public static string DataDir => Path.Combine(LocalAppData, Folder);
    public static string LogPath => Path.Combine(Path.GetTempPath(), "MusicBar-Setup.log");

    // ------------------------------------------------------------------ this version

    /// <summary>This setup's version, the installed size and the "what's new" lines (setup.txt, written by build.ps1).</summary>
    public static string Version { get; private set; } = "";
    public static int SizeMb { get; private set; } = 200;
    public static List<string> News { get; } = new();

    public static bool HasPayload => Assembly.GetExecutingAssembly().GetManifestResourceInfo("payload.exe") != null;

    public static void LoadInfo()
    {
        Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        int plus = Version.IndexOf('+');
        if (plus > 0) Version = Version.Substring(0, plus);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.txt");
        if (stream == null) return;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
            if (key == "version" && value.Length > 0) Version = value;
            else if (key == "size" && int.TryParse(value, out int mb) && mb > 0) SizeMb = mb;
            else if (key == "news" && value.Length > 0) News.Add(value);
            else if (key == "appid" && Regex.IsMatch(value, @"^[0-9A-Fa-f-]{36}$")) AppId = "{" + value.ToUpperInvariant() + "}";
            else if (key == "folder" && Regex.IsMatch(value, @"^[A-Za-z0-9 _-]{1,40}$")) Folder = value;
        }
    }

    // ------------------------------------------------------------------ the copy on this machine

    public sealed class Installed
    {
        public string Version = "";
        public string Dir = "";
    }

    /// <summary>MusicBar as Inno recorded it for this account, or null.</summary>
    public static Installed? FindInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key == null) return null;
            string dir = key.GetValue("Inno Setup: App Path") as string ?? key.GetValue("InstallLocation") as string ?? "";
            dir = dir.TrimEnd('\\');
            if (dir.Length == 0 || !File.Exists(Path.Combine(dir, "MusicBar.exe"))) return null;
            return new Installed { Version = key.GetValue("DisplayVersion") as string ?? "", Dir = dir };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>-1, 0 or 1 as a is older than, the same as or newer than b (x.y.z, a fourth part counts too).</summary>
    public static int Compare(string a, string b)
    {
        static Version Parse(string s)
        {
            var m = Regex.Match(s ?? "", @"\d+(\.\d+){0,3}");
            return m.Success && System.Version.TryParse(m.Value.Contains(".") ? m.Value : m.Value + ".0", out var v) ? v : new Version(0, 0);
        }
        var x = Parse(a);
        var y = Parse(b);
        // 2.0.1 and 2.0.1.0 are the same version.
        x = new Version(x.Major, x.Minor, Math.Max(0, x.Build), Math.Max(0, x.Revision));
        y = new Version(y.Major, y.Minor, Math.Max(0, y.Build), Math.Max(0, y.Revision));
        return x.CompareTo(y);
    }

    public static bool AppRunning()
    {
        try { return Process.GetProcessesByName("MusicBar").Length > 0; }
        catch { return false; }
    }

    /// <summary>The language MusicBar itself is set to, when it has run on this account: "vi", "en" or null.</summary>
    public static string? AppLanguage()
    {
        try
        {
            string file = Path.Combine(DataDir, "settings.json");
            if (!File.Exists(file)) return null;
            var m = Regex.Match(File.ReadAllText(file), "\"Language\"\\s*:\\s*\"?(\\w+)");
            if (!m.Success) return null;
            return m.Groups[1].Value is "Vietnamese" or "1" ? "vi" : "en";
        }
        catch
        {
            return null;
        }
    }

    public static long FreeBytes(string dir)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(dir));
            return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    // ------------------------------------------------------------------ installing

    /// <summary>Unpacks the Inno setup carried inside this exe into a folder of its own in TEMP.</summary>
    public static string ExtractPayload()
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe")
            ?? throw new InvalidOperationException("This setup carries no installer.");
        string folder = Path.Combine(Path.GetTempPath(), "MusicBar-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "MusicBar-Setup-core.exe");
        using (var file = File.Create(path)) source.CopyTo(file);
        return path;
    }

    /// <summary>Runs a program hidden and waits for its exit code.</summary>
    public static Task<int> RunAsync(string exe, string args) => Task.Run(() =>
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("Windows did not start " + Path.GetFileName(exe));
        p.WaitForExit();
        return p.ExitCode;
    });

    /// <summary>The Inno setup's command line for a hidden install, with the window to post progress to.</summary>
    public static string InnoArgs(IntPtr host, string lang, string? dir, bool? startMenu, bool? desktop, bool launch)
    {
        var a = new StringBuilder("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-");
        a.Append(" /LANG=").Append(lang);
        a.Append(" /LOG=").Append(Quote(LogPath));
        if (host != IntPtr.Zero) a.Append(" /HOSTWND=").Append(host.ToInt64());
        if (!launch) a.Append(" /NOLAUNCH=1");
        if (dir != null) a.Append(" /DIR=").Append(Quote(dir));
        if (startMenu != null && desktop != null)
            a.Append(" /MERGETASKS=").Append(Quote((startMenu.Value ? "" : "!") + "startmenu," + (desktop.Value ? "" : "!") + "desktopicon"));
        return a.ToString();
    }

    /// <summary>Deletes the unpacked setup and its folder (after it has exited).</summary>
    public static void Discard(string? payload)
    {
        if (payload == null) return;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(payload)!, true);
                return;
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    public static void Launch(string dir)
    {
        try
        {
            // --updated: the new MusicBar restarts Explorer if Explorer still holds an old copy's taskbar helper.
            Process.Start(new ProcessStartInfo(Path.Combine(dir, "MusicBar.exe"), "--updated") { UseShellExecute = true, WorkingDirectory = dir });
        }
        catch
        {
        }
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // ------------------------------------------------------------------ uninstalling

    /// <summary>Inno's uninstaller in that folder (unins000.exe, or a later number if there were several).</summary>
    public static string? FindUninstaller(string appDir)
    {
        try
        {
            return Directory.GetFiles(appDir, "unins???.exe")
                .Where(f => File.Exists(Path.ChangeExtension(f, ".dat")))
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Inno's uninstaller, hidden: it closes MusicBar, runs MusicBar --uninstall (the taskbar back as Windows had it)
    /// and removes the files. -1 if there is no uninstaller in that folder.
    /// </summary>
    public static Task<int> UninstallAsync(string appDir)
    {
        // No await here: the quiet uninstall waits on this from the UI thread (.Result), and an await would try to
        // come back to that blocked thread and never finish.
        string? uninstaller = FindUninstaller(appDir);
        return uninstaller == null ? Task.FromResult(-1) : RunAsync(uninstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART");
    }

    /// <summary>Settings, downloaded skins and logs, gone (asked for on the uninstall window).</summary>
    public static void Purge()
    {
        for (int i = 0; i < 10 && Directory.Exists(DataDir); i++)
        {
            try
            {
                Directory.Delete(DataDir, true);
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>
    /// The uninstall window can't run from the folder it removes: a copy of this exe in TEMP takes over, told where
    /// MusicBar is. True when the copy started (this one should then quit).
    /// </summary>
    public static bool HandOffToTemp(string appDir, IEnumerable<string> args)
    {
        try
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string folder = Path.Combine(Path.GetTempPath(), "MusicBar-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, "MusicBarSetup.exe");
            File.Copy(self, copy, true);
            string line = string.Join(" ", args.Select(Quote)) + " /APPDIR=" + Quote(appDir);
            Process.Start(new ProcessStartInfo(copy, line) { UseShellExecute = false, WorkingDirectory = folder });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The TEMP copy removes itself once it has exited.</summary>
    public static void RemoveSelfLater()
    {
        try
        {
            string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            if (!Path.GetFileName(folder).StartsWith("MusicBar-uninstall-", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rd /s /q " + Quote(folder))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        catch
        {
        }
    }

    public static string Quote(string s) => s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? s : "\"" + s.Replace("\"", "\\\"") + "\"";
}
