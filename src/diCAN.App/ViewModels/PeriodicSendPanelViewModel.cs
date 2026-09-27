using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.Core.Abstractions;
using DiCAN.Core.Protocol;
using DiCAN.Core.Sending;

namespace DiCAN.App.ViewModels;

// Manages transmit row.
public sealed partial class TransmitRowViewModel : ObservableObject
{

    // Initializes this instance.
    internal TransmitRowViewModel(PeriodicSendEntry entry, IReadOnlyList<LocalizedString> kindOptions)
    {
        Handle = entry.Handle;
        Frame = entry.Task.Frame;
        KindOptions = kindOptions;
        Update(entry);
    }

    internal const int KindClassic = 0;

    internal const int KindFd = 1;

    internal const int KindFdBrs = 2;

    internal const int KindRemote = 3;

    public IReadOnlyList<LocalizedString> KindOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteKind))]
    [NotifyPropertyChangedFor(nameof(ShowLength))]
    public partial int KindIndex { get; set; }

    public bool IsRemoteKind => KindIndex == KindRemote;

    public bool ShowLength => !IsRemoteKind;

    [ObservableProperty]
    public partial string RemoteLengthText { get; set; } = "8";

    // Gets reads as extended.
    internal static bool ReadsAsExtended(string idText)
    {
        string id = idText.Trim();

        if (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            id = id[2..];
        }

        return id.Length >= 8;
    }

    // Formats id.
    private static string FormatId(CanFrame frame) => frame.Id.ToString(
        frame.IsExtended ? "X8" : "X3", CultureInfo.InvariantCulture);

    // Checks recorded input.
    private static bool Says(string text, CanFrame frame) =>
        SendFrameInput.TryReadId(text, out int value)
        && value == frame.Id

        && ReadsAsExtended(text) == frame.IsExtended;

    // Checks recorded payload.
    private static bool SaysData(string text, CanFrame frame) =>

        string.Equals(
            text.Replace(" ", string.Empty, StringComparison.Ordinal).Trim(),
            Convert.ToHexString(frame.Data.Span),
            StringComparison.OrdinalIgnoreCase);

    // Checks the recorded number.
    private static bool SaysNumber(string text, int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typed)
        && typed == value;

    // Checks the repeat count.
    private static bool SaysRepeat(string text, int? total) =>
        total is { } count
            ? SaysNumber(text, count)
            : text.Trim().Length == 0 || text.Trim() == Unlimited;

    internal const string Unlimited = "∞";

    // Formats type.
    private static string FormatType(CanFrame frame) => frame switch
    {
        { IsRemote: true } => "RTR",
        { IsFd: true, IsBitRateSwitched: true } => "FD BRS",
        { IsFd: true } => "FD",
        _ => string.Empty,
    };

    // Gets kind of.
    internal static int KindOf(CanFrame frame) => frame switch
    {
        { IsRemote: true } => KindRemote,
        { IsFd: true, IsBitRateSwitched: true } => KindFdBrs,
        { IsFd: true } => KindFd,
        _ => KindClassic,
    };

    // Formats hexadecimal bytes.
    private static string Spaced(string packed)
    {
        if (packed.Length <= 2)
        {
            return packed;
        }

        var text = new StringBuilder(packed.Length + (packed.Length / 2));

        for (int i = 0; i < packed.Length; i += 2)
        {
            if (i > 0)
            {
                text.Append(' ');
            }

            text.Append(packed[i]).Append(packed[i + 1]);
        }

        return text.ToString();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasName))]
    public partial string Name { get; set; } = string.Empty;

    public bool HasName => !string.IsNullOrWhiteSpace(Name);

    [ObservableProperty]
    public partial string IdText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DataText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PeriodText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepeatText { get; set; } = Unlimited;

    public string DataSpaced => Spaced(Convert.ToHexString(Frame.Data.Span));

    public string TypeText => FormatType(Frame);

    public string LengthText =>
        (Frame.IsRemote ? Frame.RemoteLength : Frame.Data.Length)
            .ToString(CultureInfo.InvariantCulture);

    public CanFrame Frame { get; private set; }

    public int? Handle { get; private set; }

    public int IntervalMs { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentText))]
    [NotifyPropertyChangedFor(nameof(StateSymbol))]
    public partial int Sent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentText))]
    [NotifyPropertyChangedFor(nameof(StateSymbol))]
    public partial int? Remaining { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateSymbol))]
    public partial bool IsEnabled { get; set; } = true;

    internal bool Suppress { get; set; }

    internal bool EditedWhilePaused { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateSymbol))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentText))]
    internal partial int ManualSent { get; set; }

    public string SentText
    {
        get
        {

            string total = Remaining is { } left
                ? (Sent + left).ToString(CultureInfo.InvariantCulture)
                : Unlimited;

            string progress = string.Create(CultureInfo.InvariantCulture, $"{Sent}/{total}");

            return ManualSent > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{progress} (+{ManualSent})")
                : progress;
        }
    }

    public string StateSymbol =>
        IsEnabled ? RunningMark
        : !IsPaused ? NeverStartedMark

        : Remaining == 0 ? FinishedMark
        : PausedMark;

    internal const string NeverStartedMark = "·";

    internal const string RunningMark = "▶";

    internal const string PausedMark = "‖";

    internal const string FinishedMark = "✓";

    // Updates the current state.
    internal void Update(PeriodicSendEntry entry)
    {

        Sent = entry.Sent;
        Remaining = entry.Remaining;

        Suppress = true;

        IsEnabled = entry.Enabled;
        IsPaused = entry.IsPaused;
        Frame = entry.Task.Frame;
        IntervalMs = (int)entry.Task.Interval.TotalMilliseconds;

        if (!Says(IdText, entry.Task.Frame))
        {
            IdText = FormatId(entry.Task.Frame);
        }

        if (!entry.Task.Frame.IsRemote && !SaysData(DataText, entry.Task.Frame))
        {
            DataText = Spaced(Convert.ToHexString(entry.Task.Frame.Data.Span));
        }

        if (!SaysNumber(PeriodText, IntervalMs))
        {
            PeriodText = IntervalMs.ToString(CultureInfo.InvariantCulture);
        }

        if (!SaysRepeat(RepeatText, entry.Task.RepeatCount))
        {
            RepeatText = entry.Task.RepeatCount is { } total
                ? total.ToString(CultureInfo.InvariantCulture)
                : Unlimited;
        }

        KindIndex = KindOf(entry.Task.Frame);

        if (entry.Task.Frame.IsRemote)
        {
            RemoteLengthText = entry.Task.Frame.RemoteLength
                .ToString(CultureInfo.InvariantCulture);
        }

        Suppress = false;

        OnPropertyChanged(nameof(DataSpaced));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(LengthText));
    }
}

