namespace DiCAN.Core.Recordings;

// Manages recording compact.
public sealed record RecordingCompactResult(
    long BytesBefore, long BytesAfter, int VolumesCompacted, int VolumesSkipped)
{

    public long BytesReclaimed => Math.Max(0, BytesBefore - BytesAfter);

    public bool NothingToCompact => VolumesCompacted == 0 && VolumesSkipped == 0;
}
