using DiCAN.Core.Transport;

namespace DiCAN.Core.Diagnostics;

// Manages pending transport.
public readonly record struct PendingTransportReport(DateTimeOffset At, CanTransportErrorEventArgs Report);

// Manages transport report.
public readonly record struct TransportReportBatch(
    IReadOnlyList<PendingTransportReport> Reports,
    long Dropped);

// Buffers adapter reports.
public sealed class TransportReportBuffer
{

    public const int Capacity = 2048;

    private readonly object _gate = new();
    private readonly List<PendingTransportReport> _pending = new(64);
    private long _dropped;

    // Adds the requested entry.
    public void Add(DateTimeOffset at, CanTransportErrorEventArgs report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {

            if (_pending.Count >= Capacity)
            {
                _dropped++;
                return;
            }

            _pending.Add(new PendingTransportReport(at, report));
        }
    }

    // Drains queued input data.
    public TransportReportBatch Drain()
    {
        lock (_gate)
        {
            if (_pending.Count == 0 && _dropped == 0)
            {
                return new TransportReportBatch([], 0);
            }

            var reports = _pending.ToArray();
            long dropped = _dropped;

            _pending.Clear();
            _dropped = 0;

            return new TransportReportBatch(reports, dropped);
        }
    }

    // Clears the stored state.
    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _dropped = 0;
        }
    }

    public int PendingCount
    {
        get { lock (_gate) { return _pending.Count; } }
    }
}
