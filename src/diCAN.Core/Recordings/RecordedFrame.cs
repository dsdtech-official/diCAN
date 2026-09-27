using DiCAN.Core.Protocol;
using DiCAN.Core.Streaming;

namespace DiCAN.Core.Recordings;

// Stores recorded frame data.
public sealed record RecordedFrame(
    long Sequence,
    long TimeMicroseconds,
    CanFrameDirection Direction,
    CanFrame Frame,
    string? RawText);

// Manages recording frame.
public readonly record struct RecordingFrameTally(long Total, long Fd = 0)
{

    public bool IsEmpty => Total == 0;
}

// Stores recording export data.
public sealed record RecordingExport(
    Recording Recording,
    IReadOnlyList<RecordingFilterChange> FilterChanges);

// Exports recorded CAN frames.
public readonly record struct RecordingExportResult(
    long Written, long Omitted, string? OmittedReason, bool Refused = false);
