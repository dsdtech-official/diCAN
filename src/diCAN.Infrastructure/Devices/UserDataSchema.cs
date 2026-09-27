using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Devices;

// Manages user data schema.
internal static class UserDataSchema
{

    public const int Version = 1;

    // Creates the requested object.
    public static async Task CreateAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {

        bool fresh = !await HasOurTablesAsync(connection, cancellationToken);

        int found = await ReadVersionAsync(connection, cancellationToken);

        if (found > Version)
        {
            throw new InvalidOperationException(
                $"This diCAN data file was written by a newer version of the program "
                + $"(file format {found}, this build understands {Version}). Update diCAN, or "
                + $"point it at a different data folder.");
        }

        if (!fresh && found < Version)
        {
            BackUp(connection.DataSource, found);
        }

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS devices (
                id                   INTEGER PRIMARY KEY,
                device_key           TEXT    NOT NULL,
                generation           TEXT    NOT NULL,
                serial_number        TEXT,
                usb_vid              INTEGER NOT NULL,
                usb_pid              INTEGER NOT NULL,
                version_response     TEXT,
                firmware_build       TEXT,
                has_quartz           INTEGER,
                first_connected_at   TEXT    NOT NULL,
                last_connected_at    TEXT    NOT NULL,
                connect_count        INTEGER NOT NULL,
                last_port_name       TEXT    NOT NULL,
                last_nominal_bitrate INTEGER NOT NULL,
                last_data_bitrate    INTEGER,
                last_mode            TEXT    NOT NULL,
                note                 TEXT,
                app_version          TEXT    NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_devices_identity
                ON devices(device_key, generation);

            CREATE TABLE IF NOT EXISTS history (
                id           INTEGER PRIMARY KEY,
                kind         TEXT    NOT NULL,
                text         TEXT    NOT NULL,
                frame_id     INTEGER,
                is_extended  INTEGER,
                last_used_at TEXT    NOT NULL,
                use_count    INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_history_identity
                ON history(kind, text, COALESCE(frame_id, -1), COALESCE(is_extended, -1));

            CREATE INDEX IF NOT EXISTS ix_history_recent
                ON history(kind, last_used_at DESC);

            CREATE TABLE IF NOT EXISTS settings (
                key        TEXT PRIMARY KEY,
                value      TEXT NOT NULL,
                note       TEXT NOT NULL DEFAULT '',
                value_type TEXT NOT NULL DEFAULT '',
                chosen_at  TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS transmit_row (
                position      INTEGER PRIMARY KEY,
                name          TEXT NOT NULL,
                kind_index    INTEGER NOT NULL,
                can_id        TEXT NOT NULL,
                data          TEXT NOT NULL,
                period        TEXT NOT NULL,
                repeat_count  TEXT NOT NULL,
                remote_length TEXT NOT NULL
            );

            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        if (!fresh)
        {
            for (int step = found + 1; step <= Version; step++)
            {
                await ApplyStepAsync(connection, transaction, step, cancellationToken);
            }
        }

        await StampAsync(connection, transaction, Version, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Applies step.
    private static Task ApplyStepAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int step,
        CancellationToken cancellationToken) => step switch
        {
            1 => Task.CompletedTask,

            _ => throw new InvalidOperationException(
                $"No migration is defined for user data schema step {step}."),
        };

    // Reads version.
    private static async Task<int> ReadVersionAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        object? value = await command.ExecuteScalarAsync(cancellationToken);

        return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Updates the schema version.
    private static async Task StampAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version = {version};";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Checks our tables.
    private static async Task<bool> HasOurTablesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'settings' LIMIT 1;";

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    // Backs up the current data.
    private static void BackUp(string path, int fromVersion)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, $"{path}.v{fromVersion}.bak", overwrite: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Migrates saved data.
    public static void MigrateLegacyFile(string legacyPath, string currentPath)
    {
        if (!File.Exists(legacyPath) || File.Exists(currentPath))
        {
            return;
        }

        try
        {
            File.Move(legacyPath, currentPath);
        }
        catch (IOException)
        {

        }
    }
}
