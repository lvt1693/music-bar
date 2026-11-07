using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>
/// Switches MusicBar reads from its website at start and once a day: the Windows builds on which the taskbar helper
/// must stay out of Explorer, when a Windows update broke it, so the fix doesn't wait for a new release (tools that
/// live in Explorer, like StartAllBack, survive by reacting to each Windows build fast). The last answer is kept on
/// disk: with the site down or no connection that one holds, and with none ever read nothing is switched off.
/// </summary>
public static class RemoteFlags
{
    static string Cache => Path.Combine(AppPaths.DataDir, "flags.json");

    /// <summary>This Windows build is listed: the helper stays out of Explorer and the bar tints over the taskbar.</summary>
    public static bool HelperOff { get; private set; }

    public static void LoadCached()
    {
        try
        {
            if (File.Exists(Cache)) Apply(File.ReadAllText(Cache), save: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Reading the saved switches failed", ex);
        }
    }

    public static async Task RefreshAsync(AppSettings settings)
    {
        try
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(settings.ProxyUrl))
            {
                handler.Proxy = new WebProxy(settings.ProxyUrl);
                handler.UseProxy = true;
            }
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicBar/{UpdateService.Current}");
            Apply(await http.GetStringAsync($"{Community.Server}/api/v1/flags"), save: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            // The site is down or the PC offline: the last answer stays.
        }
    }

    static void Apply(string json, bool save)
    {
        int build = Environment.OSVersion.Version.Build;
        int ubr = Ubr();
        string? note = null;
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.TryGetProperty("helper_off", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in list.EnumerateArray())
                {
                    // An entry names a build, and optionally one of its updates (ubr); without it, every update.
                    if (!e.TryGetProperty("build", out var b) || b.ValueKind != JsonValueKind.Number || b.GetInt32() != build) continue;
                    if (e.TryGetProperty("ubr", out var u) && u.ValueKind == JsonValueKind.Number && u.GetInt32() != ubr) continue;
                    note = e.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    break;
                }
            }
        }
        if (save) File.WriteAllText(Cache, json);
        bool off = note != null;
        if (off == HelperOff) return;
        HelperOff = off;
        Log.Info(off
            ? $"The website keeps the taskbar helper out of Explorer on Windows {build}.{ubr}: {note}"
            : $"The website lets the taskbar helper back on Windows {build}.{ubr}");
    }

    static int Ubr()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return key?.GetValue("UBR") is int ubr ? ubr : 0;
    }
}
