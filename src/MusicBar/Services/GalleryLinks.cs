using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>
/// musicbar:// links, so the website's "Apply" button hands a gallery item to MusicBar, which downloads it
/// if it doesn't have it yet and puts it on the taskbar: musicbar://apply/&lt;slug&gt; (install/ still works). The scheme is registered for this user alone (no admin rights),
/// pointing at this copy of MusicBar, each time it starts; the uninstaller takes it away again.
/// A link only ever names an item of the gallery, which is fetched from the gallery and checked like any other.
/// </summary>
public static partial class GalleryLinks
{
    const string Scheme = "musicbar";
    const string KeyPath = @"Software\Classes\" + Scheme;

    /// <summary>Where the second copy leaves a link for the running one.</summary>
    public static string Pending => Path.Combine(AppPaths.DataDir, "pending-link.txt");

    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("", "URL:MusicBar");
            key.SetValue("URL Protocol", "");
            using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            var wanted = $"\"{exe}\" \"%1\"";
            if (command.GetValue("") as string != wanted)
            {
                command.SetValue("", wanted);
                Log.Info($"musicbar:// links open {exe}");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("Registering musicbar:// links failed", ex);
        }
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("Removing musicbar:// links failed", ex);
        }
    }

    /// <summary>The gallery item a link asks for (musicbar://install/&lt;slug&gt;), or null when it asks for nothing MusicBar does.</summary>
    public static string? SlugOf(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        var m = LinkPattern().Match(link.Trim().TrimEnd('/'));
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static bool IsLink(string arg) => arg.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^musicbar:/{0,2}(?:install|gallery|apply)/([A-Za-z0-9-]{1,60})$", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
