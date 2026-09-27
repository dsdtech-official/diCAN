using DiCAN.Core.Abstractions;
using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;

namespace DiCAN.App.Composition;

// Logs transferred CAN frames.
public sealed class FrameLoggingTransport(
    ICanTransport inner, Action<CanFrame> onTransmitted, IMonotonicClock clock)
    : ICanTransport
{
    public string Description => inner.Description;

    public bool IsOpen => inner.IsOpen;

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived
    {
        add => inner.FrameReceived += value;
        remove => inner.FrameReceived -= value;
    }

    public event EventHandler<CanTransportErrorEventArgs>? Error
    {
        add => inner.Error += value;
        remove => inner.Error -= value;
    }

    // Opens the requested resource.
    public Task OpenAsync(
        CanBusConfiguration configuration, CancellationToken cancellationToken = default) =>
        inner.OpenAsync(configuration, cancellationToken);

    // Closes the active resource.
    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        inner.CloseAsync(cancellationToken);

    // Sends the requested data.
    public async Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {

        CanFrame stamped = frame.Timestamp == default
            ? frame with { Timestamp = clock.Now }
            : frame;

        await inner.SendAsync(stamped, cancellationToken);

        onTransmitted(stamped);
    }

    // Releases held resources.
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