// Manages periodic send panel.
public sealed partial class PeriodicSendPanelViewModel : ObservableObject, IAsyncDisposable
{

    private static readonly TimeSpan HighResolutionBelow = TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan RepaintInterval = TimeSpan.FromMilliseconds(100);

    private readonly PeriodicSendRunner _runner;
    private readonly ILocalizationService _localization;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Action> _post;
    private readonly ITimerResolution _timerResolution;

    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly SemaphoreSlim _wake = new(0);

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IDisposable? _resolution;
    private DateTimeOffset _lastRepaint = DateTimeOffset.MinValue;

    private DateTimeOffset _lastFailureReport = DateTimeOffset.MinValue;

    private int _lastCount = -1;

    // Initializes this instance.
    public PeriodicSendPanelViewModel(
        PeriodicSendRunner runner,
        ILocalizationService localization,
        Func<DateTimeOffset>? now = null,
        Action<Action>? post = null,
        ITimerResolution? timerResolution = null)
    {
        _runner = runner;
        _localization = localization;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _timerResolution = timerResolution ?? NullTimerResolution.Instance;
    }

    public event EventHandler? Running;

    public event EventHandler? QueueEdited;

    public ObservableCollection<TransmitRowViewModel> Tasks { get; } = [];

    public bool CanTransmit => _runner.CanTransmit;

