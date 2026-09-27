using System.Globalization;
using System.Threading.Channels;
using DiCAN.Core.Abstractions;
using DiCAN.Core.Protocol;
using DiCAN.Core.Recordings;
using DiCAN.Core.Streaming;
using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Recordings;

// Records incoming CAN frames.
internal sealed class SqliteRecordingSession : IRecordingSession
{

    private const int BatchSize = 2000;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    // Creates a history entry.
    private readonly record struct Entry(
        long TimeMicroseconds,
        CanFrameDirection Direction,
        CanFrame? Frame,
        string? RawText,
        string? FilterExpression);

    internal const int QueueCapacity = 60_000;

    private readonly Channel<Entry> _queue = Channel.CreateBounded<Entry>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    internal int QueuedEntries => (int)Interlocked.Read(ref _queued);

    private long _queued;

    private readonly CancellationTokenSource _abort = new();
    private readonly string _volumePath;
    private readonly IMonotonicClock _clock;
    private readonly Func<Recording, CancellationToken, Task> _onFinished;
    private readonly Task _writer;

    private long _frameCount;
    private long _filterChangeCount;
    private volatile string? _fault;
    private int _stopped;

    // Initializes this instance.
    public SqliteRecordingSession(
        Recording recording,
        string volumePath,
        IMonotonicClock clock,
        Func<Recording, CancellationToken, Task> onFinished,
        string? initialFilter)
    {
        Recording = recording;
        _volumePath = volumePath;
        _clock = clock;
        _onFinished = onFinished;

        if (recording.Scope == RecordingScope.FilteredOnly)
        {
            _queue.Writer.TryWrite(new Entry(
                0, CanFrameDirection.Received, null, null, initialFilter ?? string.Empty));
        }

        _writer = Task.Run(() => WriteLoopAsync(_abort.Token));
    }

    public Recording Recording { get; private set; }

    public event EventHandler<RecordingFaultEventArgs>? Fault;

    // Adds the requested entry.
    public void Add(CanFrame frame, CanFrameDirection direction, string? rawText)
    {

        if (_queue.Writer.TryWrite(new Entry(
            RecordingTime.Microseconds(Recording.StartedAt, frame.Timestamp),
            direction,
            frame,
            rawText,
            null)))
        {
            Interlocked.Increment(ref _queued);
        }
        else
        {
            StopBecauseTheDiskFellBehind();
        }
    }

    // Filters changed.
    public void FilterChanged(string expression)
    {
        if (_queue.Writer.TryWrite(new Entry(
            RecordingTime.Microseconds(Recording.StartedAt, _clock.Now),
            CanFrameDirection.Received,
            null,
            null,
            expression ?? string.Empty)))
        {
            Interlocked.Increment(ref _queued);
        }
        else
        {
            StopBecauseTheDiskFellBehind();
        }
    }

    // Stops the active operation.
    private void StopBecauseTheDiskFellBehind()
    {

        if (Interlocked.Exchange(ref _overflowed, 1) != 0)
        {
            return;
        }

        _fault ??=
            $"The disk could not keep up: {QueueCapacity:N0} frames were waiting to be written. " +
            $"Recording stopped; {Interlocked.Read(ref _frameCount):N0} frame(s) were saved.";

        _queue.Writer.TryComplete();
        Fault?.Invoke(this, new RecordingFaultEventArgs(_fault));
    }

    private int _overflowed;

    // Stops the active operation.
    public async Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _queue.Writer.TryComplete();

            Task finished = await Task.WhenAny(_writer, Task.Delay(StopTimeout, cancellationToken));

