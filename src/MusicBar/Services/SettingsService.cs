using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

public sealed class SettingsService
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new StyleConverter<SeekStyle>(SeekStyle.Aurora),
            new StyleConverter<ControlStyle>(ControlStyle.Glow),
            new StyleConverter<AppLanguage>(AppLanguage.English),
            new StyleConverter<DisplayTarget>(DisplayTarget.Main),
            new StyleConverter<SeekDirection>(SeekDirection.FromStart),
            new StyleConverter<PlayerTarget>(PlayerTarget.SameAsBar),
            new StyleConverter<PlayerMode>(PlayerMode.Full),
            new StyleConverter<BarBackground>(BarBackground.Auto),
            new StyleConverter<Views.Face.FaceStyle>(Views.Face.FaceStyle.Classic),
            new StyleConverter<FacePlacement>(FacePlacement.BesidePlayer),
            new JsonStringEnumConverter(),
        },
    };

    /// <summary>A style removed (or not yet known) in another version loads as the default instead of failing the whole file.</summary>
    sealed class StyleConverter<T>(T fallback) : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String
               && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var style)
               && Enum.IsDefined(style)
                ? style
                : fallback;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public SettingsService()
    {
        Current = Load(out var firstRun);
        IsFirstRun = firstRun;
        Current.PropertyChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public AppSettings Current { get; }
    public bool IsFirstRun { get; }

    static AppSettings Load(out bool firstRun)
    {
        firstRun = !File.Exists(AppPaths.SettingsFile);
        if (firstRun) return new AppSettings();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var settings = Parse(AppPaths.SettingsFile);
                // The last copy known to be good, for the day the file is damaged.
                try
                {
                    File.Copy(AppPaths.SettingsFile, AppPaths.SettingsBackupFile, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error("Backing up the settings failed", ex);
                }
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not damaged, just not readable now (another program has it open for a moment): wait a little.
                if (attempt < 3)
                {
                    Thread.Sleep(100);
                    continue;
                }
                Log.Error("Settings file could not be read; using the last good copy for this run, the file is left as it is", ex);
                return Recover(damaged: false);
            }
            catch (Exception ex)
            {
                Log.Error("Settings file unreadable", ex);
                return Recover(damaged: true);
            }
        }
    }

    static AppSettings Parse(string path)
        => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
           ?? throw new InvalidDataException("The settings file holds no settings");

    /// <summary>
    /// Keeps the damaged file (never moves it: its absence would read as a first run), then goes back to the
    /// last good copy, or to defaults when there is none. Nothing here may stop the app from starting.
    /// With <paramref name="damaged"/> false the file was only unreadable for now: it is not touched at all.
    /// </summary>
    static AppSettings Recover(bool damaged)
    {
        bool kept = damaged && KeepBadCopy();
        AppSettings? settings = null;
        try
        {
            if (File.Exists(AppPaths.SettingsBackupFile)) settings = Parse(AppPaths.SettingsBackupFile);
        }
        catch (Exception ex)
        {
            Log.Error("The settings backup is unreadable too", ex);
        }
        if (settings == null)
        {
            Log.Info("Using default settings" + (kept ? "; the damaged file is kept next to it as settings.bad-*.json" : ""));
            return new AppSettings();
        }
        Log.Info("Settings restored from settings.bak" + (kept ? "; the damaged file is kept next to it as settings.bad-*.json" : ""));
        // Put the good copy in place so the next start does not find the damaged file again before anything is saved,
        // but only once the damaged file is safe: otherwise this would destroy the only copy of the latest changes.
        if (!kept) return settings;
        try
        {
            File.Copy(AppPaths.SettingsBackupFile, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Putting the settings backup back failed", ex);
        }
        return settings;
    }

    /// <summary>True when the damaged file is safe in a copy (one that was already there, identical, counts).</summary>
    static bool KeepBadCopy()
    {
        try
        {
            var bad = File.ReadAllBytes(AppPaths.SettingsFile);
            var copies = new DirectoryInfo(AppPaths.DataDir).GetFiles(AppPaths.BadSettingsPattern)
                .OrderByDescending(f => f.Name).ToList();
            // Starting again with the same damaged file would only push the older copies out.
            if (copies.Count == 0 || !File.ReadAllBytes(copies[0].FullName).AsSpan().SequenceEqual(bad.AsSpan()))
            {
                File.WriteAllBytes(AppPaths.BadSettingsFile(DateTime.Now), bad);
                copies = new DirectoryInfo(AppPaths.DataDir).GetFiles(AppPaths.BadSettingsPattern)
                    .OrderByDescending(f => f.Name).ToList();
            }
            try
            {
                foreach (var old in copies.Skip(3)) old.Delete();
            }
            catch (Exception ex)
            {
                Log.Error("Removing old copies of the damaged settings failed", ex);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Keeping a copy of the damaged settings failed", ex);
            return false;
        }
    }

    public void SaveNow()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
        }
    }
}
