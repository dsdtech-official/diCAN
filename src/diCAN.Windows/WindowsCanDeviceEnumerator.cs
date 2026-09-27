using System.Runtime.Versioning;
using DiCAN.Core.Devices;
using DiCAN.Windows.Interop;
using Microsoft.Win32;

namespace DiCAN.Windows;

// Discovers USB CAN adapters.
[SupportedOSPlatform("windows")]
public sealed class WindowsCanDeviceEnumerator : ICanDeviceEnumerator
{
    private const string EnumRoot = @"SYSTEM\CurrentControlSet\Enum\";
    private const string UsbEnumerator = "USB";

    // Counts the requested items.
    public static int CountPresentUsbDevices() =>
        CfgMgr32.GetPresentDeviceIds(UsbEnumerator)
            .Count(id => WindowsDeviceId.TryParse(id, out _));

    // Enumerates all usb devices.
    public static IReadOnlyList<(string DeviceId, ushort VendorId, ushort ProductId, CanDeviceKind Kind, string? Name, string? Parent)>
        EnumerateAllUsbDevices()
    {
        var results =
            new List<(string, ushort, ushort, CanDeviceKind, string?, string?)>();

        foreach (string deviceId in CfgMgr32.GetPresentDeviceIds(UsbEnumerator))
        {
            if (!WindowsDeviceId.TryParse(deviceId, out ParsedDeviceId parsed))
            {

                continue;
            }

            results.Add((
                deviceId,
                parsed.VendorId,
                parsed.ProductId,
                CanUsbIds.Classify(parsed.VendorId, parsed.ProductId),
                ReadDescription(deviceId),

                CfgMgr32.GetParentDeviceId(deviceId)));
        }

        return results;
    }

    // Lists the available items.
    public IReadOnlyList<CanDeviceInfo> Enumerate()
    {
        string[] presentIds = CfgMgr32.GetPresentDeviceIds(UsbEnumerator);
        if (presentIds.Length == 0)
        {
            return [];
        }

        var results = new List<CanDeviceInfo>();

        var parentCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Gets parent of.
        string? ParentOf(string id)
        {
            if (!parentCache.TryGetValue(id, out string? parent))
            {
                parent = CfgMgr32.GetParentDeviceId(id);
                parentCache[id] = parent;
            }

            return parent;
        }

        foreach (string deviceId in presentIds)
        {
            if (!WindowsDeviceId.TryParse(deviceId, out ParsedDeviceId parsed))
            {
                continue;
            }

            CanDeviceKind kind = CanUsbIds.Classify(parsed.VendorId, parsed.ProductId);
            if (kind == CanDeviceKind.Unknown)
            {
                continue;
            }

            if (parsed.InterfaceNumber is not null)
            {
                continue;
            }

            string? identityString =
                PickIdentityString(deviceId, presentIds, CfgMgr32.GetBusReportedDeviceDesc, ParentOf);
            string? description = FindDescription(deviceId, presentIds, ParentOf);

            if (CanExcludedProducts.IsExcluded(identityString, description))
            {
                continue;
            }

            CanFirmwareFacts firmware = CanFirmwareLines.Identify(
                parsed.VendorId,
                parsed.ProductId,
                WindowsDeviceId.ReadRevision(CfgMgr32.GetHardwareIds(deviceId)),
                identityString);

            results.Add(new CanDeviceInfo(
                Kind: kind,
                VendorId: parsed.VendorId,
                ProductId: parsed.ProductId,
                SerialNumber: parsed.SerialNumber,
                PortName: FindPortName(deviceId, presentIds, ParentOf),
                DeviceId: deviceId,
                Description: description ?? kind.ToString(),
                FirmwareLine: firmware.Line,
                ChipFamily: firmware.Chip,
                FirmwareVersion: firmware.Version));
        }

        return results;
    }

    // Manages device name.
    public readonly record struct DeviceNameComparison(
        string DeviceId, string? Cached, string? Reported)
    {

        public bool Differs =>
            Reported is not null && !string.Equals(Cached, Reported, StringComparison.Ordinal);
    }

