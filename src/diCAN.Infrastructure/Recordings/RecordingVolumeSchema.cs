using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Recordings;

// Manages recording tables.
internal static class RecordingVolumeSchema
{

    public const int Version = 1;

    private const string WritePragmas =
        """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous  = NORMAL;
        """;

    // Creates the requested object.
    public static async Task CreateAsync(
        SqliteConnection connection,
        string coversMonth,
        string appVersion,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = WritePragmas;
            await pragmas.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS recording (
                    recording_id      INTEGER PRIMARY KEY AUTOINCREMENT,

                    adapter           TEXT    NOT NULL,
                    started_at        TEXT    NOT NULL,
                    started_at_offset TEXT    NOT NULL,
                    ended_at          TEXT,
                    device_key        TEXT    NOT NULL,
                    generation        TEXT    NOT NULL,
                    port              TEXT    NOT NULL,
                    mode              TEXT    NOT NULL,
                    nominal_bitrate   INTEGER NOT NULL,
                    data_bitrate      INTEGER,
                    scope             TEXT    NOT NULL,
                    stored_raw        INTEGER NOT NULL,
                    firmware_build    TEXT,
                    app_version       TEXT    NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_recording_recent
                    ON recording(started_at DESC);

                CREATE TABLE IF NOT EXISTS frame (
                    frame_seq    INTEGER PRIMARY KEY AUTOINCREMENT,
                    recording_id INTEGER NOT NULL
                                 REFERENCES recording(recording_id) ON DELETE CASCADE,
                    t_us         INTEGER NOT NULL,
                    dir          INTEGER NOT NULL,
                    can_id       INTEGER NOT NULL,
                    flags        INTEGER NOT NULL,
                    len          INTEGER NOT NULL,
                    data         BLOB,
                    raw          TEXT
                );

                CREATE INDEX IF NOT EXISTS ix_frame_recording
                    ON frame(recording_id, frame_seq);

                CREATE TABLE IF NOT EXISTS filter_change (
                    recording_id INTEGER NOT NULL
                                 REFERENCES recording(recording_id) ON DELETE CASCADE,
                    at_us        INTEGER NOT NULL,
                    expression   TEXT    NOT NULL,

                    PRIMARY KEY (recording_id, at_us)
                ) WITHOUT ROWID;

                CREATE TABLE IF NOT EXISTS volume_info (
                    schema_version INTEGER NOT NULL,
                    app_version    TEXT    NOT NULL,
                    covers_month   TEXT    NOT NULL
                );
                """;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand info = connection.CreateCommand())
        {
            info.Transaction = transaction;
            info.CommandText =
                """
                INSERT INTO volume_info (schema_version, app_version, covers_month)
                SELECT $v, $a, $m WHERE NOT EXISTS (SELECT 1 FROM volume_info);
                """;
            info.Parameters.AddWithValue("$v", Version);
            info.Parameters.AddWithValue("$a", appVersion);
            info.Parameters.AddWithValue("$m", coversMonth);

            await info.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Opens the requested resource.
    public static async Task<SqliteConnection> OpenAsync(
        string path,
        SqliteOpenMode mode,
        CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(ConnectionStringFor(path, mode));

        try
        {
            await connection.OpenAsync(cancellationToken);

            await using (SqliteCommand keys = connection.CreateCommand())
            {
                keys.CommandText = "PRAGMA foreign_keys = ON;";
                await keys.ExecuteNonQueryAsync(cancellationToken);
            }

            await VerifyAsync(connection, path, cancellationToken);
        }
        catch
        {

            await connection.DisposeAsync();

            ReleasePooledConnections(path, mode);
            throw;
        }

        return connection;
    }

    // Releases held resources.
    public static void ReleasePooledConnections(string path, SqliteOpenMode mode)
    {
        using var sameKey = new SqliteConnection(ConnectionStringFor(path, mode));
        SqliteConnection.ClearPool(sameKey);
    }

    // Gets the database connection.
    private static string ConnectionStringFor(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode }.ToString();

    // Flushes pending database data.
    public static Task CheckpointAsync(SqliteConnection connection) =>
        Task.Run(() =>
        {
            try
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";

                command.CommandTimeout = 1;

                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {

            }
        });

    // Checks stored data.
    private static async Task VerifyAsync(
        SqliteConnection connection, string path, CancellationToken cancellationToken)
    {

        await using SqliteCommand exists = connection.CreateCommand();
        exists.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'volume_info';";

        if (await exists.ExecuteScalarAsync(cancellationToken) is null)
        {
            return;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM volume_info LIMIT 1;";

        object? found = await command.ExecuteScalarAsync(cancellationToken);

        if (found is null or DBNull)
        {
            return;
        }

        int version = Convert.ToInt32(found, System.Globalization.CultureInfo.InvariantCulture);

        if (version > Version)
        {
            throw new RecordingVolumeTooNewException(
                $"The recording volume {Path.GetFileName(path)} was written by a newer version of "
                + $"diCAN (volume format {version}, this build understands {Version}). "
                + "Update diCAN to open this month's recordings.");
        }
    }
}

// Reports an operation error.
public sealed class RecordingVolumeTooNewException(string message) : InvalidOperationException(message);
