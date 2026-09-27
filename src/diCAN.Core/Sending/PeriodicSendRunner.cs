using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;

namespace DiCAN.Core.Sending;

// Schedules periodic CAN frames.
public readonly record struct PeriodicSendTickResult(
    int Sent,
    IReadOnlyList<PeriodicSendFailure> Failed,
    IReadOnlyList<int> Completed,
    int SkippedOccurrences)
{

    public bool IsEmpty => Sent == 0 && Failed.Count == 0 && Completed.Count == 0;
}

// Schedules periodic CAN frames.
public sealed record PeriodicSendFailure(int Handle, CanFrame Frame, Exception Error);

// Manages periodic send runner.
public sealed class PeriodicSendRunner
{
    private readonly ICanTransport _transport;
    private readonly bool _canTransmit;
    private bool _tickInFlight;

    // Initializes this instance.
    public PeriodicSendRunner(ICanTransport transport, bool canTransmit)
    {
        _transport = transport;
        _canTransmit = canTransmit;
    }

    public PeriodicSendSchedule Schedule { get; } = new();

    public bool CanTransmit => _canTransmit;

    public long TotalSent { get; private set; }

    public long TotalSkipped { get; private set; }

    // Sends once.
    public async Task<Exception?> SendOnceAsync(
        CanFrame frame, CancellationToken cancellationToken = default)
    {
        if (!_canTransmit || !_transport.IsOpen)
        {
            return null;
        }

        try
        {
            await _transport.SendAsync(frame, cancellationToken);
            TotalSent++;
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex;
        }
    }

    // Advances scheduled work.
    public async Task<PeriodicSendTickResult> TickAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_canTransmit || !_transport.IsOpen || _tickInFlight)
        {
            return Idle;
        }

        _tickInFlight = true;

        try
        {
            PeriodicSendBatch batch = Schedule.TakeDue(now);

            if (batch.IsEmpty)
            {
                return Idle;
            }

            TotalSkipped += batch.SkippedOccurrences;

            int sent = 0;
            List<PeriodicSendFailure>? failed = null;

            foreach (PeriodicSendDispatch dispatch in batch.Due)
            {
                try
                {
                    await _transport.SendAsync(dispatch.Frame, cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {

                    (failed ??= []).Add(
                        new PeriodicSendFailure(dispatch.Handle, dispatch.Frame, ex));
                }
            }

            TotalSent += sent;

            return new PeriodicSendTickResult(
                sent,
                (IReadOnlyList<PeriodicSendFailure>?)failed ?? [],
                batch.Completed,
                batch.SkippedOccurrences);
        }
        finally
        {
            _tickInFlight = false;
        }
    }

    // Stops the active operation.
    public void Stop() => Schedule.Clear();

    // Resets counters.
    public void ResetCounters()
    {
        TotalSent = 0;
        TotalSkipped = 0;
        Schedule.ResetCounters();
    }

    private static PeriodicSendTickResult Idle => new(0, [], [], 0);
}
