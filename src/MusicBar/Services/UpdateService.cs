using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>A release newer than this copy; <see cref="SetupFallback"/> is GitHub's copy of the installer when <see cref="Setup"/> is the website's.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string Page, Uri? Setup, long SetupSize, string? SetupSha256, Uri? SetupFallback = null);

/// <summary>
/// Looks for a newer MusicBar and installs it: the installed copy downloads the new installer and
/// runs it quietly (it closes MusicBar and opens the new one); a portable copy is pointed at the
/// download page instead. Asked of the website first, which mirrors the releases (each file checked
/// against GitHub's checksum) and is the page people know; of GitHub when the site doesn't answer.
/// </summary>
public sealed class UpdateService
{
    const string LatestRelease = "https://api.github.com/repos/lvt1693/music-bar/releases/latest";
    static string SiteRelease => Community.Server + "/api/v1/release";

    readonly AppSettings _settings;

    public UpdateService(AppSettings settings) => _settings = settings;

    /// <summary>This copy's version (major.minor.build).</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Installed by the installer (its uninstaller sits beside the app), rather than unzipped.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    HttpClient Client(TimeSpan timeout)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (!string.IsNullOrWhiteSpace(_settings.ProxyUrl))
        {
            handler.Proxy = new WebProxy(_settings.ProxyUrl);
            handler.UseProxy = true;
        }
        var http = new HttpClient(handler) { Timeout = timeout };
        // GitHub's API turns away requests without a user agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicBar/{Current} (+https://github.com/lvt1693/music-bar)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The latest release if it is newer than this copy, null when there is none (or neither the site nor GitHub can be reached).</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct)
    {
        var (answered, release) = await CheckSiteAsync(ct);
        return answered ? release : await CheckGitHubAsync(ct);
    }

    /// <summary>The website's mirror of the latest release; not answered when the site is down or has nothing mirrored yet.</summary>
    async Task<(bool Answered, ReleaseInfo? Release)> CheckSiteAsync(CancellationToken ct)
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(SiteRelease, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"Update check: the website answered {(int)response.StatusCode}, asking GitHub");
                return (false, null);
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return (false, null);
            version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            if (version <= Current) return (true, null);

            Uri? setup = null, fallback = null;
            long size = 0;
            string? sha = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                setup = new Uri($"{Community.Server}/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");
                size = asset.GetProperty("size").GetInt64();
                sha = asset.TryGetProperty("sha256", out var s) && s.GetString() is { Length: 64 } hex ? hex : null;
                if (asset.TryGetProperty("github_url", out var g) && Uri.TryCreate(g.GetString(), UriKind.Absolute, out var github)) fallback = github;
            }
            string notes = root.TryGetProperty("notes", out var body) ? body.GetString() ?? "" : "";
            Log.Info($"Update check: {tag} is out (this is {Current}), from the website");
            return (true, new ReleaseInfo(version, tag, notes, Community.Server + "/download", setup, size, sha, fallback));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or System.Collections.Generic.KeyNotFoundException
                                       or InvalidOperationException or UriFormatException)
        {
            Log.Info($"Update check: the website didn't answer ({ex.GetType().Name}), asking GitHub");
            return (false, null);
        }
    }

    async Task<ReleaseInfo?> CheckGitHubAsync(CancellationToken ct)
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(20));
            using var response = await http.GetAsync(LatestRelease, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"Update check: GitHub answered {(int)response.StatusCode}");
                return null;
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            if (version <= Current) return null;

            Uri? setup = null;
            long size = 0;
            string? sha = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                setup = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
                size = asset.GetProperty("size").GetInt64();
                // GitHub publishes each asset's digest ("sha256:…"); check the download against it when there is one.
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = d[7..];
            }
            string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            string page = root.GetProperty("html_url").GetString() ?? "https://github.com/lvt1693/music-bar/releases/latest";
            Log.Info($"Update check: {tag} is out (this is {Current})");
            return new ReleaseInfo(version, tag, notes, page, setup, size, sha);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            return null;
        }
    }

    /// <summary>Downloads the release's installer to a temporary folder (from GitHub when the site's copy fails); null when both failed or didn't match.</summary>
    public async Task<string?> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        if (release.Setup == null) return null;
        var path = await DownloadAsync(release, release.Setup, progress, ct);
        if (path != null || release.SetupFallback == null || ct.IsCancellationRequested) return path;
        Log.Info($"Update {release.Tag}: downloading from GitHub instead");
        progress.Report(0);
        return await DownloadAsync(release, release.SetupFallback, progress, ct);
    }

    async Task<string?> DownloadAsync(ReleaseInfo release, Uri from, IProgress<double> progress, CancellationToken ct)
    {
        string folder = Path.Combine(Path.GetTempPath(), "MusicBar-update");
        string path = Path.Combine(folder, Uri.UnescapeDataString(Path.GetFileName(from.LocalPath)));
        try
        {
            Directory.CreateDirectory(folder);
            using var http = Client(TimeSpan.FromMinutes(10));
            using var response = await http.GetAsync(from, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.SetupSize;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(path))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress.Report((double)done / total);
                }
            }
            if (release.SetupSha256 is { } expected)
            {
                string actual;
                await using (var check = File.OpenRead(path))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"Update download of {release.Tag} did not match its published checksum");
                    File.Delete(path);
                    return null;
                }
            }
            Log.Info($"Update {release.Tag} downloaded to {path}");
            return path;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Downloading update {release.Tag} failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Runs the installer quietly: it shows its progress, closes MusicBar, installs over this copy
    /// and opens the new one. False if Windows wouldn't start it.
    /// </summary>
    public static bool Install(string installer)
    {
        try
        {
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Starting the update installer failed", ex);
            return false;
        }
    }

    /// <summary>
    /// The part of the release notes worth showing: the "Mới / New" and "Sửa lỗi / Fixed" sections in
    /// Vietnamese, the "In English" paragraph in English, as plain text. Release notes keep that shape.
    /// </summary>
    public static string Summary(string notes, AppLanguage language)
    {
        string text = notes.Replace("\r\n", "\n");
        if (language == AppLanguage.English)
        {
            var english = Regex.Match(text, @"\*\*In English:\*\*\s*(.+?)(\n\n|$)", RegexOptions.Singleline);
            if (english.Success) return Plain(english.Groups[1].Value);
        }
        int start = text.IndexOf("**Mới**", StringComparison.Ordinal);
        if (start < 0) start = text.IndexOf("**Sửa lỗi**", StringComparison.Ordinal);
        int end = text.IndexOf("**In English", StringComparison.Ordinal);
        if (start >= 0) text = end > start ? text[start..end] : text[start..];
        return Plain(text);
    }

    static string Plain(string markdown)
    {
        var lines = markdown.Split('\n')
            .Select(l => l.TrimEnd())
            .Select(l => Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1"))
            .Select(l => l.Replace("**", "").Replace("`", ""))
            .Select(l => Regex.Replace(l, @"^(\s*)[-*] ", "$1• "))
            .Select(l => l.TrimStart('#', ' ') is var t && l.StartsWith('#') ? t : l);
        string text = Regex.Replace(string.Join("\n", lines).Trim(), @"\n{3,}", "\n\n");
        return text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : text;
    }
}
