using System.Globalization;
using System.Runtime.Versioning;
using DiCAN.Core.Devices;
using DiCAN.Mac.Interop;

namespace DiCAN.Mac;

// Discovers USB CAN adapters.
[SupportedOSPlatform("macos")]
public sealed class MacCanDeviceEnumerator : ICanDeviceEnumerator
{

    private const string VendorId = "idVendor";
    private const string ProductId = "idProduct";
    private const string DeviceRevision = "bcdDevice";
    private const string SerialNumber = "USB Serial Number";
    private const string ProductString = "kUSBProductString";
    private const string ProductName = "USB Product Name";
    private const string LocationId = "locationID";

    private const string CalloutDevice = "IOCalloutDevice";

    // Lists the available items.
    public IReadOnlyList<CanDeviceInfo> Enumerate()
    {
        uint iterator = IOKit.MatchingUsbDevices();
        if (iterator == 0)
        {
            return [];
        }

        var results = new List<CanDeviceInfo>();

        try
        {
            for (uint node = IOKit.Next(iterator); node != 0; node = IOKit.Next(iterator))
            {
                try
                {
                    if (Describe(node) is { } device)
                    {
                        results.Add(device);
                    }
                }
                finally
                {

                    IOKit.Release(node);
                }
            }
        }
        finally
        {
            IOKit.Release(iterator);
        }

        return results;
    }

    // Counts the requested items.
    public static MacUsbBusReading CountPresentUsbDevices()
    {
        uint iterator = IOKit.MatchingUsbDevices();
        if (iterator == 0)
        {

            return new MacUsbBusReading(0, 0);
        }

        int nodes = 0;
        int identified = 0;

        try
        {
            for (uint node = IOKit.Next(iterator); node != 0; node = IOKit.Next(iterator))
            {
                try
                {
                    nodes++;

                    if (ReadId(node, VendorId) is not null && ReadId(node, ProductId) is not null)
                    {
                        identified++;
                    }
                }
                finally
                {

                    IOKit.Release(node);
                }
            }
        }
        finally
        {
            IOKit.Release(iterator);
        }

        return new MacUsbBusReading(nodes, identified);
    }

    // Describes the requested value.
    private static CanDeviceInfo? Describe(uint node)
    {
        ushort? vendor = ReadId(node, VendorId);
        ushort? product = ReadId(node, ProductId);

        if (vendor is not { } vid || product is not { } pid)
        {
            return null;
        }

        CanDeviceKind kind = CanUsbIds.Classify(vid, pid);
        if (kind == CanDeviceKind.Unknown)
        {
            return null;
        }

        string? serial = ReadString(node, SerialNumber);
        string? name = ReadString(node, ProductString) is { Length: > 0 } raw
            ? raw
            : ReadString(node, ProductName);

        if (CanExcludedProducts.IsExcluded(name, ReadString(node, ProductName)))
        {
            return null;
        }
        ushort? revision = ReadId(node, DeviceRevision);
        string? port = ReadString(node, CalloutDevice, search: true);
        long? location = ReadNumber(node, LocationId);

        CanFirmwareFacts firmware = CanFirmwareLines.Identify(
            vid, pid, deviceRevision: revision, reportedProductString: name);

        return new CanDeviceInfo(
            Kind: kind,
            VendorId: vid,
            ProductId: pid,
            SerialNumber: serial,
            PortName: port,
            DeviceId: BuildDeviceId(vid, pid, serial, location),
            Description: name ?? kind.ToString(),
            FirmwareLine: firmware.Line,
            ChipFamily: firmware.Chip,
            FirmwareVersion: firmware.Version);
    }

    // Builds device id.
    private static string BuildDeviceId(ushort vendor, ushort product, string? serial, long? location)
    {
        string tail = serial is { Length: > 0 }
            ? serial
            : location is { } id
                ? "LOC_" + id.ToString("X8", CultureInfo.InvariantCulture)

                : "UNKNOWN";

        return string.Create(
            CultureInfo.InvariantCulture, $@"IOUSB\VID_{vendor:X4}&PID_{product:X4}\{tail}");
    }

    // Reads id.
    private static ushort? ReadId(uint node, string key) =>
        ReadNumber(node, key) is { } value and >= 0 and <= ushort.MaxValue ? (ushort)value : null;

    // Reads number.
    private static long? ReadNumber(uint node, string key)
    {
        IntPtr value = IOKit.Property(node, key);
        try
        {
            return CoreFoundation.ToInt64(value);
        }
        finally
        {
            CoreFoundation.Release(value);
        }
    }

    // Reads string.
    private static string? ReadString(uint node, string key, bool search = false)
    {
        IntPtr value = search ? IOKit.SearchProperty(node, key) : IOKit.Property(node, key);
        try
        {
            return CoreFoundation.ToString(value);
        }
        finally
        {
            CoreFoundation.Release(value);
        }
    }
}

// Manages mac usb.
public readonly record struct MacUsbBusReading(int Nodes, int WithIdentity);