    // Compares the requested values.
    public static IReadOnlyList<DeviceNameComparison> CompareDeviceNames()
    {
        string[] presentIds = CfgMgr32.GetPresentDeviceIds(UsbEnumerator);
        if (presentIds.Length == 0)
        {
            return [];
        }

        var parentCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Gets parent of.
        string? ParentOf(string id)
        {
            if (!parentCache.TryGetValue(id, out string? parent))
            {
                parent = CfgMgr32.GetParentDeviceId(id);
                parentCache[id] = parent;
            }

            return parent;
        }

        var results = new List<DeviceNameComparison>();

        foreach (string deviceId in presentIds)
        {
            if (!WindowsDeviceId.TryParse(deviceId, out ParsedDeviceId parsed)
                || CanUsbIds.Classify(parsed.VendorId, parsed.ProductId) == CanDeviceKind.Unknown
                || parsed.InterfaceNumber is not null)
            {
                continue;
            }

            results.Add(new DeviceNameComparison(
                deviceId,
                PickDescription(deviceId, presentIds, ReadDescription, ParentOf),
                PickDescription(deviceId, presentIds, CfgMgr32.GetBusReportedDeviceDesc, ParentOf)));
        }

        return results;
    }

    // Finds port name.
    private static string? FindPortName(
        string deviceId, IReadOnlyList<string> presentIds, Func<string, string?> parentOf) =>
        ReadPortName(deviceId) ?? PickFromChildren(deviceId, presentIds, ReadPortName, parentOf);

    // Selects a matching value.
    private static string? PickFromChildren(
        string deviceId,
        IReadOnlyList<string> presentIds,
        Func<string, string?> read,
        Func<string, string?> parentOf)
    {
        foreach (string candidate in Children(deviceId, presentIds, parentOf))
        {
            if (read(candidate) is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    // Lists child device nodes.
    private static IEnumerable<string> Children(
        string deviceId, IReadOnlyList<string> presentIds, Func<string, string?> parentOf)
    {
        foreach (string candidate in presentIds)
        {
            if (string.Equals(candidate, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(parentOf(candidate), deviceId, StringComparison.OrdinalIgnoreCase))
            {
                yield return candidate;
            }
        }
    }

    // Reads port name.
    private static string? ReadPortName(string deviceId)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
            EnumRoot + deviceId + @"\Device Parameters");

        return key?.GetValue("PortName") as string;
    }

    // Finds description.
    private static string? FindDescription(
        string deviceId, IReadOnlyList<string> presentIds, Func<string, string?> parentOf) =>
        PickDescription(deviceId, presentIds, ReadDescription, parentOf);

    // Selects a matching value.
    internal static string? PickIdentityString(
        string deviceId,
        IReadOnlyList<string> presentIds,
        Func<string, string?> read,
        Func<string, string?> parentOf) =>
        read(deviceId) is { Length: > 0 } own
            ? own
            : PickDescription(deviceId, presentIds, read, parentOf);

    // Selects a matching value.
    internal static string? PickDescription(
        string deviceId,
        IReadOnlyList<string> presentIds,
        Func<string, string?> read,
        Func<string, string?> parentOf)
    {
        string? fromAnyChild = null;

        foreach (string candidate in Children(deviceId, presentIds, parentOf))
        {
            if (read(candidate) is not { Length: > 0 } fromChild)
            {
                continue;
            }

            if (WindowsDeviceId.TryParse(candidate, out ParsedDeviceId child) &&
                child.InterfaceNumber == 0)
            {
                return fromChild;
            }

            fromAnyChild ??= fromChild;
        }

        return fromAnyChild ?? read(deviceId);
    }

    // Reads description.
    private static string? ReadDescription(string deviceId)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(EnumRoot + deviceId);
        if (key is null)
        {
            return null;
        }

        if (key.GetValue("FriendlyName") is string friendly && friendly.Length > 0)
        {
            return friendly;
        }

        if (key.GetValue("DeviceDesc") is not string desc || desc.Length == 0)
        {
            return null;
        }

        int semicolon = desc.LastIndexOf(';');
        return semicolon >= 0 && semicolon < desc.Length - 1 ? desc[(semicolon + 1)..] : desc;
    }
}