            if (finished != _writer)
            {
                await _abort.CancelAsync();
                _fault ??= "Recording did not finish writing within " +
                           StopTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) +
                           " s; some frames may be missing.";
            }

            try
            {
                await _writer;
            }
            catch (OperationCanceledException)
            {

            }

            Recording = Recording with { EndedAt = _clock.Now };
            await _onFinished(Recording, CancellationToken.None);
        }

        return new RecordingResult(
            Recording,
            Interlocked.Read(ref _frameCount),
            Interlocked.Read(ref _filterChangeCount),
            VolumeBytes(),
            _fault);
    }

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        if (_stopped == 0)
        {
            await StopAsync();
        }

        _abort.Dispose();
    }

    // Writes loop.
    private async Task WriteLoopAsync(CancellationToken abort)
    {
        SqliteConnection? connection = null;

        try
        {

            connection = await RecordingVolumeSchema.OpenAsync(
                _volumePath, SqliteOpenMode.ReadWrite, abort);

            var batch = new List<Entry>(BatchSize);
            DateTimeOffset lastCommit = _clock.Now;

            while (!abort.IsCancellationRequested)
            {
                bool more = await WaitForWorkAsync(abort);

                while (batch.Count < BatchSize && _queue.Reader.TryRead(out Entry entry))
                {
                    batch.Add(entry);
                    Interlocked.Decrement(ref _queued);
                }

                bool due = _clock.Now - lastCommit >= FlushInterval;

                if (batch.Count >= BatchSize || (batch.Count > 0 && (due || !more)))
                {
                    await CommitAsync(connection, batch, abort);
                    batch.Clear();
                    lastCommit = _clock.Now;
                }
                else if (due)
                {
                    lastCommit = _clock.Now;
                }

                if (!more && batch.Count == 0 && _queue.Reader.Completion.IsCompleted)
                {
                    break;
                }
            }

        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {

            _fault ??= error.Message;
            _queue.Writer.TryComplete();
            Fault?.Invoke(this, new RecordingFaultEventArgs(_fault));
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    // Waits for pending work.
    private async Task<bool> WaitForWorkAsync(CancellationToken abort)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(abort);
        timeout.CancelAfter(FlushInterval);

        try
        {
            return await _queue.Reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
            return false;
        }
    }

    // Commits the requested changes.
    private async Task CommitAsync(
        SqliteConnection connection, List<Entry> batch, CancellationToken abort)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(abort);

        await using SqliteCommand frames = connection.CreateCommand();
        frames.Transaction = transaction;
        frames.CommandText =
            """
            INSERT INTO frame (recording_id, t_us, dir, can_id, flags, len, data, raw)
            VALUES ($r, $t, $d, $i, $f, $l, $b, $w);
            """;

        SqliteParameter recordingId = frames.Parameters.Add("$r", SqliteType.Integer);
        SqliteParameter time = frames.Parameters.Add("$t", SqliteType.Integer);
        SqliteParameter dir = frames.Parameters.Add("$d", SqliteType.Integer);
        SqliteParameter id = frames.Parameters.Add("$i", SqliteType.Integer);
        SqliteParameter flags = frames.Parameters.Add("$f", SqliteType.Integer);
        SqliteParameter length = frames.Parameters.Add("$l", SqliteType.Integer);
        SqliteParameter data = frames.Parameters.Add("$b", SqliteType.Blob);
        SqliteParameter raw = frames.Parameters.Add("$w", SqliteType.Text);

        recordingId.Value = Recording.Id;

        await using SqliteCommand changes = connection.CreateCommand();
        changes.Transaction = transaction;
        changes.CommandText =
            """
            INSERT INTO filter_change (recording_id, at_us, expression) VALUES ($r, $t, $e)
            ON CONFLICT(recording_id, at_us) DO UPDATE SET expression = excluded.expression;
            """;

        SqliteParameter changeRecording = changes.Parameters.Add("$r", SqliteType.Integer);
        SqliteParameter changeAt = changes.Parameters.Add("$t", SqliteType.Integer);
        SqliteParameter expression = changes.Parameters.Add("$e", SqliteType.Text);
        changeRecording.Value = Recording.Id;

        long written = 0;
        long changed = 0;

        foreach (Entry entry in batch)
        {
            if (entry.FilterExpression is { } text)
            {
                changeAt.Value = entry.TimeMicroseconds;
                expression.Value = text;
                await changes.ExecuteNonQueryAsync(abort);
                changed++;
                continue;
            }

            CanFrame frame = entry.Frame!;

            time.Value = entry.TimeMicroseconds;
            dir.Value = entry.Direction == CanFrameDirection.Transmitted ? 1 : 0;
            id.Value = frame.Id;
            flags.Value = (int)frame.ToRecordingFlags();
            length.Value = frame.IsRemote ? frame.RemoteLength : frame.Data.Length;

            data.Value = frame.IsRemote ? DBNull.Value : frame.Data.ToArray();
            raw.Value = (object?)entry.RawText ?? DBNull.Value;

            await frames.ExecuteNonQueryAsync(abort);
            written++;
        }

        if (changed > 0)
        {

            await using SqliteCommand total = connection.CreateCommand();
            total.Transaction = transaction;
            total.CommandText = "SELECT COUNT(*) FROM filter_change WHERE recording_id = $r;";
            total.Parameters.AddWithValue("$r", Recording.Id);

            Interlocked.Exchange(
                ref _filterChangeCount,
                Convert.ToInt64(await total.ExecuteScalarAsync(abort), CultureInfo.InvariantCulture));
        }

        await transaction.CommitAsync(abort);

        Interlocked.Add(ref _frameCount, written);
    }

    // Gets volume bytes.
    private long VolumeBytes()
    {
        long total = 0;

        foreach (string path in new[] { _volumePath, _volumePath + "-wal" })
        {
            try
            {
                var file = new FileInfo(path);

                if (file.Exists)
                {
                    total += file.Length;
                }
            }
            catch (IOException)
            {

            }
        }

        return total;
    }
}