    [ObservableProperty]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    // Notifies state changes.
    public void NotifyError() => OnPropertyChanged(nameof(HasError));

    [ObservableProperty]
    public partial long TotalSent { get; set; }

    [ObservableProperty]
    public partial long TotalSkipped { get; set; }

    public bool HasSkipped => TotalSkipped > 0;

    public bool HasTasks => Tasks.Count > 0;

    public bool IsEmptyQueue => Tasks.Count == 0;

    public bool HasRunningTasks => Tasks.Any(t => t.IsEnabled);

    public bool HasPausedTasks => Tasks.Any(t => t.IsPaused);

    public string QueueSummary
    {
        get
        {
            int enabled = 0;
            double rate = 0;

            foreach (TransmitRowViewModel task in Tasks)
            {
                if (!task.IsEnabled)
                {
                    continue;
                }

                enabled++;

                if (task.IntervalMs > 0)
                {
                    rate += 1000.0 / task.IntervalMs;
                }
            }

            return _localization.Format(
                "Tx.QueueSummary", enabled, Tasks.Count, Math.Round(rate));
        }
    }

    public string Summary => _localization.Format("Send.TasksSummary", Tasks.Count, TotalSent);

    public IReadOnlyList<LocalizedString> KindOptions => _kindOptions ??=
    [
        _localization.GetBindable("Tx.Kind.Classic"),
        _localization.GetBindable("Tx.Kind.Fd"),
        _localization.GetBindable("Tx.Kind.FdBrs"),
        _localization.GetBindable("Tx.Kind.Remote"),
    ];

    private IReadOnlyList<LocalizedString>? _kindOptions;

    public string SkippedNote => _localization.Format("Send.SkippedNote", TotalSkipped);

    // Starts the operation.
    public void Start()
    {
        if (_loop is not null || !_runner.CanTransmit)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    // Stops the active operation.
    public async Task StopAsync()
    {
        if (_cts is { } cts)
        {
            await cts.CancelAsync();
        }

        if (_loop is { } loop)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }

            _loop = null;
        }

        _cts?.Dispose();
        _cts = null;

