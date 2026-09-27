using DiCAN.Core.Sending;
using DiCAN.Infrastructure.Devices;
using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Sending;

// Stores the transmit queue.
public sealed class SqliteTransmitQueueStore : ITransmitQueueStore
{
    private readonly string _connectionString;
    private bool _ready;

    // Initializes this instance.
    public SqliteTransmitQueueStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    // Opens the requested resource.
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!_ready)
        {
            await UserDataSchema.CreateAsync(connection, cancellationToken);
            _ready = true;
        }

        return connection;
    }

    // Loads saved data.
    public async Task<IReadOnlyList<TransmitQueueRow>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT position, name, kind_index, can_id, data, period, repeat_count, remote_length
            FROM transmit_row ORDER BY position;
            """;

        var rows = new List<TransmitQueueRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new TransmitQueueRow(
                Position: reader.GetInt32(0),
                Name: reader.GetString(1),
                KindIndex: reader.GetInt32(2),
                Id: reader.GetString(3),
                Data: reader.GetString(4),
                Period: reader.GetString(5),
                Repeat: reader.GetString(6),
                RemoteLength: reader.GetString(7)));
        }

        return rows;
    }

    // Saves application data.
    public async Task SaveAsync(
        IReadOnlyList<TransmitQueueRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM transmit_row;";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO transmit_row
                (position, name, kind_index, can_id, data, period, repeat_count, remote_length)
            VALUES ($p, $n, $k, $i, $d, $t, $r, $l);
            """;

        SqliteParameter position = insert.Parameters.Add("$p", SqliteType.Integer);
        SqliteParameter name = insert.Parameters.Add("$n", SqliteType.Text);
        SqliteParameter kind = insert.Parameters.Add("$k", SqliteType.Integer);
        SqliteParameter id = insert.Parameters.Add("$i", SqliteType.Text);
        SqliteParameter data = insert.Parameters.Add("$d", SqliteType.Text);
        SqliteParameter period = insert.Parameters.Add("$t", SqliteType.Text);
        SqliteParameter repeat = insert.Parameters.Add("$r", SqliteType.Text);
        SqliteParameter remote = insert.Parameters.Add("$l", SqliteType.Text);

        for (var index = 0; index < rows.Count; index++)
        {
            TransmitQueueRow row = rows[index];

            position.Value = index;
            name.Value = row.Name;
            kind.Value = row.KindIndex;
            id.Value = row.Id;
            data.Value = row.Data;
            period.Value = row.Period;
            repeat.Value = row.Repeat;
            remote.Value = row.RemoteLength;

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
