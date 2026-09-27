using System.Collections.Concurrent;
using DiCAN.Core.Protocol;
using DiCAN.Core.Streaming;

namespace DiCAN.Core.Aggregation;

// Stores can id row data.
public readonly record struct CanIdRow(
    int Id,
    bool IsExtended,
    long Count,
    ReadOnlyMemory<byte> Data,
    DateTimeOffset LastSeen,
    TimeSpan? Period,
    TimeSpan LastDelta,
    TimeSpan MinDelta,
    TimeSpan MaxDelta,
    bool SawReceived = true,
    bool SawTransmitted = false,
    bool IsFd = false,
    bool IsBitRateSwitched = false,
    bool IsRemote = false,
    int RemoteLength = 0);

// Manages can id aggregator.
public sealed class CanIdAggregator
{

    // Manages slot.
    private sealed class Slot
    {
        public int Id;
        public bool IsExtended;
        public long Count;
        public byte[] Data = [];
        public int DataLength;
        public long LastSeenTicks;
        public long FirstSeenTicks;
        public long LastDeltaTicks;
        public long MinDeltaTicks = long.MaxValue;
        public long MaxDeltaTicks;

        public bool SawReceived;
        public bool SawTransmitted;

        public bool IsFd;
        public bool IsBitRateSwitched;
        public bool IsRemote;
        public int RemoteLength;
    }

    private readonly ConcurrentDictionary<long, Slot> _slots = new();

    public int RowCount => _slots.Count;

    public long TotalFrames { get; private set; }

    // Adds the requested entry.
    public void Add(CanFrame frame, CanFrameDirection direction = CanFrameDirection.Received)
    {
        long key = ((long)frame.Id << 1) | (frame.IsExtended ? 1L : 0L);
        long ticks = frame.Timestamp.UtcTicks;

        Slot slot = _slots.GetOrAdd(
            key,
            static (_, state) => new Slot
            {
                Id = state.Id,
                IsExtended = state.IsExtended,
                FirstSeenTicks = state.Ticks,
            },
            (frame.Id, frame.IsExtended, Ticks: ticks));

        if (slot.Count > 0)
        {

            long delta = Math.Max(0, ticks - slot.LastSeenTicks);
            slot.LastDeltaTicks = delta;

            if (delta < slot.MinDeltaTicks)
            {
                slot.MinDeltaTicks = delta;
            }

            if (delta > slot.MaxDeltaTicks)
            {
                slot.MaxDeltaTicks = delta;
            }
        }

        slot.Count++;
        slot.LastSeenTicks = ticks;

        if (direction == CanFrameDirection.Transmitted)
        {
            slot.SawTransmitted = true;
        }
        else
        {
            slot.SawReceived = true;
        }

        ReadOnlySpan<byte> payload = frame.Data.Span;
        if (slot.Data.Length < payload.Length)
        {
            slot.Data = new byte[payload.Length];
        }

        payload.CopyTo(slot.Data);
        slot.DataLength = payload.Length;

        slot.IsFd = frame.IsFd;
        slot.IsBitRateSwitched = frame.IsBitRateSwitched;
        slot.IsRemote = frame.IsRemote;
        slot.RemoteLength = frame.RemoteLength;

        TotalFrames++;
    }

    // Gets snapshot.
    public IReadOnlyList<CanIdRow> Snapshot()
    {
        var rows = new List<CanIdRow>(_slots.Count);

        foreach (Slot slot in _slots.Values)
        {
            long count = slot.Count;
            if (count == 0)
            {
                continue;
            }

            TimeSpan? period = count > 1
                ? TimeSpan.FromTicks((slot.LastSeenTicks - slot.FirstSeenTicks) / (count - 1))
                : null;

            byte[] data = new byte[slot.DataLength];
            Array.Copy(slot.Data, data, slot.DataLength);

            rows.Add(new CanIdRow(
                slot.Id,
                slot.IsExtended,
                count,
                data,
                new DateTimeOffset(slot.LastSeenTicks, TimeSpan.Zero),
                period,
                TimeSpan.FromTicks(slot.LastDeltaTicks),
                TimeSpan.FromTicks(slot.MinDeltaTicks == long.MaxValue ? 0 : slot.MinDeltaTicks),
                TimeSpan.FromTicks(slot.MaxDeltaTicks),
                slot.SawReceived,
                slot.SawTransmitted,
                slot.IsFd,
                slot.IsBitRateSwitched,
                slot.IsRemote,
                slot.RemoteLength));
        }

        return rows;
    }

    // Clears the stored state.
    public void Clear()
    {
        _slots.Clear();
        TotalFrames = 0;
    }
}
