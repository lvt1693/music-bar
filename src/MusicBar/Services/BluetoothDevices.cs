using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace MusicBar.Services;

/// <summary>What a Bluetooth device is, for its icon and for whether it can be connected from here.</summary>
public enum BluetoothKind { Audio, Keyboard, Mouse, Phone, Computer, Other }

/// <param name="Battery">Percent left, for a connected device that tells Windows (see <see cref="BluetoothBattery"/>).</param>
public sealed record BluetoothItem(string Id, string Name, string Address, bool Paired, bool Connected, BluetoothKind Kind, int? Battery = null);

/// <summary>A pairing step that needs the user: what kind, and the PIN Windows shows (if any).</summary>
public sealed record PairingPrompt(DevicePairingKinds Kind, string? Pin);

/// <summary>
/// Paired Bluetooth devices and the ones nearby. While <see cref="Start"/>ed, an association-endpoint
/// watcher keeps the list current and makes the radio look for new devices, the way Windows'
/// own "Add device" does. Pairing runs here (custom pairing), so PINs show in MusicBar's panel.
/// </summary>
public sealed class BluetoothDevices
{
    const string Classic = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
    const string LowEnergy = "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";
    const string IsPaired = "System.Devices.Aep.IsPaired";
    const string IsConnected = "System.Devices.Aep.IsConnected";
    const string IsPresent = "System.Devices.Aep.IsPresent";
    const string Address = "System.Devices.Aep.DeviceAddress";
    const string Major = "System.Devices.Aep.Bluetooth.Cod.Major";
    const string Minor = "System.Devices.Aep.Bluetooth.Cod.Minor";
    const string ContainerId = "System.Devices.Aep.ContainerId";

    readonly Dispatcher _ui;
    readonly Dictionary<string, DeviceInformation> _devices = [];
    readonly DispatcherTimer _batteryPoll;
    Dictionary<Guid, int> _battery = [];
    bool _batteryFailed;
    DeviceWatcher? _watcher;

