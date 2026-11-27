using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace MusicBar.Setup;

/// <summary>What the command line asked for.</summary>
public sealed class Request
{
    /// <summary>/SILENT: no questions, the progress window only (MusicBar's own updates start setup this way).</summary>
    public bool Silent;
    /// <summary>/VERYSILENT: no window at all; everything goes straight to the installer underneath.</summary>
    public bool VerySilent;
    /// <summary>/UNINSTALL: the uninstall window (Windows' Installed apps runs MusicBar-Uninstall.exe so).</summary>
    public bool Uninstall;
    /// <summary>/PURGE=1: when uninstalling quietly, also delete settings and downloaded skins.</summary>
    public bool Purge;
    /// <summary>/APPDIR=: the MusicBar folder, for the uninstall window running from its copy in TEMP.</summary>
    public string? AppDir;
    /// <summary>/LANG=vi|en</summary>
    public string? Lang;
    /// <summary>The command line after the program's name, as typed: handed to the installer underneath.</summary>
    public string Tail = "";
    /// <summary>Tests: /TESTPOS=x,y opens the window there (off screen) without taking focus.</summary>
    public Point? TestPos;
    /// <summary>Tests: /SNAPSHOTS=folder draws every screen into PNGs there and quits; nothing is installed.</summary>
    public string? Snapshots;

    public static Request Parse(string[] args)
    {
        var r = new Request { Tail = RawTail() };
        foreach (string arg in args)
        {
            string a = arg.Trim();
            int eq = a.IndexOf('=');
            string name = (eq > 0 ? a.Substring(0, eq) : a).ToUpperInvariant();
            string value = eq > 0 ? a.Substring(eq + 1).Trim('"') : "";
            switch (name)
            {
                case "/SILENT": r.Silent = true; break;
                case "/VERYSILENT": r.VerySilent = true; break;
                case "/UNINSTALL": r.Uninstall = true; break;
                case "/PURGE": r.Purge = value == "1"; break;
                case "/APPDIR": r.AppDir = value.TrimEnd('\\'); break;
                case "/LANG": r.Lang = value.StartsWith("vi", StringComparison.OrdinalIgnoreCase) ? "vi" : "en"; break;
                case "/SNAPSHOTS": r.Snapshots = value; break;
                case "/TESTPOS":
                    string[] xy = value.Split(',');
                    if (xy.Length == 2 && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                        && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                        r.TestPos = new Point(x, y);
                    break;
            }
        }
        return r;
    }

    /// <summary>Environment.CommandLine without the program's own path (quoted or not).</summary>
    static string RawTail()
    {
        string line = Environment.CommandLine.TrimStart();
        int end = line.StartsWith("\"") ? line.IndexOf('"', 1) + 1 : line.IndexOf(' ');
        return end <= 0 || end >= line.Length ? "" : line.Substring(end).Trim();
    }

    /// <summary>The command line for a hidden run of the installer underneath, with /SILENT turned into /VERYSILENT.</summary>
    public string QuietTail() => Regex.Replace(Tail, @"(?<![\w/])/SILENT\b", "/VERYSILENT", RegexOptions.IgnoreCase);
}

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Engine.LoadInfo();
        var request = Request.Parse(e.Args);
        if (request.Snapshots != null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Snapshots.Render(request.Snapshots);
            Shutdown(0);
            return;
        }
        string lang = request.Lang ?? Engine.AppLanguage()
            ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi" ? "vi" : "en");

        // The copy beside MusicBar carries no installer: whatever started it, it is there to uninstall.
        if (request.Uninstall || !Engine.HasPayload)
        {
            string? appDir = request.AppDir;
            if (appDir == null)
            {
                string here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                if (File.Exists(Path.Combine(here, "MusicBar.exe")))
                {
                    // Running from the folder about to be removed: a copy in TEMP takes over.
                    if (Engine.HandOffToTemp(here, e.Args))
                    {
                        Shutdown(0);
                        return;
                    }
                    appDir = here;
                }
                else
                {
                    appDir = Engine.FindInstalled()?.Dir;
                }
            }
            if (request.Silent || request.VerySilent)
            {
                int code = appDir == null ? 1 : Engine.UninstallAsync(appDir).Result;
                if (code == 0 && request.Purge) Engine.Purge();
                Engine.RemoveSelfLater();
                Shutdown(code < 0 ? 1 : code);
                return;
            }
            Open(new MainWindow(Flow.Uninstall, request, lang, appDir));
            return;
        }

        if (request.VerySilent)
        {
            string? payload = null;
            int code;
            try
            {
                payload = Engine.ExtractPayload();
                code = Engine.RunAsync(payload, request.Tail).Result;
            }
            catch
            {
                code = 1;
            }
            finally
            {
                Engine.Discard(payload);
            }
            Shutdown(code);
            return;
        }

        var installed = Engine.FindInstalled();
        Open(new MainWindow(request.Silent ? Flow.QuietUpdate : installed != null ? Flow.Update : Flow.Install, request, lang, installed?.Dir));
    }

    void Open(MainWindow window)
    {
        MainWindow = window;
        window.Show();
    }
}
