using DiCAN.Core.Protocol;

namespace DiCAN.Core.Transport;

// Manages i can.
public interface ICanTransport : IAsyncDisposable
{

    string Description { get; }

    bool IsOpen { get; }

    event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;

    event EventHandler<CanTransportErrorEventArgs>? Error;

    // Opens the requested resource.
    Task OpenAsync(CanBusConfiguration configuration, CancellationToken cancellationToken = default);

    // Closes the active resource.
    Task CloseAsync(CancellationToken cancellationToken = default);

    // Sends the requested data.
    Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default);
}

// Manages adapter configuration.
public readonly record struct CanBusConfiguration(
    int NominalBitrate,
    int? DataBitrate = null,
    SlcanOpenMode Mode = SlcanOpenMode.Normal,
    double? NominalSamplePoint = null,
    double? DataSamplePoint = null,
    bool AutoRetransmit = true)
{

    public const double MinSamplePoint = 0.50;

    public const double MaxSamplePoint = 0.90;

    // Validates input data.
    public void Validate()
    {
        Check(NominalSamplePoint, nameof(NominalSamplePoint));
        Check(DataSamplePoint, nameof(DataSamplePoint));

        // Checks the requested state.
        static void Check(double? value, string name)
        {
            if (value is { } point && (point < MinSamplePoint || point > MaxSamplePoint))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    point,
                    $"A sample point must be between {MinSamplePoint:P0} and {MaxSamplePoint:P0}.");
            }
        }
    }
}

// Stores event data.
public sealed class CanFrameReceivedEventArgs(CanFrame frame, string? rawText = null) : EventArgs
{

    public CanFrame Frame { get; } = frame;

    public string? RawText { get; } = rawText;
}

// Stores event data.
public sealed class CanTransportErrorEventArgs(
    string message, bool isFatal, string? collapseKey = null) : EventArgs
{

    public string Message { get; } = message;

    public string? CollapseKey { get; } = collapseKey;

    public bool IsFatal { get; } = isFatal;
}
