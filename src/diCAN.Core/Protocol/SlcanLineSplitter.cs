namespace DiCAN.Core.Protocol;

// Stores slcan line data.
public readonly record struct SlcanLine(string Text, DateTimeOffset FirstByteAt, DateTimeOffset CompletedAt)
{

    public bool WasSplit => CompletedAt != FirstByteAt;
}

// Manages slcan line splitter.
public sealed class SlcanLineSplitter
{

    public const int LongestFrameLine = 1 + 8 + 1 + (SlcanDlc.MaxPayload * 2);

    public const int LongestLine = 200;

    private readonly int _maxLineLength;
    private readonly System.Text.StringBuilder _pending = new(LongestFrameLine);
    private DateTimeOffset _pendingSince;

    // Initializes this instance.
    public SlcanLineSplitter(int maxLineLength = LongestLine + 56)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineLength, LongestLine);
        _maxLineLength = maxLineLength;
    }

    public long OverlongLinesDropped { get; private set; }

    public int PendingLength => _pending.Length;

    // Appends the requested data.
    public IReadOnlyList<SlcanLine> Append(ReadOnlySpan<byte> chunk, DateTimeOffset timestamp)
    {
        List<SlcanLine>? lines = null;

        foreach (byte b in chunk)
        {
            if (b == SlcanCodec.Terminator)
            {
                (lines ??= []).Add(new SlcanLine(
                    _pending.ToString(),
                    _pending.Length == 0 ? timestamp : _pendingSince,
                    timestamp));

                _pending.Clear();
                continue;
            }

            if (b == SlcanCodec.Rejected)
            {
                (lines ??= []).Add(new SlcanLine(
                    SlcanCodec.Rejected.ToString(),
                    _pending.Length == 0 ? timestamp : _pendingSince,
                    timestamp));

                _pending.Clear();
                continue;
            }

            if (_pending.Length == 0)
            {
                _pendingSince = timestamp;
            }

            if (_pending.Length >= _maxLineLength)
            {
                OverlongLinesDropped++;
                _pending.Clear();
                continue;
            }

            _pending.Append((char)b);
        }

        return lines ?? (IReadOnlyList<SlcanLine>)[];
    }

    // Resets the current state.
    public void Reset()
    {
        _pending.Clear();
    }
}
