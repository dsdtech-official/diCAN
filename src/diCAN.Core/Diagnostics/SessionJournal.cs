namespace DiCAN.Core.Diagnostics;

// Manages session event.
public enum SessionEventLevel
{

    Info,

    Warning,

    Error,
}

// Stores session event data.
public readonly record struct SessionEvent(
    TimeSpan At,
    SessionEventLevel Level,
    string Message,
    int Count = 1,
    string? CollapseKey = null)
{

    public string Key => CollapseKey ?? Message;
}

// Manages session journal.
public sealed class SessionJournal
{

    public const int Capacity = 500;

    private readonly List<SessionEvent> _entries = new(64);
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset _origin;

    // Initializes this instance.
    public SessionJournal(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
        _origin = _clock();
    }

    public event EventHandler<SessionEvent>? Recorded;

    public event EventHandler? Changed;

    public IReadOnlyList<SessionEvent> Entries => _entries;

    public bool Truncated { get; private set; }

    // Restarts the active operation.
    public void Restart() => _origin = _clock();

    // Adds the requested entry.
    public void Add(SessionEventLevel level, string message, string? collapseKey = null)
    {
        AddCore(level, message, collapseKey, _clock());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Adds range.
    public void AddRange(IReadOnlyList<JournalWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);

        if (writes.Count == 0)
        {
            return;
        }

        foreach (JournalWrite write in writes)
        {
            AddCore(write.Level, write.Message, write.CollapseKey, write.At);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Adds core.
    private void AddCore(SessionEventLevel level, string message, string? collapseKey, DateTimeOffset at)
    {
        var entry = new SessionEvent(at - _origin, level, message, CollapseKey: collapseKey);

        if (_entries.Count > 0)
        {
            SessionEvent last = _entries[^1];

            if (last.Level == entry.Level && string.Equals(last.Key, entry.Key, StringComparison.Ordinal))
            {

                _entries[^1] = last with { Message = message, Count = last.Count + 1 };

                Recorded?.Invoke(this, _entries[^1]);
                return;
            }
        }

        _entries.Add(entry);

        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(0, _entries.Count - Capacity);
            Truncated = true;
        }

        Recorded?.Invoke(this, entry);
    }

    // Reports status information.
    public void Info(string message) => Add(SessionEventLevel.Info, message);

    // Reports a warning.
    public void Warning(string message) => Add(SessionEventLevel.Warning, message);

    // Reports an error.
    public void Error(string message) => Add(SessionEventLevel.Error, message);

    // Clears the stored state.
    public void Clear()
    {
        _entries.Clear();
        Truncated = false;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

// Stores journal write data.
public readonly record struct JournalWrite(
    SessionEventLevel Level,
    string Message,
    string? CollapseKey,
    DateTimeOffset At);
