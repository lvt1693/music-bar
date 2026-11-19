using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace MusicBar.Infrastructure;

/// <summary>
/// A package identity for MusicBar on Windows 10, which lets only packaged apps read the notification center (#21).
/// MusicBar stays an ordinary program installed by its own setup; next to it ships a small "sparse" package (just a
/// manifest: identity, capabilities, logos) registered with the install folder as its external location. Windows then
/// starts MusicBar.exe, whose own manifest names that package (the msix element in app.manifest), with the package's
/// identity: it shows up in Settings → Privacy → Notifications and may ask for access. Nothing else changes for it
/// (win32App behavior: no file or registry virtualization). Windows 11 lets desktop apps in without any of this, so
/// the package is only registered on Windows 10.
/// The package has to be signed by a certificate Windows trusts for the whole machine; a self-made test certificate
/// must first be put in the machine's Trusted People store (the test setup does that, with an admin prompt).
/// </summary>
public static class PackageIdentity
{
    /// <summary>The package's name and publisher, as in installer\identity\AppxManifest.xml and app.manifest. The
    /// publisher is the subject of the certificate that signs the package (a test one for now).</summary>
    public const string Name = "MusicBar";
    public const string Publisher = "CN=MusicBar Test";

    static bool? _has;

    /// <summary>This process runs with the package's identity.</summary>
    public static bool Has => _has ??= Probe();

    /// <summary>Windows 10 (MUSICBAR_IDENTITY=1 to try it on Windows 11).</summary>
    public static bool Wanted => Environment.OSVersion.Version.Build < 22000 || Environment.GetEnvironmentVariable("MUSICBAR_IDENTITY") == "1";

    static string Folder => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>The signed package that comes with the setup (none in the portable zip, nor in a build without one).</summary>
    static string PackageFile => Path.Combine(Folder, "identity", "MusicBar.Identity.msix");

    static bool Probe()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Registers the package for this install folder (setup runs MusicBar with --register-identity before opening it,
    /// so the first start already has the identity). Logs why when Windows says no.
    /// </summary>
    public static async Task<bool> RegisterAsync()
    {
        if (!Wanted) return false;
        if (!File.Exists(PackageFile))
        {
            Log.Info("Package identity: no package next to MusicBar");
            return false;
        }
        try
        {
            var options = new AddPackageOptions
            {
                ExternalLocationUri = new Uri(Folder.EndsWith('\\') ? Folder : Folder + "\\"),
                ForceUpdateFromAnyVersion = true,
            };
            var result = await new PackageManager().AddPackageByUriAsync(new Uri(PackageFile), options);
            if (result.IsRegistered)
            {
                Log.Info($"Package identity registered for {Folder}");
                return true;
            }
            Log.Info($"Package identity not registered: 0x{result.ExtendedErrorCode?.HResult:X8} {result.ErrorText}"
                     + (result.ExtendedErrorCode?.HResult == unchecked((int)0x800B0109) ? " (the package's certificate isn't trusted on this PC)" : ""));
            return false;
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x800B0109))
        {
            Log.Info("Package identity not registered: the package's certificate isn't trusted on this PC (0x800B0109)");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Registering the package identity failed", ex);
            return false;
        }
    }

    /// <summary>Whether the package is registered for this user (MusicBar may still run without it until restarted).</summary>
    public static bool Registered()
    {
        try
        {
            return new PackageManager().FindPackagesForUser("", Name, Publisher).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Takes the package away again (uninstall).</summary>
    public static async Task UnregisterAsync()
    {
        try
        {
            var manager = new PackageManager();
            foreach (var package in manager.FindPackagesForUser("", Name, Publisher).ToList())
            {
                await manager.RemovePackageAsync(package.Id.FullName);
                Log.Info($"Package identity removed: {package.Id.FullName}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Removing the package identity failed", ex);
        }
    }
}