        _runner.Stop();
        ReleaseResolution();
    }

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _wake.Dispose();
    }

    // Adds row.
    public void AddRow(string id, string data, bool extended, int intervalMs, bool enabled)
    {
        Error = null;

        try
        {
            Schedule(SendFrameInput.Parse(id, data, extended), intervalMs, enabled);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {

            Error = _localization.Describe(ex);
        }

        OnPropertyChanged(nameof(HasError));
    }

    // Adds frame.
    public void AddFrame(CanFrame frame, int intervalMs, bool enabled)
    {
        Error = null;

        try
        {
            Schedule(frame, intervalMs, enabled);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Error = _localization.Describe(ex);
        }

        OnPropertyChanged(nameof(HasError));
    }

    // Schedules pending work.
    private void Schedule(CanFrame frame, int intervalMs, bool enabled)
    {
        if (intervalMs <= 0)
        {

            throw new ArgumentOutOfRangeException(
                nameof(intervalMs), _localization["Transmit.BadInterval"]);
        }

        var task = new PeriodicSendTask(frame, TimeSpan.FromMilliseconds(intervalMs));

        Enqueue(() => _runner.Schedule.Add(task, _now(), enabled));

        QueueEdited?.Invoke(this, EventArgs.Empty);
    }

    // Restores saved state.
    public void Restore(IReadOnlyList<TransmitQueueRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        foreach (TransmitQueueRow row in rows)
        {
            try
            {
                bool remote = row.KindIndex == TransmitRowViewModel.KindRemote;

                CanFrame frame = SendFrameInput.Parse(
                    row.Id,
                    remote ? string.Empty : row.Data,
                    TransmitRowViewModel.ReadsAsExtended(row.Id),
                    fd: row.KindIndex is TransmitRowViewModel.KindFd or TransmitRowViewModel.KindFdBrs,
                    bitRateSwitched: row.KindIndex == TransmitRowViewModel.KindFdBrs,
                    remote: remote,
                    remoteLengthText: row.RemoteLength);

                if (!int.TryParse(
                        row.Period.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int interval) || interval <= 0)
                {
                    continue;
                }

                int? repeat = null;

                if (!string.Equals(
                        row.Repeat.Trim(),
                        TransmitRowViewModel.Unlimited,
                        StringComparison.Ordinal) &&
                    int.TryParse(
                        row.Repeat.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int count) && count > 0)
                {
                    repeat = count;
                }

                var task = new PeriodicSendTask(
                    frame, TimeSpan.FromMilliseconds(interval), repeat);

                Enqueue(() => _runner.Schedule.Add(task, _now(), enabled: false));

                _restoredNames.Enqueue(row.Name);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {

            }
        }
    }

    private readonly Queue<string> _restoredNames = new();

    // Gets snapshot.
    public IReadOnlyList<TransmitQueueRow> Snapshot() =>
        [.. Tasks.Select((row, index) => new TransmitQueueRow(
            Position: index,
            Name: row.Name,
            KindIndex: row.KindIndex,
            Id: row.IdText,
            Data: row.DataText,
            Period: row.PeriodText,
            Repeat: row.RepeatText,
            RemoteLength: row.RemoteLengthText))];

    // Adds default row.
    [RelayCommand]
    private void AddDefaultRow() =>
        AddRow("100", "00 00 00 00 00 00 00 00", extended: false, 100, enabled: false);

    // Applies edit.
    private void ApplyEdit(TransmitRowViewModel row)
    {
        if (row.Suppress || row.Handle is not { } handle)
        {
            return;
        }

        if (row.IsEnabled)
        {

            Error = _localization["Tx.PauseToEdit"];
            OnPropertyChanged(nameof(HasError));

            return;
        }

        string periodText = row.PeriodText;
        string repeatText = row.RepeatText;

        Error = null;

        try
        {

            bool remote = row.KindIndex == TransmitRowViewModel.KindRemote;

            CanFrame frame = SendFrameInput.Parse(
                row.IdText,

                remote ? string.Empty : row.DataText,
                TransmitRowViewModel.ReadsAsExtended(row.IdText),
                fd: row.KindIndex is TransmitRowViewModel.KindFd or TransmitRowViewModel.KindFdBrs,
                bitRateSwitched: row.KindIndex == TransmitRowViewModel.KindFdBrs,
                remote: remote,
                remoteLengthText: row.RemoteLengthText);

            if (!int.TryParse(
                    periodText.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int ms)
                || ms <= 0)
            {
                throw new FormatException(_localization["Transmit.BadInterval"]);
            }

            int? repeat = ParseRepeat(repeatText);

            bool restartCount = repeat is { } total && total <= row.Sent;

            var task = new PeriodicSendTask(frame, TimeSpan.FromMilliseconds(ms), repeat);

            ApplyToSchedule(handle, task, restartCount);

            row.EditedWhilePaused = true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {

            Error = _localization.Describe(ex);
        }

        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(QueueSummary));
    }

    // Applies to schedule.
    private void ApplyToSchedule(int handle, PeriodicSendTask task, bool restartCount)
    {
        Enqueue(() =>
        {
            try
            {

                if (restartCount)
                {
                    _runner.Schedule.ResetCounter(handle);
                }

                _runner.Schedule.Update(handle, task, _now());
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {

                string message = _localization.Describe(ex);
                _post(() =>
                {
                    Error = message;
                    OnPropertyChanged(nameof(HasError));
                });
            }
        });
    }

    // Parses repeat.
    private static int? ParseRepeat(string text)
    {
        string value = text.Trim();

        if (value.Length == 0 || value == TransmitRowViewModel.Unlimited)
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            || count <= 0)
        {
            throw new FormatException(
                "A repeat count must be a positive whole number, or blank to repeat until stopped.");
        }

        return count;
    }

    // Removes the requested entry.
    [RelayCommand]
    private void Remove(TransmitRowViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        Error = null;
        OnPropertyChanged(nameof(HasError));

        if (task.Handle is { } handle)
        {
            Enqueue(() => _runner.Schedule.Remove(handle));
        }

        QueueEdited?.Invoke(this, EventArgs.Empty);
    }

    // Sends row.
    [RelayCommand]
    private void SendRow(TransmitRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Enqueue(() => _pending.Enqueue(row));
    }

    private readonly ConcurrentQueue<TransmitRowViewModel> _pending = new();

    // Resets counters.
    [RelayCommand]
    private void ResetCounters()
    {

        foreach (TransmitRowViewModel task in Tasks)
        {
            task.ManualSent = 0;
        }

        Enqueue(_runner.ResetCounters);
    }

    // Resets row counter.
    [RelayCommand]
    private void ResetRowCounter(TransmitRowViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        task.ManualSent = 0;

        if (task.Handle is { } handle)
        {
            Enqueue(() => _runner.Schedule.ResetCounter(handle));
        }
    }

    // Stops all.
    [RelayCommand]
    private void StopAll() => Enqueue(_runner.Stop);

    // Pauses active transmissions.
    [RelayCommand]
    private void PauseAll() => Enqueue(() => _runner.Schedule.PauseAll());

    // Resumes paused transmissions.
    [RelayCommand]
    private void ResumeAll()
    {

        List<int> restart = [];

        foreach (TransmitRowViewModel row in Tasks)
        {
            if (row.EditedWhilePaused && row.Handle is { } handle)
            {
                restart.Add(handle);
                row.EditedWhilePaused = false;
            }
        }

        Enqueue(() =>
        {
            foreach (int handle in restart)
            {
                _runner.Schedule.ResetCounter(handle);
            }

            _runner.Schedule.ResumeAll(_now());
        });
    }

    // Sets enabled.
    internal void SetEnabled(TransmitRowViewModel task, bool enabled)
    {

        if (task.Suppress || task.Handle is not { } handle)
        {
            return;
        }

        bool restart = enabled && task.EditedWhilePaused;

        if (restart)
        {
            task.EditedWhilePaused = false;
        }

        Enqueue(() =>
        {
            if (restart)
            {
                _runner.Schedule.ResetCounter(handle);
            }

            _runner.Schedule.SetEnabled(handle, enabled, _now());
        });
    }

    // Advances scheduled work.
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        while (_commands.TryDequeue(out Action? command))
        {
            command();
        }

        while (_pending.TryDequeue(out TransmitRowViewModel? manual))
        {
            Exception? failure = await _runner.SendOnceAsync(manual.Frame);

            if (failure is null)
            {
                _post(() =>
                {

                    manual.ManualSent++;
                    OnPropertyChanged(nameof(Summary));
                });

                continue;
            }

            string message = _localization.Describe(failure);

            _post(() =>
            {
                Error = message;
                OnPropertyChanged(nameof(HasError));
            });
        }

        PeriodicSendTickResult result = await _runner.TickAsync(_now(), cancellationToken);

        if (result.Failed.Count > 0 && _now() - _lastFailureReport >= RepaintInterval)
        {
            _lastFailureReport = _now();

            PeriodicSendFailure failure = result.Failed[0];

            string message = _localization.Format(
                "Send.TaskFailed", failure.Handle, _localization.Describe(failure.Error));

            _post(() =>
            {
                Error = message;
                OnPropertyChanged(nameof(HasError));
            });
        }

        Publish();
    }

    // Queues the requested data.
    private void Enqueue(Action command)
    {
        _commands.Enqueue(command);

        _wake.Release();
    }

    // Runs the requested operation.
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {

                if (_runner.Schedule.IsIdle && _commands.IsEmpty && _pending.IsEmpty)
                {
                    ReleaseResolution();

                    Publish(force: true);

                    await _wake.WaitAsync(token);
                    continue;
                }

                await TickAsync(token);

                TimeSpan shortest = Shortest();
                ApplyResolution(shortest);
                await Task.Delay(TickInterval(shortest), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ReleaseResolution();
        }
    }

    // Gets the shortest option.
    private TimeSpan Shortest()
    {
        TimeSpan shortest = TimeSpan.MaxValue;

        foreach (PeriodicSendEntry entry in _runner.Schedule.Entries)
        {
            if (entry.Task.Interval < shortest)
            {
                shortest = entry.Task.Interval;
            }
        }

        return shortest == TimeSpan.MaxValue ? HighResolutionBelow : shortest;
    }

    // Advances scheduled work.
    private static TimeSpan TickInterval(TimeSpan shortest)
    {
        double ms = shortest.TotalMilliseconds / 4;

        return TimeSpan.FromMilliseconds(Math.Clamp(ms, 1, 20));
    }

    // Applies resolution.
    private void ApplyResolution(TimeSpan shortest)
    {
        if (shortest < HighResolutionBelow)
        {
            _resolution ??= _timerResolution.Acquire();
        }
        else
        {
            ReleaseResolution();
        }
    }

    // Releases held resources.
    private void ReleaseResolution()
    {
        _resolution?.Dispose();
        _resolution = null;
    }

    // Publishes the current state.
    private void Publish(bool force = false)
    {
        IReadOnlyList<PeriodicSendEntry> entries = _runner.Schedule.Entries;
        DateTimeOffset now = _now();

        bool changed = entries.Count != _lastCount;

        if (!force && !changed && now - _lastRepaint < RepaintInterval)
        {
            return;
        }

        _lastCount = entries.Count;
        _lastRepaint = now;

        long sent = _runner.TotalSent;
        long skipped = _runner.TotalSkipped;

        _post(() => Apply(entries, sent, skipped));
    }

    // Applies the requested changes.
    private void Apply(IReadOnlyList<PeriodicSendEntry> entries, long sent, long skipped)
    {
        TotalSent = sent;
        TotalSkipped = skipped;

        foreach (PeriodicSendEntry entry in entries)
        {
            if (Tasks.FirstOrDefault(t => t.Handle == entry.Handle) is { } existing)
            {
                existing.Update(entry);
            }
            else
            {

                var created = new TransmitRowViewModel(entry, KindOptions);

                if (_restoredNames.Count > 0)
                {
                    created.Name = _restoredNames.Dequeue();
                }

                Tasks.Add(Attach(created));
            }
        }

        for (int i = Tasks.Count - 1; i >= 0; i--)
        {
            if (Tasks[i].Handle is { } handle && !entries.Any(e => e.Handle == handle))
            {
                Tasks.RemoveAt(i);
            }
        }

        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(IsEmptyQueue));
        OnPropertyChanged(nameof(HasRunningTasks));
        OnPropertyChanged(nameof(HasPausedTasks));
        OnPropertyChanged(nameof(Summary));

        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(SkippedNote));

        Running?.Invoke(this, EventArgs.Empty);
    }

    // Attaches the active session.
    private TransmitRowViewModel Attach(TransmitRowViewModel task)
    {
        task.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TransmitRowViewModel.IsEnabled))
            {
                SetEnabled(task, task.IsEnabled);

                OnPropertyChanged(nameof(QueueSummary));

                return;
            }

            if (e.PropertyName is nameof(TransmitRowViewModel.IdText)
                or nameof(TransmitRowViewModel.DataText)
                or nameof(TransmitRowViewModel.PeriodText)
                or nameof(TransmitRowViewModel.RepeatText)
                or nameof(TransmitRowViewModel.KindIndex)
                or nameof(TransmitRowViewModel.RemoteLengthText))
            {

                ApplyEdit(task);
                QueueEdited?.Invoke(this, EventArgs.Empty);

                return;
            }

            if (e.PropertyName == nameof(TransmitRowViewModel.Name))
            {
                QueueEdited?.Invoke(this, EventArgs.Empty);
            }
        };

        return task;
    }
}
