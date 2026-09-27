using DiCAN.Core.Abstractions;
using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Devices;

// Stores saved device details.
public sealed class SqliteDeviceRegistry : IDeviceRegistry
{
    private readonly string _connectionString;
    private readonly IMonotonicClock _clock;
    private readonly string _appVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    // Initializes this instance.
    public SqliteDeviceRegistry(string databasePath, IMonotonicClock clock, string appVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _clock = clock;
        _appVersion = appVersion;

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

    // Records connection.
    public async Task<CanDeviceRecord> RecordConnectionAsync(
        CanConnectionFact fact, CancellationToken cancellationToken = default)
    {

        string deviceKey = fact.Device.RegistryKey;

        SlcanDeviceInfo.TryParse(fact.VersionResponse ?? string.Empty, out SlcanDeviceInfo? report);

        string now = Format(_clock.Now);

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO devices (device_key, generation, serial_number, usb_vid, usb_pid,
                                 version_response, firmware_build, has_quartz,
                                 first_connected_at, last_connected_at, connect_count,
                                 last_port_name, last_nominal_bitrate, last_data_bitrate,
                                 last_mode, app_version)
            VALUES ($key, $gen, $serial, $vid, $pid, $v, $build, $quartz,
                    $now, $now, 1, $port, $nominal, $data, $mode, $app)
            ON CONFLICT(device_key, generation) DO UPDATE SET
                serial_number        = excluded.serial_number,
                usb_vid              = excluded.usb_vid,
                usb_pid              = excluded.usb_pid,
                version_response     = excluded.version_response,
                firmware_build       = excluded.firmware_build,
                has_quartz           = excluded.has_quartz,
                last_connected_at    = excluded.last_connected_at,
                connect_count        = connect_count + 1,
                last_port_name       = excluded.last_port_name,
                last_nominal_bitrate = excluded.last_nominal_bitrate,
                last_data_bitrate    = excluded.last_data_bitrate,
                last_mode            = excluded.last_mode,
                app_version          = excluded.app_version;
            """;

        command.Parameters.AddWithValue("$key", deviceKey);
        command.Parameters.AddWithValue("$gen", fact.Generation.ToString());
        command.Parameters.AddWithValue("$serial", (object?)fact.Device.SerialNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$vid", fact.Device.VendorId);
        command.Parameters.AddWithValue("$pid", fact.Device.ProductId);
        command.Parameters.AddWithValue("$v", (object?)fact.VersionResponse ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$build",
            (object?)SlcanFirmwareId.BuildOf(fact.VersionResponse, report) ?? DBNull.Value);
        command.Parameters.AddWithValue("$quartz", QuartzOf(report));
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$port", fact.PortName);
        command.Parameters.AddWithValue("$nominal", fact.NominalBitrate);
        command.Parameters.AddWithValue("$data", (object?)fact.DataBitrate ?? DBNull.Value);
        command.Parameters.AddWithValue("$mode", fact.Mode.ToString());
        command.Parameters.AddWithValue("$app", _appVersion);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return await ReadAsync(connection, deviceKey, fact.Generation, cancellationToken)
               ?? throw new InvalidOperationException(
                   $"The registry row for {deviceKey} was not there immediately after writing it.");
    }

    // Finds the requested item.
    public async Task<CanDeviceRecord?> FindAsync(
        string deviceKey,
        SlcanFirmwareGeneration generation,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);

        return await ReadAsync(connection, deviceKey, generation, cancellationToken);
    }

    // Lists the requested items.
    public async Task<IReadOnlyList<CanDeviceRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = SelectColumns + " ORDER BY last_connected_at DESC;";

        var records = new List<CanDeviceRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Read(reader));
        }

        return records;
    }

    private const string SelectColumns =
        """
        SELECT device_key, generation, serial_number, usb_vid, usb_pid, version_response,
               firmware_build, has_quartz, first_connected_at, last_connected_at,
               connect_count, last_port_name, last_nominal_bitrate, last_data_bitrate,
               last_mode, note, app_version
        FROM devices
        """;

    // Reads input data.
    private static async Task<CanDeviceRecord?> ReadAsync(
        SqliteConnection connection,
        string deviceKey,
        SlcanFirmwareGeneration generation,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = SelectColumns + " WHERE device_key = $key AND generation = $gen;";
        command.Parameters.AddWithValue("$key", deviceKey);
        command.Parameters.AddWithValue("$gen", generation.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    // Reads input data.
    private static CanDeviceRecord Read(SqliteDataReader reader) =>
        new(
                DeviceKey: reader.GetString(0),
                Generation: ParseGeneration(reader.GetString(1)),
                SerialNumber: reader.IsDBNull(2) ? null : reader.GetString(2),
                VendorId: (ushort)reader.GetInt32(3),
                ProductId: (ushort)reader.GetInt32(4),
                VersionResponse: reader.IsDBNull(5) ? null : reader.GetString(5),
                FirmwareBuild: reader.IsDBNull(6) ? null : reader.GetString(6),
                HasQuartz: reader.IsDBNull(7) ? null : reader.GetInt32(7) != 0,
                FirstConnectedAt: DateTimeOffset.Parse(reader.GetString(8), null),
                LastConnectedAt: DateTimeOffset.Parse(reader.GetString(9), null),
                ConnectCount: reader.GetInt32(10),
                LastPortName: reader.GetString(11),
                LastNominalBitrate: reader.GetInt32(12),
                LastDataBitrate: reader.IsDBNull(13) ? null : reader.GetInt32(13),
                LastMode: ParseMode(reader.GetString(14)),
                Note: reader.IsDBNull(15) ? null : reader.GetString(15),
                AppVersion: reader.GetString(16));

    // Sets note.
    public async Task SetNoteAsync(
        string deviceKey,
        SlcanFirmwareGeneration generation,
        string? note,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            "UPDATE devices SET note = $note WHERE device_key = $key AND generation = $gen;";
        command.Parameters.AddWithValue("$note", Clamp(note));
        command.Parameters.AddWithValue("$key", deviceKey);
        command.Parameters.AddWithValue("$gen", generation.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

    // Limits the requested value.
    private static object Clamp(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return DBNull.Value;
        }

        string trimmed = note.Trim();

        return trimmed.Length <= IDeviceRegistry.MaxNoteLength
            ? trimmed
            : trimmed[..IDeviceRegistry.MaxNoteLength];
    }

    // Gets the clock frequency.
    private static object QuartzOf(SlcanDeviceInfo? report) =>
        report?.HasQuartz is { } quartz ? (quartz ? 1 : 0) : DBNull.Value;

    // Formats the requested value.
    private static string Format(DateTimeOffset instant) =>
        instant.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    // Parses generation.
    private static SlcanFirmwareGeneration ParseGeneration(string value) =>
        Enum.TryParse(value, out SlcanFirmwareGeneration generation)
            ? generation
            : SlcanFirmwareGeneration.Unknown;

    // Parses mode.
    private static SlcanOpenMode ParseMode(string value) =>
        Enum.TryParse(value, out SlcanOpenMode mode) ? mode : SlcanOpenMode.Normal;
}
