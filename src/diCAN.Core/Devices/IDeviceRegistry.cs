using DiCAN.Core.Protocol;

namespace DiCAN.Core.Devices;

// Manages can connection.
public readonly record struct CanConnectionFact(
    CanDeviceInfo Device,
    SlcanFirmwareGeneration Generation,
    string? VersionResponse,
    string PortName,
    int NominalBitrate,
    int? DataBitrate,
    SlcanOpenMode Mode);

// Stores saved device details.
public interface IDeviceRegistry
{

    public const int MaxNoteLength = 20;

    // Records connection.
    Task<CanDeviceRecord> RecordConnectionAsync(
        CanConnectionFact fact, CancellationToken cancellationToken = default);

    // Finds the requested item.
    Task<CanDeviceRecord?> FindAsync(
        string deviceKey,
        SlcanFirmwareGeneration generation,
        CancellationToken cancellationToken = default);

    // Lists the requested items.
    Task<IReadOnlyList<CanDeviceRecord>> ListAsync(CancellationToken cancellationToken = default);

    // Sets note.
    Task SetNoteAsync(
        string deviceKey,
        SlcanFirmwareGeneration generation,
        string? note,
        CancellationToken cancellationToken = default);
}
