using DiCAN.Core.Protocol;

namespace DiCAN.Core.Streaming;

// Manages can frame.
public enum CanFrameDirection
{

    Received,

    Transmitted,
}

// Manages can frame.
public readonly record struct CanFrameLogEntry(
    long Sequence,
    CanFrameDirection Direction,
    CanFrame Frame);

// Manages can frame log.
public sealed class CanFrameLog
{

    public const int DefaultCapacity = 10_000;

    private readonly Lock _gate = new();
    private readonly CanFrameLogEntry[] _entries;

    private int _cursor;

    private int _held;

    private long _next;
    private long _dropped;

    // Initializes this instance.
    public CanFrameLog(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _entries = new CanFrameLogEntry[capacity];
    }

    public int Capacity => _entries.Length;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _held;
            }
        }
    }

    public long TotalFrames
    {
        get
        {
            lock (_gate)
            {
                return _next;
            }
        }
    }

    public long DroppedFrames
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    // Adds the requested entry.
    public long Add(CanFrame frame, CanFrameDirection direction)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            long sequence = _next++;

            if (_held == _entries.Length)
            {
                _dropped++;
            }
            else
            {
                _held++;
            }

            _entries[_cursor] = new CanFrameLogEntry(sequence, direction, frame);
            _cursor = (_cursor + 1) % _entries.Length;

            return sequence;
        }
    }

    // Clears the stored state.
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_entries);
            _cursor = 0;
            _held = 0;
            _next = 0;
            _dropped = 0;
        }
    }

    // Gets snapshot.
    public CanFrameLogEntry[] Snapshot(
        long afterSequence = -1,
        int limit = int.MaxValue,
        long throughSequence = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        lock (_gate)
        {
            if (_held == 0 || limit == 0)
            {
                return [];
            }

            int oldest = (_cursor - _held + _entries.Length) % _entries.Length;

            int matching = 0;

            for (int i = 0; i < _held; i++)
            {
                if (Qualifies(_entries[(oldest + i) % _entries.Length]))
                {
                    matching++;
                }
            }

            int take = Math.Min(matching, limit);

            if (take == 0)
            {
                return [];
            }

            var result = new CanFrameLogEntry[take];

            int written = 0;

            for (int i = _held - 1; i >= 0 && written < take; i--)
            {
                CanFrameLogEntry entry = _entries[(oldest + i) % _entries.Length];

                if (Qualifies(entry))
                {
                    result[take - 1 - written] = entry;
                    written++;
                }
            }

            return result;
        }

        // Checks frame eligibility.
        bool Qualifies(CanFrameLogEntry entry) =>
            entry.Sequence > afterSequence && entry.Sequence <= throughSequence;
    }
}
