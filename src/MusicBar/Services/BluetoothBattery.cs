using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MusicBar.Services;

/// <summary>
/// The battery levels Bluetooth devices report to Windows, the ones Settings → Bluetooth shows (#20): headsets and
/// speakers through the hands-free profile, Low Energy devices (mice, keyboards, earbuds) through their battery
/// service. Windows keeps the last level on the device's own nodes, BTHENUM for classic Bluetooth and BTHLE for Low
/// Energy, as a property {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2 holding a byte in percent. The nodes and the entries
/// of the Bluetooth list share a container id (the physical device), which is how the two are matched.
/// Windows keeps the level after the device disconnects, so it means something only for a connected device.
/// </summary>
public static class BluetoothBattery
{
    static readonly Guid LevelSet = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    static readonly Guid ContainerSet = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");   // DEVPKEY_Device_ContainerId
    const uint DigcfPresent = 0x2, DigcfAllClasses = 0x4;
    const uint TypeByte = 0x3, TypeUInt32 = 0x7, TypeGuid = 0xD;

    /// <summary>Battery percent by container id; the lowest, when a device reports it on several of its nodes.</summary>
    public static Dictionary<Guid, int> Read()
    {
        var levels = new Dictionary<Guid, int>();
        foreach (var enumerator in new[] { "BTHENUM", "BTHLE" })
        {
            foreach (var (container, value) in ReadProperty(enumerator, LevelSet, 2))
            {
                if (value is < 0 or > 100) continue;
                levels[container] = levels.TryGetValue(container, out int other) ? Math.Min(other, value) : value;
            }
        }
        return levels;
    }

    /// <summary>
    /// A number property of the present devices one enumerator made (BTHENUM, USB…), with each device's container
    /// id. Separate from <see cref="Read"/> so that it can be tried on a PC without Bluetooth.
    /// </summary>
    public static List<(Guid Container, int Value)> ReadProperty(string enumerator, Guid set, uint id)
    {
        var found = new List<(Guid, int)>();
        IntPtr devices = SetupDiGetClassDevsW(IntPtr.Zero, enumerator, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (devices == new IntPtr(-1)) return found;
        try
        {
            var data = new SpDevinfoData { Size = (uint)Marshal.SizeOf<SpDevinfoData>() };
            var buffer = new byte[16];
            for (uint i = 0; SetupDiEnumDeviceInfo(devices, i, ref data); i++)
            {
                var key = new DevPropKey { Set = set, Id = id };
                if (!SetupDiGetDevicePropertyW(devices, ref data, ref key, out uint type, buffer, (uint)buffer.Length, out _, 0)) continue;
                int? value = type switch
                {
                    TypeByte => buffer[0],
                    TypeUInt32 => (int)Math.Min(BitConverter.ToUInt32(buffer, 0), int.MaxValue),
                    _ => null,
                };
                if (value == null) continue;
                key = new DevPropKey { Set = ContainerSet, Id = 2 };
                if (!SetupDiGetDevicePropertyW(devices, ref data, ref key, out type, buffer, (uint)buffer.Length, out _, 0) || type != TypeGuid) continue;
                found.Add((new Guid(buffer), value.Value));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devices);
        }
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DevPropKey
    {
        public Guid Set;
        public uint Id;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr devices, uint index, ref SpDevinfoData data);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiGetDevicePropertyW(IntPtr devices, ref SpDevinfoData data, ref DevPropKey key, out uint type,
        byte[] buffer, uint size, out uint required, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
}
