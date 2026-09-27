namespace DiCAN.Core.Diagnostics;

// Manages repeated line limiter.
public sealed class RepeatedLineLimiter
{

    public const int DefaultPerSecond = 5;

    private readonly Func<DateTimeOffset> _clock;
    private readonly int _perSecond;
    private readonly object _gate = new();

    private string? _key;
    private string? _message;
    private DateTimeOffset _windowStart;
    private int _writtenInWindow;
    private int _runTotal;
    private int _runSuppressed;

    // Initializes this instance.
    public RepeatedLineLimiter(Func<DateTimeOffset>? clock = null, int perSecond = DefaultPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perSecond, 1);

        _clock = clock ?? (() => DateTimeOffset.Now);
        _perSecond = perSecond;
    }

    // Gets the next item.
    public LimiterDecision Next(string key, string message)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(message);

        DateTimeOffset at = _clock();

        lock (_gate)
        {
            if (!string.Equals(key, _key, StringComparison.Ordinal))
            {
                string? summary = BuildSummary();

                _key = key;
                _message = message;
                _windowStart = at;
                _writtenInWindow = 1;
                _runTotal = 1;
                _runSuppressed = 0;

                return new LimiterDecision(summary, message);
            }

            if (at - _windowStart >= TimeSpan.FromSeconds(1))
            {
                _windowStart = at;
                _writtenInWindow = 0;
            }

            _runTotal++;

            _message = message;

            if (_writtenInWindow < _perSecond)
            {
                _writtenInWindow++;
                return new LimiterDecision(null, message);
            }

            _runSuppressed++;
            return new LimiterDecision(null, null);
        }
    }

    // Flushes buffered data.
    public string? Flush()
    {
        lock (_gate)
        {
            string? summary = BuildSummary();

            _key = null;
            _message = null;
            _runTotal = 0;
            _runSuppressed = 0;
            _writtenInWindow = 0;

            return summary;
        }
    }

    // Builds summary.
    private string? BuildSummary()
    {
        if (_runSuppressed == 0 || _message is null)
        {

            return null;
        }

        return $"{_message} × {_runTotal} ({_runSuppressed} not written, capped at {_perSecond}/s)";
    }
}

// Stores limiter decision data.
public readonly record struct LimiterDecision(string? Summary, string? Line);
