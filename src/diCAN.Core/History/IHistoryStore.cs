namespace DiCAN.Core.History;

// Defines history kind values.
public enum HistoryKind
{

    SendFrame,

    Filter,
}

// Stores history entry data.
public sealed record HistoryEntry(
    HistoryKind Kind,
    string Text,
    int? FrameId,
    bool? IsExtended,
    DateTimeOffset LastUsedAt,
    int UseCount);

// Manages i history.
public interface IHistoryStore
{

    // Lists the requested items.
    Task<IReadOnlyList<HistoryEntry>> ListAsync(
        HistoryKind kind, int limit, CancellationToken cancellationToken = default);

    // Saves the current choice.
    Task RememberAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    // Removes a saved history entry.
    Task ForgetAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    // Counts the requested items.
    Task<int> CountAsync(HistoryKind kind, CancellationToken cancellationToken = default);

    // Clears the stored state.
    Task ClearAsync(HistoryKind kind, CancellationToken cancellationToken = default);

    public const int VisibleLimit = 12;

    public const int StoredLimit = 100;
}
