using DiCAN.Core.Protocol;

namespace DiCAN.Infrastructure.Virtual;

// Manages virtual bus.
public sealed class VirtualBus
{
    private readonly int[] _ids;
    private readonly int[] _periodsMicroseconds;
    private readonly byte[][] _payloads;
    private readonly int[] _schedule;
    private readonly Random _random;
    private long _sequence;

    // Initializes this instance.
    public VirtualBus(int uniqueIds = 250, double fdRatio = 0.3, int seed = 20260814)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(uniqueIds, 1);

        _random = new Random(seed);
        _ids = new int[uniqueIds];
        _periodsMicroseconds = new int[uniqueIds];
        _payloads = new byte[uniqueIds][];

        int[] periods = [10_000, 20_000, 50_000, 100_000, 200_000, 1_000_000];

        for (int i = 0; i < uniqueIds; i++)
        {
            _ids[i] = 0x100 + i;
            _periodsMicroseconds[i] = periods[i % periods.Length];

            bool fd = _random.NextDouble() < fdRatio;
            int length = fd
                ? new[] { 12, 16, 24, 32, 48, 64 }[_random.Next(6)]
                : _random.Next(1, 9);

            byte[] payload = new byte[length];
            _random.NextBytes(payload);
            _payloads[i] = payload;
        }

        _schedule = BuildSchedule(_periodsMicroseconds, periods.Length);
    }

    // Builds schedule.
    private static int[] BuildSchedule(int[] periodsMicroseconds, int classCount)
    {
        int count = periodsMicroseconds.Length;

        long cycle = 0;
        foreach (int period in periodsMicroseconds)
        {
            cycle = Math.Max(cycle, period);
        }

        var keys = new List<long>();

        for (int i = 0; i < count; i++)
        {
            int period = periodsMicroseconds[i];
            int peers = (count - (i % classCount) + classCount - 1) / classCount;
            long phase = (long)period * (i / classCount) / peers;

            for (long time = phase; time < cycle; time += period)
            {
                keys.Add((time * count) + i);
            }
        }

        long[] sorted = keys.ToArray();
        Array.Sort(sorted);

        var schedule = new int[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            schedule[i] = (int)(sorted[i] % count);
        }

        return schedule;
    }

    public int UniqueIds => _ids.Length;

    // Generates the output data.
    public CanFrame[] Generate(int count, DateTimeOffset start, double framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);

        var frames = new CanFrame[count];
        double intervalTicks = TimeSpan.TicksPerSecond / framesPerSecond;

        for (int i = 0; i < count; i++)
        {

            int slot = _schedule[(int)(_sequence % _schedule.Length)];
            _sequence++;

            byte[] payload = (byte[])_payloads[slot].Clone();

            if (payload.Length > 0)
            {
                payload[0] = (byte)(_sequence & 0xFF);
            }

            bool fd = payload.Length > 8;

            frames[i] = new CanFrame
            {
                Id = _ids[slot],
                IsFd = fd,
                IsBitRateSwitched = fd,
                Data = payload,
                Timestamp = start.AddTicks((long)(i * intervalTicks)),
            };
        }

        return frames;
    }

    // Generates the output data.
    public string GenerateWireText(int count, DateTimeOffset start, double framesPerSecond)
    {
        var text = new System.Text.StringBuilder(count * 40);

        foreach (CanFrame frame in Generate(count, start, framesPerSecond))
        {
            text.Append(SlcanCodec.Encode(frame)).Append(SlcanCodec.Terminator);
        }

        return text.ToString();
    }
}
