namespace DiCAN.Core.Protocol;

// Manages slcan bit timing.
public static class SlcanBitTiming
{

    public const int LimitsFieldLength = 8;

    // Tries limits.
    public static bool TryLimits(
        SlcanDeviceInfo? info, bool dataPhase, out GsUsbBitTimingLimits limits)
    {
        limits = default;

        if (info?.ClockMhz is not { } mhz || mhz <= 0)
        {
            return false;
        }

        if (info.BitTimingLimits is not { } values || values.Count < LimitsFieldLength)
        {
            return false;
        }

        int offset = dataPhase ? 4 : 0;

        uint brpMax = (uint)values[offset];
        uint tseg1Max = (uint)values[offset + 1];
        uint tseg2Max = (uint)values[offset + 2];
        uint sjwMax = (uint)values[offset + 3];

        if (brpMax == 0 || tseg1Max == 0 || tseg2Max == 0 || sjwMax == 0)
        {
            return false;
        }

        limits = new GsUsbBitTimingLimits(

            Features: 0,
            CanClockHz: (uint)mhz * 1_000_000u,

            Tseg1Min: 1,
            Tseg1Max: tseg1Max,
            Tseg2Min: 1,
            Tseg2Max: tseg2Max,
            SjwMax: sjwMax,
            BrpMin: 1,
            BrpMax: brpMax,

            BrpIncrement: 1);

        return true;
    }

    // Sets nominal bit timing.
    public static string SetNominalBitTiming(GsUsbBitTiming timing) => Format('s', timing);

    // Sets data bit timing.
    public static string SetDataBitTiming(GsUsbBitTiming timing) => Format('y', timing);

    // Formats the requested value.
    private static string Format(char command, GsUsbBitTiming timing) =>
        $"{command}{timing.Brp},{timing.PropSeg + timing.PhaseSeg1},{timing.PhaseSeg2},{timing.Sjw}";
}
