using System.Buffers.Binary;

namespace DiCAN.Core.Protocol;

// Manages gs usb.
public readonly record struct GsUsbBitTimingLimits(
    uint Features,
    uint CanClockHz,
    uint Tseg1Min,
    uint Tseg1Max,
    uint Tseg2Min,
    uint Tseg2Max,
    uint SjwMax,
    uint BrpMin,
    uint BrpMax,
    uint BrpIncrement)
{

    public const int Size = 40;

    public const int ExtendedSize = 72;

    // Tries parse.
    public static bool TryParse(
        ReadOnlySpan<byte> source, bool dataPhase, out GsUsbBitTimingLimits limits)
    {
        limits = default;

        int required = dataPhase ? ExtendedSize : Size;

        if (source.Length < required)
        {
            return false;
        }

        int btc = dataPhase ? 40 : 8;

        limits = new GsUsbBitTimingLimits(
            Features: Read(source, 0),
            CanClockHz: Read(source, 4),
            Tseg1Min: Read(source, btc),
            Tseg1Max: Read(source, btc + 4),
            Tseg2Min: Read(source, btc + 8),
            Tseg2Max: Read(source, btc + 12),
            SjwMax: Read(source, btc + 16),
            BrpMin: Read(source, btc + 20),
            BrpMax: Read(source, btc + 24),
            BrpIncrement: Read(source, btc + 28));

        return true;

        // Reads input data.
        static uint Read(ReadOnlySpan<byte> s, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(s[offset..]);
    }
}

// Stores gs usb bit timing data.
public readonly record struct GsUsbBitTiming(
    uint PropSeg, uint PhaseSeg1, uint PhaseSeg2, uint Sjw, uint Brp)
{

    public const int Size = 20;

    public uint TimeQuanta => 1 + PropSeg + PhaseSeg1 + PhaseSeg2;

    public double SamplePoint => (double)(TimeQuanta - PhaseSeg2) / TimeQuanta;

    // Calculates the bit rate.
    public uint BitrateOn(uint canClockHz) => canClockHz / (Brp * TimeQuanta);

    // Writes to.
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, PropSeg);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], PhaseSeg1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], PhaseSeg2);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], Sjw);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], Brp);
    }
}

// Calculates CAN bit timing.
public static class GsUsbBitTimingSolver
{

    public const double DefaultSamplePoint = 0.75;

    public const double DataPhaseSamplePoint = 0.75;

    // Tries solve.
    public static bool TrySolve(
        GsUsbBitTimingLimits limits,
        int bitsPerSecond,
        out GsUsbBitTiming timing,
        double samplePoint = DefaultSamplePoint)
    {
        timing = default;

        if (bitsPerSecond <= 0 || limits.CanClockHz == 0)
        {
            return false;
        }

        uint increment = Math.Max(1, limits.BrpIncrement);
        double bestError = double.MaxValue;
        bool found = false;

        for (uint brp = Math.Max(1, limits.BrpMin); brp <= limits.BrpMax; brp += increment)
        {
            uint divisor = brp * (uint)bitsPerSecond;

            if (divisor == 0 || limits.CanClockHz % divisor != 0)
            {
                continue;
            }

            uint quanta = limits.CanClockHz / divisor;

            if (quanta < 1 + limits.Tseg1Min + limits.Tseg2Min ||
                quanta > 1 + limits.Tseg1Max + limits.Tseg2Max)
            {
                continue;
            }

            uint tseg1 = (uint)Math.Round((quanta * samplePoint) - 1, MidpointRounding.AwayFromZero);
            tseg1 = Math.Clamp(tseg1, limits.Tseg1Min, limits.Tseg1Max);

            uint tseg2 = quanta - 1 - tseg1;

            if (tseg2 < limits.Tseg2Min || tseg2 > limits.Tseg2Max)
            {
                tseg2 = Math.Clamp(tseg2, limits.Tseg2Min, limits.Tseg2Max);
                tseg1 = quanta - 1 - tseg2;

                if (tseg1 < limits.Tseg1Min || tseg1 > limits.Tseg1Max)
                {
                    continue;
                }
            }

            double actual = (double)(quanta - tseg2) / quanta;
            double error = Math.Abs(actual - samplePoint);

            if (!found || error < bestError - 1e-9)
            {
                found = true;
                bestError = error;

                uint prop = tseg1 / 2;

                timing = new GsUsbBitTiming(
                    PropSeg: prop,
                    PhaseSeg1: tseg1 - prop,
                    PhaseSeg2: tseg2,
                    Sjw: Math.Min(limits.SjwMax, tseg2),
                    Brp: brp);
            }
        }

        return found;
    }
}
