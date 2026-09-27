using DiCAN.Core.Protocol;

namespace DiCAN.Core.Devices;

// Stores can device record data.
public sealed record CanDeviceRecord(
    string DeviceKey,
    SlcanFirmwareGeneration Generation,
    string? SerialNumber,
    ushort VendorId,
    ushort ProductId,
    string? VersionResponse,
    string? FirmwareBuild,
    bool? HasQuartz,
    DateTimeOffset FirstConnectedAt,
    DateTimeOffset LastConnectedAt,
    int ConnectCount,
    string LastPortName,
    int LastNominalBitrate,
    int? LastDataBitrate,
    SlcanOpenMode LastMode,
    string? Note,
    string AppVersion)
{

    public string UsbId => $"{VendorId:X4}:{ProductId:X4}";
}