    public BluetoothDevices(Dispatcher ui)
    {
        _ui = ui;
        // Devices send their level now and then; Windows' own panel isn't faster either.
        _batteryPoll = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => RefreshBattery(), ui);
        _batteryPoll.Stop();
    }

    /// <summary>The list changed; raised on the UI thread.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_watcher != null) return;
        _devices.Clear();
        try
        {
            _watcher = Watcher([IsPaired, IsConnected, IsPresent, Address, Major, Minor, ContainerId]);
        }
        catch (Exception ex)
        {
            // An older Windows without the class-of-device properties: names and states are enough.
            Log.Error("Bluetooth watcher with device classes failed", ex);
            _watcher = Watcher([IsPaired, IsConnected, IsPresent, Address, ContainerId]);
        }
        RefreshBattery();
        _batteryPoll.Start();
        _watcher.Added += (_, info) => OnUi(() => _devices[info.Id] = info);
        _watcher.Updated += (_, update) => OnUi(() =>
        {
            if (_devices.TryGetValue(update.Id, out var info)) info.Update(update);
        });
        _watcher.Removed += (_, update) => OnUi(() => _devices.Remove(update.Id));
        _watcher.Start();
    }

    public void Stop()
    {
        _batteryPoll.Stop();
        var watcher = _watcher;
        _watcher = null;
        if (watcher == null) return;
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
        }
        catch (Exception ex)
        {
            Log.Error("Stopping the Bluetooth watcher failed", ex);
        }
    }

    static DeviceWatcher Watcher(string[] properties)
        => DeviceInformation.CreateWatcher($"({Classic}) OR ({LowEnergy})", properties, DeviceInformationKind.AssociationEndpoint);

    void OnUi(Action change) => _ui.BeginInvoke(() =>
    {
        change();
        Changed?.Invoke();
    });

    /// <summary>The battery levels Windows holds, read on a pool thread (a walk over the Bluetooth device nodes).</summary>
    async void RefreshBattery()
    {
        try
        {
            var levels = await Task.Run(BluetoothBattery.Read);
            bool same = levels.Count == _battery.Count && levels.All(l => _battery.TryGetValue(l.Key, out int v) && v == l.Value);
            _battery = levels;
            if (!same) Changed?.Invoke();
        }
        catch (Exception ex)
        {
            if (!_batteryFailed) Log.Error("Reading Bluetooth battery levels failed", ex);
            _batteryFailed = true;
        }
    }

    /// <summary>
    /// Paired devices (connected first), then named devices in range that could be paired. A device
    /// that shows up twice (classic and Low Energy) is listed once, as the paired one.
    /// </summary>
    public IReadOnlyList<BluetoothItem> Items()
    {
        var items = _devices.Values.Select(ToItem).Where(i => i != null).Select(i => i!).ToList();
        var pairedNames = items.Where(i => i.Paired).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items
            .Where(i => i.Paired || !pairedNames.Contains(i.Name))
            .GroupBy(i => (i.Name.ToLowerInvariant(), i.Paired))
            .Select(g => g.OrderByDescending(i => i.Connected).First())
            .OrderByDescending(i => i.Paired)
            .ThenByDescending(i => i.Connected)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    BluetoothItem? ToItem(DeviceInformation info)
    {
        bool paired = Flag(info, IsPaired) || info.Pairing.IsPaired;
        // Unnamed or out-of-range strangers are noise: beacons, other people's phones walking by.
        if (!paired && (info.Name.Length == 0 || !Flag(info, IsPresent) || !info.Pairing.CanPair)) return null;
        string name = info.Name.Length > 0 ? info.Name : Loc.T("BT_Unnamed");
        string address = info.Properties.TryGetValue(Address, out var a) ? a as string ?? "" : "";
        bool connected = Flag(info, IsConnected);
        // Windows keeps a device's last level after it leaves: only a connected one's is current.
        int? battery = connected && info.Properties.TryGetValue(ContainerId, out var c) && c is Guid container
                       && _battery.TryGetValue(container, out int level) ? level : null;
        return new BluetoothItem(info.Id, name, address, paired, connected, KindOf(info), battery);
    }

    static bool Flag(DeviceInformation info, string key) => info.Properties.TryGetValue(key, out var v) && v is true;

    /// <summary>From the Bluetooth class of device: the major class, and for peripherals the minor bits.</summary>
    static BluetoothKind KindOf(DeviceInformation info)
    {
        int major = info.Properties.TryGetValue(Major, out var m) && m != null ? Convert.ToInt32(m) : -1;
        int minor = info.Properties.TryGetValue(Minor, out var n) && n != null ? Convert.ToInt32(n) : 0;
        return major switch
        {
            1 => BluetoothKind.Computer,
            2 => BluetoothKind.Phone,
            4 => BluetoothKind.Audio,
            5 when (minor & 0x10) != 0 => BluetoothKind.Keyboard,
            5 when (minor & 0x20) != 0 => BluetoothKind.Mouse,
            _ => BluetoothKind.Other,
        };
    }

    /// <summary>
    /// Pairs with the device. <paramref name="ask"/> runs on the UI thread whenever a step needs the
    /// user (compare or type a PIN) and returns whether to go on, and the PIN typed if one was asked.
    /// </summary>
    public async Task<bool> PairAsync(string id, Func<PairingPrompt, Task<(bool Accept, string? Pin)>> ask)
    {
        if (!_devices.TryGetValue(id, out var info)) return false;
        var custom = info.Pairing.Custom;
        TypedEventHandler<DeviceInformationCustomPairing, DevicePairingRequestedEventArgs> requested = async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                // Just confirming is what the click on "Pair" already did.
                if (args.PairingKind == DevicePairingKinds.ConfirmOnly)
                {
                    args.Accept();
                    return;
                }
                var prompt = new PairingPrompt(args.PairingKind, args.Pin);
                var (accept, pin) = await _ui.InvokeAsync(() => ask(prompt)).Task.Unwrap();
                if (!accept) return;
                if (args.PairingKind == DevicePairingKinds.ProvidePin) args.Accept(pin ?? "");
                else args.Accept();
            }
            catch (Exception ex)
            {
                Log.Error("Answering a Bluetooth pairing request failed", ex);
            }
            finally
            {
                deferral.Complete();
            }
        };
        custom.PairingRequested += requested;
        try
        {
            var kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin
                        | DevicePairingKinds.ProvidePin | DevicePairingKinds.ConfirmPinMatch;
            var result = await custom.PairAsync(kinds, DevicePairingProtectionLevel.Default);
            Log.Info($"Bluetooth pair '{info.Name}': {result.Status}");
            return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
        }
        catch (Exception ex)
        {
            Log.Error($"Pairing '{info.Name}' failed", ex);
            return false;
        }
        finally
        {
            custom.PairingRequested -= requested;
        }
    }

    /// <summary>Removes the device: Windows forgets the pairing, as "Remove device" in Settings.</summary>
    public async Task<bool> UnpairAsync(string id)
    {
        if (!_devices.TryGetValue(id, out var info)) return false;
        try
        {
            var result = await info.Pairing.UnpairAsync();
            Log.Info($"Bluetooth remove '{info.Name}': {result.Status}");
            return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
        }
        catch (Exception ex)
        {
            Log.Error($"Removing '{info.Name}' failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Connects or disconnects a paired audio device (headphones, speakers). Windows has no public
    /// call for this; <see cref="BluetoothAudioLink"/> asks the Bluetooth audio driver directly.
    /// </summary>
    public static Task<bool> SetConnectedAsync(BluetoothItem item, bool connect)
        => Task.Run(() => BluetoothAudioLink.SetConnected(item.Address.Length > 0 ? item.Address : item.Id, connect));

    /// <summary>Whether the device has audio endpoints MusicBar can connect and disconnect.</summary>
    public static Task<bool> CanConnectAsync(BluetoothItem item)
        => Task.Run(() => BluetoothAudioLink.IsAudio(item.Address.Length > 0 ? item.Address : item.Id));
}
