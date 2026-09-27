using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;

namespace DiCAN.Core.Recordings;

// Stores recording request data.
public sealed record RecordingRequest(
    string Adapter,
    RecordingScope Scope,
    bool StoreRawText,
    string DeviceKey,
    SlcanFirmwareGeneration Generation,
    string Port,
    CanBusConfiguration Configuration,
    string? FirmwareBuild,
    string AppVersion,
    DateTimeOffset StartedAt,
    string? InitialFilter = null);

// Stores recording data.
public sealed record Recording(
    long Id,
    string Volume,
    string Adapter,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string DeviceKey,
    SlcanFirmwareGeneration Generation,
    string Port,
    CanBusConfiguration Configuration,
    RecordingScope Scope,
    bool StoredRaw,
    string? FirmwareBuild,
    string AppVersion)
{

    // Gets the elapsed duration.
    public TimeSpan Duration(DateTimeOffset now) => (EndedAt ?? now) - StartedAt;
}

// Stores recording result data.
public sealed record RecordingResult(
    Recording Recording,
    long FrameCount,
    long FilterChangeCount,
    long Bytes,
    string? Fault);
