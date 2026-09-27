using DiCAN.Core.Protocol;

namespace DiCAN.Core.Streaming;

// Manages can bus load.
public static class CanBusLoad
{

    private const int ClassicStandardOverheadBits = 47;

    private const int ClassicExtendedOverheadBits = 67;

    private const int FdArbitrationBits = 32;

    private const int FdExtendedArbitrationBits = 52;

    private const int FdDataPhaseOverheadBits = 31;

    // Estimates frame wire time.
    public static double SecondsOnWire(
        CanFrame frame, int nominalBitsPerSecond, int? dataBitsPerSecond)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nominalBitsPerSecond);

        double nominal = nominalBitsPerSecond;

        if (!frame.IsFd)
        {

            int payloadBits = frame.IsRemote ? 0 : frame.Data.Length * 8;

            int overhead = frame.IsExtended
                ? ClassicExtendedOverheadBits
                : ClassicStandardOverheadBits;

            return (overhead + payloadBits) / nominal;
        }

        int arbitration = frame.IsExtended ? FdExtendedArbitrationBits : FdArbitrationBits;
        int dataPhase = FdDataPhaseOverheadBits + (frame.Data.Length * 8);

        if (!frame.IsBitRateSwitched || dataBitsPerSecond is not { } data || data <= 0)
        {
            return (arbitration + dataPhase) / nominal;
        }

        return (arbitration / nominal) + (dataPhase / (double)data);
    }
}
