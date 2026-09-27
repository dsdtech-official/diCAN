namespace DiCAN.Core.Sending;

// Manages transmit queue.
public sealed record TransmitQueueRow(
    int Position,
    string Name,
    int KindIndex,
    string Id,
    string Data,
    string Period,
    string Repeat,
    string RemoteLength);

// Stores the transmit queue.
public interface ITransmitQueueStore
{

    // Loads saved data.
    Task<IReadOnlyList<TransmitQueueRow>> LoadAsync(CancellationToken cancellationToken = default);

    // Saves application data.
    Task SaveAsync(
        IReadOnlyList<TransmitQueueRow> rows, CancellationToken cancellationToken = default);
}
