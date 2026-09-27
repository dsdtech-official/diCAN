namespace DiCAN.Core.Devices;

// Stores can device info data.
public sealed record CanDeviceInfo(
    CanDeviceKind Kind,
    ushort VendorId,
    ushort ProductId,
    string? SerialNumber,
    string? PortName,
    string DeviceId,
    string Description,
    CanFirmwareLine FirmwareLine = CanFirmwareLine.Unknown,
    CanChipFamily ChipFamily = CanChipFamily.Unknown,
    string? FirmwareVersion = null)
{

    public string UsbId => $"{VendorId:X4}:{ProductId:X4}";

    public string? PortDisplayName =>
        PortName is { } port && port.StartsWith(CalloutPrefix, StringComparison.Ordinal)
            ? port[CalloutPrefix.Length..]
            : PortName;

    private const string CalloutPrefix = "/dev/cu.";

    public string RegistryKey =>
        SerialNumber is { Length: > 0 } serial ? serial : DeviceId;

    public bool IsUsable => Kind switch
    {
        CanDeviceKind.Slcan or CanDeviceKind.LegacyCanable1 => PortName is not null,
        CanDeviceKind.Candlelight => true,
        _ => false,
    };

    // Checks probably same device.
    public bool IsProbablySameDevice(CanDeviceInfo other)
    {
        const int MinimumPrefix = 8;

        if (SerialNumber is null || other.SerialNumber is null)
        {
            return false;
        }

        string a = SerialNumber;
        string b = other.SerialNumber;
        int shared = Math.Min(a.Length, b.Length);

        if (shared < MinimumPrefix)
        {
            return false;
        }

        return string.Equals(a[..shared], b[..shared], StringComparison.OrdinalIgnoreCase);
    }
}
