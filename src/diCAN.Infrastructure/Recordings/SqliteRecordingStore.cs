using System.Globalization;
using DiCAN.Core.Abstractions;
using DiCAN.Core.Protocol;
using DiCAN.Core.Recordings;
using DiCAN.Core.Streaming;
using DiCAN.Core.Transport;
using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Recordings;

// Stores recordings.
public sealed class SqliteRecordingStore : IRecordingStore
{
    private readonly string _volumeFolder;
    private readonly IMonotonicClock _clock;

    // Initializes this instance.
    public SqliteRecordingStore(IAppPaths paths, IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _volumeFolder = paths.RecordingsFolder;
        _clock = clock;

        Directory.CreateDirectory(_volumeFolder);
    }

    // Gets volume path.
    public string VolumePath(RecordingVolume volume) =>
        Path.Combine(_volumeFolder, volume.FileName);

    // Starts the operation.
    public async Task<IRecordingSession> StartAsync(
        RecordingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Adapter);

        RecordingVolume volume = RecordingVolume.For(request.StartedAt);
        string volumePath = VolumePath(volume);

        long id;

        await using (SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
            volumePath, SqliteOpenMode.ReadWriteCreate, cancellationToken))
        {
            await RecordingVolumeSchema.CreateAsync(
                connection, volume.Key, request.AppVersion, cancellationToken);

            id = await InsertAsync(connection, request, cancellationToken);
        }

        var recording = new Recording(
            id,
            volume.Key,
            request.Adapter,
            request.StartedAt,
            null,
            request.DeviceKey,
            request.Generation,
            request.Port,
            request.Configuration,
            request.Scope,
            request.StoreRawText,
            request.FirmwareBuild,
            request.AppVersion);

        return new SqliteRecordingSession(
            recording, volumePath, _clock, FinishAsync, request.InitialFilter);
    }

    // Lists the requested items.
    public async Task<IReadOnlyList<Recording>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        RecordingLibrary library = await ListLibraryAsync(cancellationToken);

        if (library.UnreadableVolumes.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(library.UnreadableVolumes[0].Error)
                .Throw();
        }

        return library.Recordings;
    }

    // Lists the requested items.
    public async Task<RecordingLibrary> ListLibraryAsync(CancellationToken cancellationToken = default)
    {
        var recordings = new List<Recording>();
        var unreadable = new List<UnreadableRecordingVolume>();

        foreach (RecordingVolume volume in Volumes())
        {
            try
            {

                await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
                    VolumePath(volume), SqliteOpenMode.ReadOnly, cancellationToken);

                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = SelectColumns + " FROM recording;";

                await using SqliteDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    recordings.Add(Read(reader, volume.Key));
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {

                unreadable.Add(new UnreadableRecordingVolume(
                    volume.Key, ClassifyVolumeProblem(error), error));

                RecordingVolumeSchema.ReleasePooledConnections(VolumePath(volume), SqliteOpenMode.ReadOnly);
            }
        }

        recordings.Sort(static (left, right) =>
        {
            int byTime = right.StartedAt.CompareTo(left.StartedAt);

            return byTime != 0 ? byTime : right.Id.CompareTo(left.Id);
        });

        unreadable.Sort(static (left, right) => string.CompareOrdinal(right.VolumeKey, left.VolumeKey));

        return new RecordingLibrary(recordings, unreadable);
    }

    // Classifies the input value.
    public static RecordingVolumeProblem ClassifyVolumeProblem(Exception error) => error switch
    {
        RecordingVolumeTooNewException => RecordingVolumeProblem.WrittenByNewerBuild,
        SqliteException { SqliteErrorCode: 11 or 26 } => RecordingVolumeProblem.Damaged,
        _ => RecordingVolumeProblem.Unreadable,
    };

    // Gets library bytes.
    public Task<long> GetLibraryBytesAsync(CancellationToken cancellationToken = default)
    {
        long total = 0;

        foreach (RecordingVolume volume in Volumes())
        {
            string path = VolumePath(volume);

            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            {
                try
                {
                    var info = new FileInfo(file);

                    if (info.Exists)
                    {
                        total += info.Length;
                    }
                }
                catch (IOException)
                {

                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return Task.FromResult(total);
    }

    // Reads frames.
    public async IAsyncEnumerable<RecordedFrame> ReadFramesAsync(
        Recording recording,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!TryVolumePath(recording.Volume, out string? volumePath))
        {
            yield break;
        }

        await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
            volumePath, SqliteOpenMode.ReadOnly, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT frame_seq, t_us, dir, can_id, flags, len, data, raw
            FROM frame WHERE recording_id = $id ORDER BY frame_seq;
            """;
        command.Parameters.AddWithValue("$id", recording.Id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            long microseconds = reader.GetInt64(1);
            var flags = (RecordingFrameFlags)reader.GetInt32(4);

            yield return new RecordedFrame(
                reader.GetInt64(0),
                microseconds,
                reader.GetInt32(2) == 1
                    ? CanFrameDirection.Transmitted
                    : CanFrameDirection.Received,
                flags.ToFrame(
                    reader.GetInt32(3),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? ReadOnlyMemory<byte>.Empty : (byte[])reader[6],
                    recording.StartedAt.AddMicroseconds(microseconds)),
                reader.IsDBNull(7) ? null : reader.GetString(7));
        }
    }

    // Counts the requested items.
    public async Task<RecordingFrameTally> CountFramesAsync(
        Recording recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!TryVolumePath(recording.Volume, out string? volumePath))
        {

            return default;
        }

        await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
            volumePath, SqliteOpenMode.ReadOnly, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*),
                   SUM(CASE WHEN (flags & $fd) != 0 THEN 1 ELSE 0 END)
            FROM frame
            WHERE recording_id = $id;
            """;

        command.Parameters.AddWithValue("$fd", (long)RecordingFrameFlags.Fd);
        command.Parameters.AddWithValue("$id", recording.Id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return default;
        }

        return new RecordingFrameTally(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1));
    }

    // Reads filter changes.
    public async Task<IReadOnlyList<RecordingFilterChange>> ReadFilterChangesAsync(
        Recording recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!TryVolumePath(recording.Volume, out string? volumePath))
        {
            return [];
        }

        await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
            volumePath, SqliteOpenMode.ReadOnly, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT at_us, expression FROM filter_change
            WHERE recording_id = $id ORDER BY at_us;
            """;
        command.Parameters.AddWithValue("$id", recording.Id);

        var changes = new List<RecordingFilterChange>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new RecordingFilterChange(reader.GetInt64(0), reader.GetString(1)));
        }

        return changes;
    }

    // Deletes the requested entry.
    public async Task DeleteAsync(
        Recording recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!TryVolumePath(recording.Volume, out string? volumePath))
        {
            return;
        }

        await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
            volumePath, SqliteOpenMode.ReadWrite, cancellationToken);

        await using SqliteCommand remove = connection.CreateCommand();
        remove.CommandText = "DELETE FROM recording WHERE recording_id = $id;";
        remove.Parameters.AddWithValue("$id", recording.Id);

        await remove.ExecuteNonQueryAsync(cancellationToken);

        await RecordingVolumeSchema.CheckpointAsync(connection);
    }

    // Compacts stored data.
    public async Task<RecordingCompactResult> CompactAsync(
        CancellationToken cancellationToken = default)
    {

        long before = await GetLibraryBytesAsync(cancellationToken);

        int compacted = 0;
        int skipped = 0;

        foreach (RecordingVolume volume in Volumes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = VolumePath(volume);

            try
            {
                await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
                    path, SqliteOpenMode.ReadWrite, cancellationToken);

                await RecordingVolumeSchema.CheckpointAsync(connection);

                await using (SqliteCommand vacuum = connection.CreateCommand())
                {
                    vacuum.CommandText = "VACUUM;";

                    vacuum.CommandTimeout = 600;

                    await vacuum.ExecuteNonQueryAsync(cancellationToken);
                }

                await RecordingVolumeSchema.CheckpointAsync(connection);

                compacted++;
            }
            catch (SqliteException)
            {

                skipped++;
            }
            catch (IOException)
            {
                skipped++;
            }
            finally
            {

                RecordingVolumeSchema.ReleasePooledConnections(path, SqliteOpenMode.ReadWrite);
            }
        }

        long after = await GetLibraryBytesAsync(cancellationToken);

        return new RecordingCompactResult(before, after, compacted, skipped);
    }

    // Tries volume path.
    private bool TryVolumePath(
        string volumeKey, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? path)
    {
        path = null;

        if (!RecordingVolume.TryParse("recordings-" + volumeKey + ".db", out RecordingVolume volume))
        {
            return false;
        }

        string candidate = VolumePath(volume);

        if (!File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    // Lists recording volumes.
    private IEnumerable<RecordingVolume> Volumes()
    {
        if (!Directory.Exists(_volumeFolder))
        {
            yield break;
        }

        var found = new List<RecordingVolume>();

        foreach (string file in Directory.EnumerateFiles(_volumeFolder, "recordings-*"))
        {
            if (RecordingVolume.TryParse(Path.GetFileName(file), out RecordingVolume volume))
            {
                found.Add(volume);
            }
        }

        found.Sort(static (left, right) => left.Key.CompareTo(right.Key));

        foreach (RecordingVolume volume in found)
        {
            yield return volume;
        }
    }

    // Inserts the requested entry.
    private static async Task<long> InsertAsync(
        SqliteConnection connection, RecordingRequest request, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO recording (
                adapter, started_at, started_at_offset, ended_at, device_key, generation,
                port, mode, nominal_bitrate, data_bitrate, scope, stored_raw, firmware_build,
                app_version)
            VALUES ($n, $s, $o, NULL, $k, $g, $p, $m, $nb, $db, $sc, $raw, $fw, $app)
            RETURNING recording_id;
            """;

        command.Parameters.AddWithValue("$n", request.Adapter);
        command.Parameters.AddWithValue(
            "$s", request.StartedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$o", Offset(request.StartedAt));
        command.Parameters.AddWithValue("$k", request.DeviceKey);
        command.Parameters.AddWithValue("$g", request.Generation.ToString());
        command.Parameters.AddWithValue("$p", request.Port);
        command.Parameters.AddWithValue("$m", request.Configuration.Mode.ToString());
        command.Parameters.AddWithValue("$nb", request.Configuration.NominalBitrate);
        command.Parameters.AddWithValue(
            "$db", (object?)request.Configuration.DataBitrate ?? DBNull.Value);
        command.Parameters.AddWithValue("$sc", request.Scope.ToString());
        command.Parameters.AddWithValue("$raw", request.StoreRawText ? 1 : 0);
        command.Parameters.AddWithValue("$fw", (object?)request.FirmwareBuild ?? DBNull.Value);
        command.Parameters.AddWithValue("$app", request.AppVersion);

        object? id = await command.ExecuteScalarAsync(cancellationToken);

        if (id is null or DBNull)
        {
            throw new InvalidOperationException(
                "The recording could not be written to the database: the insert returned no id. "
                + "Nothing was recorded.");
        }

        return Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    // Finishes the active operation.
    private async Task FinishAsync(Recording recording, CancellationToken cancellationToken)
    {
        if (!TryVolumePath(recording.Volume, out string? volumePath))
        {
            return;
        }

        await using SqliteConnection connection = await RecordingVolumeSchema.OpenAsync(
            volumePath, SqliteOpenMode.ReadWrite, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "UPDATE recording SET ended_at = $e WHERE recording_id = $id;";
        command.Parameters.AddWithValue(
            "$e",
            recording.EndedAt is { } ended
                ? ended.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", recording.Id);

        await command.ExecuteNonQueryAsync(cancellationToken);

        await RecordingVolumeSchema.CheckpointAsync(connection);
    }

    private const string SelectColumns =
        """
        SELECT recording_id, adapter, started_at, started_at_offset, ended_at, device_key,
               generation, port, mode, nominal_bitrate, data_bitrate, scope, stored_raw,
               firmware_build, app_version
        """;

    // Reads input data.
    private static Recording Read(SqliteDataReader reader, string volumeKey)
    {
        var startedUtc = DateTimeOffset.Parse(
            reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        string offsetText = reader.GetString(3);
        TimeSpan magnitude = TimeSpan.Parse(
            offsetText.TrimStart('+', '-'), CultureInfo.InvariantCulture);
        TimeSpan offset = offsetText.StartsWith('-') ? -magnitude : magnitude;

        return new Recording(
            reader.GetInt64(0),
            volumeKey,
            reader.GetString(1),
            startedUtc.ToOffset(offset),
            reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(4),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind).ToOffset(offset),
            reader.GetString(5),
            Enum.TryParse(reader.GetString(6), out SlcanFirmwareGeneration generation)
                ? generation
                : SlcanFirmwareGeneration.Unknown,
            reader.GetString(7),
            new CanBusConfiguration(
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Enum.TryParse(reader.GetString(8), out SlcanOpenMode mode)
                    ? mode
                    : SlcanOpenMode.Normal),
            Enum.TryParse(reader.GetString(11), out RecordingScope scope)
                ? scope
                : RecordingScope.FilteredOnly,
            reader.GetInt64(12) != 0,
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetString(14));
    }

    // Gets offset.
    private static string Offset(DateTimeOffset value) =>
        (value.Offset < TimeSpan.Zero ? "-" : "+") +
        value.Offset.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);
}
