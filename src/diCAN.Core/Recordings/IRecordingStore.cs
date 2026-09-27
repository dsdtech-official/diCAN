using DiCAN.Core.Protocol;
using DiCAN.Core.Streaming;

namespace DiCAN.Core.Recordings;

// Stores event data.
public sealed class RecordingFaultEventArgs(string message) : EventArgs
{

    public string Message { get; } = message;
}

// Manages recording filter.
public readonly record struct RecordingFilterChange(long AtMicroseconds, string Expression);

// Records incoming CAN frames.
public interface IRecordingSession : IAsyncDisposable
{

    Recording Recording { get; }

    event EventHandler<RecordingFaultEventArgs>? Fault;

    // Adds the requested entry.
    void Add(CanFrame frame, CanFrameDirection direction, string? rawText);

    // Filters changed.
    void FilterChanged(string expression);

    // Stops the active operation.
    Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default);
}

// Stores recordings.
public interface IRecordingStore
{

    // Starts the operation.
    Task<IRecordingSession> StartAsync(
        RecordingRequest request, CancellationToken cancellationToken = default);

    // Lists the requested items.
    Task<IReadOnlyList<Recording>> ListAsync(CancellationToken cancellationToken = default);

    // Lists the requested items.
    Task<RecordingLibrary> ListLibraryAsync(CancellationToken cancellationToken = default);

    // Gets library bytes.
    Task<long> GetLibraryBytesAsync(CancellationToken cancellationToken = default);

    // Reads frames.
    IAsyncEnumerable<RecordedFrame> ReadFramesAsync(
        Recording recording, CancellationToken cancellationToken = default);

    // Counts the requested items.
    Task<RecordingFrameTally> CountFramesAsync(
        Recording recording, CancellationToken cancellationToken = default);

    // Reads filter changes.
    Task<IReadOnlyList<RecordingFilterChange>> ReadFilterChangesAsync(
        Recording recording, CancellationToken cancellationToken = default);

    // Deletes the requested entry.
    Task DeleteAsync(Recording recording, CancellationToken cancellationToken = default);

    // Compacts stored data.
    Task<RecordingCompactResult> CompactAsync(CancellationToken cancellationToken = default);
}
