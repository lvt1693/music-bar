using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>What VLC is playing, as its web interface tells it.</summary>
public sealed record VlcStatus(string State, TimeSpan Position, TimeSpan Length, double Rate, int ItemId,
    string Title, string Artist, string Album, string ArtUrl)
{
    public bool Playing => State == "playing";
    public bool Stopped => State == "stopped";
}

/// <summary>
/// VLC 3 publishes no Windows media session, so MusicBar can't see it the usual way. VLC's own web
/// interface (Lua HTTP) tells what plays, where, and takes commands, and MusicBar talks to that.
/// Switched on in VLC's settings file (vlcrc) at the user's word, for this computer only
/// (127.0.0.1), on a port of MusicBar's and with a password of its own; settings the user made
/// there already are kept. VLC reads the file when it starts, so it has to restart once.
/// </summary>
public sealed class VlcLink : IDisposable
{
    public const string AppId = "vlc.exe";
    const int OurPort = 47328;
    const string Localhost = "127.0.0.1";

    static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlcrc");

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(900) };

    /// <summary>VLC is installed (the 64-bit or the 32-bit one).</summary>
    public static bool Installed
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\VideoLAN\VLC") ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\VideoLAN\VLC");
                if (key != null) return true;
            }
            catch (Exception)
            {
            }
            return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"))
                   || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"));
        }
    }

    public static bool Running
    {
        get
        {
            var all = Process.GetProcessesByName("vlc");
            foreach (var p in all) p.Dispose();
            return all.Length > 0;
        }
    }

    // ------------------------------------------------------------------ VLC's settings file

    /// <summary>The web interface's settings as VLC will read them: on or not, where it listens, the password.</summary>
    sealed record Config(bool On, string Host, int Port, string Password);

    // Read again only when the file changes: it is asked for every second, and is some 100 KB.
    static (DateTime Written, Config? Config) _cached;

    static Config? ReadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var written = File.GetLastWriteTimeUtc(ConfigPath);
            if (written == _cached.Written) return _cached.Config;
            var config = ParseConfig(File.ReadAllLines(ConfigPath, Encoding.UTF8));
            _cached = (written, config);
            return config;
        }
        catch (Exception ex)
        {
            Log.Error("Reading VLC's settings failed", ex);
            return null;
        }
    }

    static Config ParseConfig(string[] lines)
    {
        string section = "";
        string interfaces = "", host = "", password = "";
        int port = 8080;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) section = line[1..Math.Max(1, line.IndexOf(']'))];
            if (line.StartsWith('#') || !line.Contains('=')) continue;
            var (name, value) = (line[..line.IndexOf('=')].Trim(), line[(line.IndexOf('=') + 1)..].Trim());
            if (section == "core" && name == "extraintf") interfaces = value;
            else if (section == "core" && name == "http-host") host = value;
            else if (section == "core" && name == "http-port" && int.TryParse(value, out var p)) port = p;
            else if (section == "lua" && name == "http-password") password = value;
        }
        bool on = interfaces.Split(':', ',').Any(i => i.Trim() == "http");
        // Listening everywhere (or not said) reaches this computer too; a specific address is used as given.
        string reach = host.Length == 0 || host is "0.0.0.0" or "::" or "*" ? Localhost : host;
        return new Config(on, reach, port, password);
    }

    /// <summary>The web interface is switched on in VLC's settings, with a password (it answers once VLC has restarted).</summary>
    public static bool Enabled => ReadConfig() is { On: true, Password.Length: > 0 };

    /// <summary>
    /// Switches VLC's web interface on in its settings file: the interface added, and where nothing
    /// is set yet, this computer only, MusicBar's port and a password. A copy of the file is kept
    /// first. False when the file couldn't be written.
    /// </summary>
    public static bool Enable()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            if (!File.Exists(ConfigPath))
            {
                File.WriteAllText(ConfigPath,
                    $"[core]\nextraintf=http\nhttp-host={Localhost}\nhttp-port={OurPort}\n[lua]\nhttp-password={password}\n",
                    new UTF8Encoding(true));
                Log.Info("VLC's web interface switched on (new settings file)");
                return true;
            }

            var bytes = File.ReadAllBytes(ConfigPath);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            // VLC writes plain \n; a line ending in \r\n here and there (from an editor) stays as it is,
            // and the lines changed here are written VLC's way: a \r would end up inside the value.
            var lines = text.Split('\n').ToList();
            var backup = ConfigPath + ".musicbar-backup";
            if (!File.Exists(backup)) File.WriteAllBytes(backup, bytes);

            Set(lines, "core", "extraintf", value => value.Split(':').Any(i => i.Trim() == "http") ? value : value.Length == 0 ? "http" : value + ":http");
            Set(lines, "core", "http-host", value => value.Length == 0 ? Localhost : value);
            Set(lines, "core", "http-port", value => value.Length == 0 ? OurPort.ToString() : value);
            Set(lines, "lua", "http-password", value => value.Length == 0 ? password : value);

            var output = new UTF8Encoding(bom).GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\n", lines))).ToArray();
            File.WriteAllBytes(ConfigPath, output);
            Log.Info("VLC's web interface switched on");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Switching VLC's web interface on failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Sets a value in a section, from what is set now ("" when nothing is): over the line that
    /// sets it, else over its commented default, else just under the section's heading.
    /// </summary>
    static void Set(System.Collections.Generic.List<string> lines, string section, string name, Func<string, string> value)
    {
        int start = lines.FindIndex(l => l.TrimStart().StartsWith($"[{section}]", StringComparison.Ordinal));
        if (start < 0)
        {
            lines.Add($"[{section}]");
            lines.Add($"{name}={value("")}");
            return;
        }
        int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        int set = -1, commented = -1;
        for (int i = start + 1; i < end; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith(name + "=", StringComparison.Ordinal)) set = i;
            else if (line.StartsWith("#" + name + "=", StringComparison.Ordinal)) commented = i;
        }
        if (set >= 0)
        {
            var now = lines[set][(lines[set].IndexOf('=') + 1)..].Trim();
            lines[set] = $"{name}={value(now)}";
        }
        else if (commented >= 0) lines[commented] = $"{name}={value("")}";
        else lines.Insert(start + 1, $"{name}={value("")}");
    }

    // ------------------------------------------------------------------ talking to VLC

    HttpRequestMessage Request(Config config, string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{config.Host}:{config.Port}/requests/status.json{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + config.Password)));
        return request;
    }

    /// <summary>What VLC plays now; null when its web interface doesn't answer (not on, VLC not restarted, or closed).</summary>
    public async Task<VlcStatus?> ReadAsync()
    {
        if (ReadConfig() is not { On: true } config) return null;
        try
        {
            using var response = await _http.SendAsync(Request(config, ""));
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            double length = Number(root, "length");
            double position = Number(root, "position");
            double time = Number(root, "time");
            // "time" is whole seconds; the position (a share of the length) is finer.
            double seconds = length > 0 && position > 0 ? position * length : time;
            string title = "", artist = "", album = "", file = "", art = "";
            if (root.TryGetProperty("information", out var info) && info.TryGetProperty("category", out var category)
                && category.TryGetProperty("meta", out var meta))
            {
                title = Text(meta, "title");
                artist = Text(meta, "artist");
                album = Text(meta, "album");
                file = Text(meta, "filename");
                art = Text(meta, "artwork_url");
            }
            // A file without tags: its name, as "Artist - Title" when it reads like that.
            if (title.Length == 0 && file.Length > 0)
            {
                title = Path.GetFileNameWithoutExtension(file);
                int dash = title.IndexOf(" - ", StringComparison.Ordinal);
                if (artist.Length == 0 && dash > 0) (artist, title) = (title[..dash].Trim(), title[(dash + 3)..].Trim());
            }
            return new VlcStatus(Text(root, "state"), TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(length),
                Number(root, "rate") is > 0 and var rate ? rate : 1, (int)Number(root, "currentplid"), title, artist, album, art);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A command for the playlist: pl_pause (play / pause), pl_next, pl_previous, seek (with a value).</summary>
    public async Task<bool> SendAsync(string command, string? value = null)
    {
        if (ReadConfig() is not { On: true } config) return false;
        try
        {
            var query = "?command=" + command + (value != null ? "&val=" + Uri.EscapeDataString(value) : "");
            using var response = await _http.SendAsync(Request(config, query));
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error($"VLC command {command} failed", ex);
            return false;
        }
    }

    /// <summary>The cover of what plays: from VLC's art cache when it is a file there, else through the web interface.</summary>
    public async Task<BitmapSource?> ArtAsync(string artUrl)
    {
        try
        {
            byte[] bytes;
            if (artUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && new Uri(artUrl).LocalPath is var path && File.Exists(path))
            {
                bytes = await File.ReadAllBytesAsync(path);
            }
            else
            {
                if (ReadConfig() is not { On: true } config) return null;
                var request = Request(config, "");
                request.RequestUri = new Uri($"http://{config.Host}:{config.Port}/art");
                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;
                bytes = await response.Content.ReadAsByteArrayAsync();
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.DecodePixelWidth = 300;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static double Number(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static string Text(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public void Dispose() => _http.Dispose();
}
