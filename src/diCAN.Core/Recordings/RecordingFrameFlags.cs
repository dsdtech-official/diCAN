using DiCAN.Core.Protocol;

namespace DiCAN.Core.Recordings;

// Manages recording frame.
[Flags]
public enum RecordingFrameFlags
{
    None = 0,

    Extended = 0x01,

    Fd = 0x02,

    BitRateSwitched = 0x04,

    ErrorStateIndicated = 0x08,

    Remote = 0x10,
}

// Manages recording frame flags.
public static class RecordingFrameFlagsExtensions
{

    // Converts the requested value.
    public static RecordingFrameFlags ToRecordingFlags(this CanFrame frame)
    {
        RecordingFrameFlags flags = RecordingFrameFlags.None;

        if (frame.IsExtended)
        {
            flags |= RecordingFrameFlags.Extended;
        }

        if (frame.IsFd)
        {
            flags |= RecordingFrameFlags.Fd;
        }

        if (frame.IsBitRateSwitched)
        {
            flags |= RecordingFrameFlags.BitRateSwitched;
        }

        if (frame.IsErrorStateIndicated)
        {
            flags |= RecordingFrameFlags.ErrorStateIndicated;
        }

        if (frame.IsRemote)
        {
            flags |= RecordingFrameFlags.Remote;
        }

        return flags;
    }

    // Converts the requested value.
    public static CanFrame ToFrame(
        this RecordingFrameFlags flags,
        int id,
        int length,
        ReadOnlyMemory<byte> data,
        DateTimeOffset timestamp)
    {
        bool remote = flags.HasFlag(RecordingFrameFlags.Remote);

        return new CanFrame
        {
            Id = id,
            IsExtended = flags.HasFlag(RecordingFrameFlags.Extended),
            IsFd = flags.HasFlag(RecordingFrameFlags.Fd),
            IsBitRateSwitched = flags.HasFlag(RecordingFrameFlags.BitRateSwitched),
            IsErrorStateIndicated = flags.HasFlag(RecordingFrameFlags.ErrorStateIndicated),
            IsRemote = remote,
            RemoteLength = remote ? length : 0,
            Data = remote ? ReadOnlyMemory<byte>.Empty : data,
            Timestamp = timestamp,
        };
    }
}
