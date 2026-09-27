using DiCAN.Core.Abstractions;
using DiCAN.Core.History;
using DiCAN.Infrastructure.Devices;
using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.History;

// Manages sqlite history store.
public sealed class SqliteHistoryStore : IHistoryStore
{
    private readonly string _connectionString;
    private readonly IMonotonicClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    // Initializes this instance.
    public SqliteHistoryStore(string databasePath, IMonotonicClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _clock = clock;

        if (Path.GetDirectoryName(Path.GetFullPath(databasePath)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    // Lists the requested items.
    public async Task<IReadOnlyList<HistoryEntry>> ListAsync(
        HistoryKind kind, int limit, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT text, frame_id, is_extended, last_used_at, use_count
            FROM history WHERE kind = $kind
            ORDER BY last_used_at DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$kind", Wire(kind));
        command.Parameters.AddWithValue("$limit", limit);

        var entries = new List<HistoryEntry>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new HistoryEntry(
                Kind: kind,
                Text: reader.GetString(0),
                FrameId: reader.IsDBNull(1) ? null : reader.GetInt32(1),
                IsExtended: reader.IsDBNull(2) ? null : reader.GetInt32(2) != 0,
                LastUsedAt: DateTimeOffset.Parse(reader.GetString(3), null),
                UseCount: reader.GetInt32(4)));
        }

        return entries;
    }

    // Saves the current choice.
    public async Task RememberAsync(
        HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Text))
        {
            return;
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO history (kind, text, frame_id, is_extended, last_used_at, use_count)
            VALUES ($kind, $text, $id, $ext, $now, 1)
            ON CONFLICT(kind, text, COALESCE(frame_id, -1), COALESCE(is_extended, -1))
            DO UPDATE SET last_used_at = excluded.last_used_at, use_count = use_count + 1;
            """;
        command.Parameters.AddWithValue("$kind", Wire(entry.Kind));
        command.Parameters.AddWithValue("$text", entry.Text.Trim());
        command.Parameters.AddWithValue("$id", (object?)entry.FrameId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ext", entry.IsExtended is { } e ? (e ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(_clock.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await TrimAsync(connection, entry.Kind, cancellationToken);
    }

    // Removes a saved history entry.
    public async Task ForgetAsync(
        HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            DELETE FROM history
            WHERE kind = $kind AND text = $text
              AND COALESCE(frame_id, -1) = COALESCE($id, -1)
              AND COALESCE(is_extended, -1) = COALESCE($ext, -1);
            """;
        command.Parameters.AddWithValue("$kind", Wire(entry.Kind));
        command.Parameters.AddWithValue("$text", entry.Text);
        command.Parameters.AddWithValue("$id", (object?)entry.FrameId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ext", entry.IsExtended is { } e ? (e ? 1 : 0) : DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Counts the requested items.
    public async Task<int> CountAsync(
        HistoryKind kind, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM history WHERE kind = $kind;";
        command.Parameters.AddWithValue("$kind", Wire(kind));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    // Clears the stored state.
    public async Task ClearAsync(
        HistoryKind kind, CancellationToken cancellationToken = default)
    {

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "DELETE FROM history WHERE kind = $kind;";
        command.Parameters.AddWithValue("$kind", Wire(kind));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Trims stored data.
    private static async Task TrimAsync(
        SqliteConnection connection, HistoryKind kind, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            DELETE FROM history WHERE id IN (
                SELECT id FROM history WHERE kind = $kind
                ORDER BY last_used_at DESC LIMIT -1 OFFSET $keep);
            """;
        command.Parameters.AddWithValue("$kind", Wire(kind));
        command.Parameters.AddWithValue("$keep", IHistoryStore.StoredLimit);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Formats the requested value.
    private static string Format(DateTimeOffset instant) =>
        instant.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    // Opens the requested resource.
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (Volatile.Read(ref _ready))
        {
            return connection;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!_ready)
            {
                await UserDataSchema.CreateAsync(connection, cancellationToken);
                Volatile.Write(ref _ready, true);
            }
        }
        finally
        {
            _gate.Release();
        }

        return connection;
    }

    // Gets the wire representation.
    private static string Wire(HistoryKind kind) => kind switch
    {
        HistoryKind.SendFrame => "SendFrame",
        HistoryKind.Filter => "Filter",

        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "This history kind has no frozen on-disk name."),
    };
}
