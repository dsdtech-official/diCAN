using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.App.ViewModels.Dialogs;
using DiCAN.Core.Abstractions;
using DiCAN.Core.Aggregation;
using DiCAN.Core.Devices;
using DiCAN.Core.Diagnostics;
using DiCAN.Core.Filtering;
using DiCAN.Core.History;
using DiCAN.Core.Protocol;
using DiCAN.Core.Sending;
using DiCAN.Core.Recordings;
using DiCAN.Core.Settings;
using DiCAN.Core.Streaming;
using DiCAN.Core.Transport;
using DiCAN.App.Composition;
using DiCAN.Infrastructure.Time;

namespace DiCAN.App.ViewModels;

// Manages main window.
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly ITimerResolution _timerResolution;
    private readonly IDeviceRegistry? _registry;
    private readonly CanIdAggregator _aggregator = new();

    private readonly IMonotonicClock _clock = new MonotonicClock();

    private readonly CanFrameLog _log;

    internal CanFrameLog FrameRing => _log;

    internal CanIdAggregator IdAggregator => _aggregator;

    private ICanTransport? _transport;
    private DispatcherTimer? _refresh;

    private readonly TransportReportBuffer _reports = new();

    private DispatcherTimer? _reportPump;

    private DateTimeOffset _origin;

    private long _drawnSequence = -1;

    private DateTimeOffset? _previousStreamTime;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    // Initializes this instance.
    public MainWindowViewModel(
        ILocalizationService localization,
        ITimerResolution? timerResolution = null,
        IDeviceRegistry? registry = null,
        IRecordingStore? recordings = null,
        IHistoryStore? history = null,
        ISettingsStore? settings = null,
        AppOptions? options = null,
        ITransmitQueueStore? transmitQueue = null,
        Action<Action>? periodicPost = null)
    {
        _transmitQueue = transmitQueue;
        _periodicPost = periodicPost;

        _options = (options ?? AppOptions.Default).Clamped();
        _log = new CanFrameLog(_options.StreamCapacity);

        _localization = localization;
        _timerResolution = timerResolution ?? NullTimerResolution.Instance;
        _registry = registry;
        _recordings = recordings;
        _history = history;
        _settings = settings;

        Display.SwitchChanged += (_, change) => RememberSwitch(change.Setting, change.Value);
        Display.FontSizeChanged += (_, size) => RememberFontSize(size);
        SessionTypes = SessionTypeViewModel.All(localization);

        Languages =
        [
            .. localization.AvailableLanguages.Select(
                l => new LanguageMenuItemViewModel(localization, l)),
        ];
        Log = new SessionLogViewModel(Journal, localization);

        Journal.Recorded += OnJournalRecorded;

        _localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IdCountText));
            OnPropertyChanged(nameof(ListenOnlyNote));
            OnPropertyChanged(nameof(NotConfirmedWarning));
            OnPropertyChanged(nameof(FilterLabel));
            OnPropertyChanged(nameof(FilterHint));

            OnPropertyChanged(nameof(LanguageMenuHeader));

            OnPropertyChanged(nameof(RecordLabel));

            OnPropertyChanged(nameof(ConnectionLabel));

            if (_session is { } session)
            {
                SessionTitle = SessionLine(session, _sessionNote);
            }
        };

        Display.Changed += (_, _) =>
        {

            RaiseDetailChanged();

            if (!IsConnected)
            {
                return;
            }

            PullSnapshot();
            RebuildStream();
        };
    }

    public Func<SessionTypeViewModel, Task<NewSessionResult?>>? RequestNewSession { get; set; }

    public string WindowTitle { get; init; } = "diCAN";

    public IReadOnlyList<SessionTypeViewModel> SessionTypes { get; }

    public IReadOnlyList<LanguageMenuItemViewModel> Languages { get; }

    public string LanguageMenuHeader =>
        _localization.Format("Menu.Language", CurrentLanguageName());

    // Gets current language name.
    private string CurrentLanguageName()
    {
        foreach (LanguageOption each in _localization.AvailableLanguages)
        {
            if (string.Equals(
                    each.CultureName,
                    _localization.CurrentCulture.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return each.NativeName;
            }
        }

        return _localization.AvailableLanguages[0].NativeName;
    }

    public ObservableCollection<CanIdRowViewModel> Rows { get; } = [];

    public ObservableCollection<FrameStreamRowViewModel> StreamRows { get; } = [];

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    public SessionJournal Journal { get; } = new();

    // Describes the requested value.
    internal static string DescribeSessionOpened(NewSessionResult result) =>
        $"Session opened: {result.Title}"

        + (result.Device.Kind == CanDeviceKind.Candlelight ? string.Empty : $", {result.Generation}")
        + (result.DataBitrate is { } fd ? $", data {fd} bit/s" : string.Empty)
        + $", {(result.IsListenOnly ? "listen only" : "normal")}";

    public SessionLogViewModel Log { get; }

    [ObservableProperty]
    public partial PeriodicSendPanelViewModel? Periodic { get; set; }

    public bool IsTransmittingPeriodically => Periodic is { HasRunningTasks: true };

    // Handles a state change.
    private void OnPeriodicRunningChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(IsTransmittingPeriodically));

    // Adds to transmit list.
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void AddToTransmitList()
    {
        if (Periodic is not { } panel)
        {
            return;
        }

        int? interval = int.TryParse(
            SendIntervalMs.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : null;

        if (interval is null or <= 0)
        {
            panel.Error = _localization["Transmit.BadInterval"];
            panel.NotifyError();
            return;
        }

        panel.AddRow(SendId, SendData, SendExtended, interval.Value, enabled: false);
    }

    public bool HasTransmitQueue => Periodic is not null;

    [ObservableProperty]
    public partial TransmitRowViewModel? SelectedTransmitRow { get; set; }

    public bool HasSelectedTransmitRow => SelectedTransmitRow is not null;

    // Handles a state change.
    partial void OnSelectedTransmitRowChanged(TransmitRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedTransmitRow));
        DuplicateTransmitRowCommand.NotifyCanExecuteChanged();
        RemoveTransmitRowCommand.NotifyCanExecuteChanged();
    }

    // Handles periodic changed.
    partial void OnPeriodicChanged(PeriodicSendPanelViewModel? value)
    {
        OnPropertyChanged(nameof(HasTransmitQueue));

        SelectedTransmitRow = null;

        SendSelectedRowToQueueCommand.NotifyCanExecuteChanged();
    }

    // Copies a transmit queue row.
    [RelayCommand(CanExecute = nameof(HasSelectedTransmitRow))]
    private void DuplicateTransmitRow()
    {
        if (Periodic is not { } panel || SelectedTransmitRow is not { } row)
        {
            return;
        }

        panel.AddRow(row.IdText, row.DataText, row.Frame.IsExtended, row.IntervalMs, enabled: false);
    }

    // Removes transmit row.
    [RelayCommand(CanExecute = nameof(HasSelectedTransmitRow))]
    private void RemoveTransmitRow()
    {
        if (Periodic is { } panel && SelectedTransmitRow is { } row)
        {
            panel.RemoveCommand.Execute(row);
            SelectedTransmitRow = null;
        }
    }

    // Stops periodic.
    [RelayCommand]
    private void StopPeriodic()
    {
        if (Periodic is { } panel)
        {
            panel.StopAllCommand.Execute(null);
            Journal.Info("Periodic sending stopped.");
        }
    }

    public DisplaySettingsViewModel Display { get; } = new();

    [ObservableProperty]
    public partial CanIdRowViewModel? SelectedRow { get; set; }

    [ObservableProperty]
    public partial FrameStreamRowViewModel? SelectedStreamRow { get; set; }

    public string? DetailId => SelectedTab == 1
        ? SelectedStreamRow is { IsGap: false } stream ? stream.Id : null
        : SelectedRow?.Id;

    public string? DetailData => SelectedTab == 1
        ? SelectedStreamRow is { IsGap: false } stream ? stream.FullData : null
        : SelectedRow?.FullData;

    public bool HasDetail => DetailId is { Length: > 0 };

    public string DetailIdLabel => _localization["Grid.Id"];

    public string DetailDataLabel => _localization["Grid.Data"];

    public string DetailDirLabel => _localization["Grid.Dir"];

    public string DetailLenLabel => _localization["Grid.Len"];

    public string DetailTypeLabel => _localization["Grid.Type"];

    public string DetailPeriodLabel => _localization["Grid.Period"];

    public string DetailCountLabel => _localization["Grid.Count"];

    public string DetailTimeLabel => _localization["Grid.Time"];

    public string DetailAddressingLabel => _localization["Detail.Addressing"];

    public string? DetailAddressing => Selected() is not { } row
        ? null
        : _localization[row.Extended ? "Detail.Extended" : "Detail.Standard"];

    public string? DetailDirection => Selected()?.Direction;

    public string? DetailLength => Selected()?.Length;

    public string? DetailType => Selected()?.Type;

    public bool HasDetailType => DetailType is { Length: > 0 };

    public bool DetailShowsPeriod => SelectedTab == 0 && HasDetail;

    public bool DetailShowsTime => SelectedTab == 1 && HasDetail;

    public string? DetailPeriod => SelectedRow?.Period;

    public string? DetailCount =>
        SelectedRow?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? DetailTime => SelectedStreamRow is { IsGap: false } row ? row.Time : null;

    public string? DetailDataSpaced => DetailHex() is { Length: > 0 } hex
        ? CanIdRowViewModel.Spaced(hex, Display.SpacedData)
        : null;

    // Copies detail payload text.
    public string? CopyDetailPayloadText() => DetailHex() is { Length: > 0 } hex
        ? CanIdRowViewModel.Spaced(hex, spaced: true)
        : null;

    // Copies selected id text.
    public string? CopySelectedIdText() => SelectedRow?.IdHex;

    // Copies selected data text.
    public string? CopySelectedDataText() => SelectedRow is { } row && row.FullDataHex.Length > 0
        ? CanIdRowViewModel.Spaced(row.FullDataHex, spaced: true)
        : null;

    private const int QueueIntervalMs = 100;

    // Sends selected row to queue.
    [RelayCommand(CanExecute = nameof(CanSendSelectedRowToQueue))]
    private void SendSelectedRowToQueue()
    {
        if (Periodic is not { } panel || SelectedRow is not { } row)
        {
            return;
        }

        panel.AddFrame(row.ToFrame(), QueueIntervalMs, enabled: false);
    }

    // Checks operation support.
    private bool CanSendSelectedRowToQueue() => Periodic is not null && SelectedRow is not null;

    // Gets the current selection.
    private (bool Extended, string Direction, string Length, string Type)? Selected() => SelectedTab == 1
        ? SelectedStreamRow is { IsGap: false } s
            ? (s.IsExtended, s.Direction, s.Length, s.Type)
            : null
        : SelectedRow is { } a
            ? (a.IsExtended, a.Direction, a.Length, a.Type)
            : null;

    // Gets detail hex.
    private string? DetailHex() => SelectedTab == 1
        ? SelectedStreamRow is { IsGap: false } s ? s.FullDataHex : null
        : SelectedRow?.FullDataHex;

    public bool HasSelectedAggregateRow => SelectedRow is not null;

    // Handles selected row changed.
    partial void OnSelectedRowChanged(CanIdRowViewModel? value)
    {
        RaiseDetailChanged();
        OnPropertyChanged(nameof(HasSelectedAggregateRow));
        SendSelectedRowToQueueCommand.NotifyCanExecuteChanged();
    }

    // Handles a state change.
    partial void OnSelectedStreamRowChanged(FrameStreamRowViewModel? value) => RaiseDetailChanged();

    // Raises the requested event.
    private void RaiseDetailChanged()
    {
        OnPropertyChanged(nameof(DetailId));
        OnPropertyChanged(nameof(DetailData));
        OnPropertyChanged(nameof(HasDetail));

        OnPropertyChanged(nameof(DetailAddressing));
        OnPropertyChanged(nameof(DetailDirection));
        OnPropertyChanged(nameof(DetailLength));
        OnPropertyChanged(nameof(DetailType));
        OnPropertyChanged(nameof(HasDetailType));
        OnPropertyChanged(nameof(DetailShowsPeriod));
        OnPropertyChanged(nameof(DetailShowsTime));
        OnPropertyChanged(nameof(DetailPeriod));
        OnPropertyChanged(nameof(DetailCount));
        OnPropertyChanged(nameof(DetailTime));
        OnPropertyChanged(nameof(DetailDataSpaced));
    }

    public bool IsAggregateTab => SelectedTab == 0;

    public bool IsStreamTab => SelectedTab == 1;

    // Selects the requested view.
    [RelayCommand]
    private void SelectView(string? index)
    {
        SelectedTab = index == "1" ? 1 : 0;

        RaiseViewChanged();
    }

    // Raises the requested event.
    private void RaiseViewChanged()
    {
        OnPropertyChanged(nameof(IsAggregateTab));
        OnPropertyChanged(nameof(IsStreamTab));
    }

    // Handles selected tab changed.
    partial void OnSelectedTabChanged(int value)
    {
        RaiseDetailChanged();
        RaiseViewChanged();

        if (value == 1 && IsConnected)
        {
            SyncStream();
        }
    }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool HasSession { get; set; }

    public bool IsEmpty => !HasSession;

    // Handles has session changed.
    partial void OnHasSessionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));

        ToggleConnectionCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    public partial string? SessionTitle { get; set; }

    // Handles session title changed.
    partial void OnSessionTitleChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    [ObservableProperty]
    public partial string? TransportMessage { get; set; }

    [ObservableProperty]
    public partial bool ConfigurationConfirmed { get; set; }

    [ObservableProperty]
    public partial bool IsListenOnly { get; set; }

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    // Handles is paused changed.
    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    [ObservableProperty]
    public partial long TotalFrames { get; set; }

    public string StatusText => IsReconnecting
        ? _localization.Format("Reconnect.Trying", ReconnectAttempt, _options.ReconnectAttempts)
        : TransportMessage ?? (
        !HasSession ? _localization["Status.NoSession"]
        : IsConnected && IsPaused ? _localization.Format("Status.Paused", SessionTitle ?? string.Empty)
        : IsConnected ? SessionTitle ?? string.Empty
        : _localization.Format("Status.Disconnected", SessionTitle ?? string.Empty));

    public string SummaryText => $"{IdCountText} · {RateText}";

    public string RateText => _localization.Format(
        "Status.Rate", _framesPerSecond, _busLoadPercent);

    private double _framesPerSecond;
    private double _busLoadPercent;

    private long _wireNanoseconds;

    private int _nominalBitsPerSecond;
    private int? _dataBitsPerSecond;

    private readonly Queue<(long Timestamp, long Frames, long WireNanoseconds)> _rateSamples = new();

    // Creates sample display data.
    private void SampleRate()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long wire = Interlocked.Read(ref _wireNanoseconds);

        _rateSamples.Enqueue((now, TotalFrames, wire));

        while (_rateSamples.Count > 1
            && (now - _rateSamples.Peek().Timestamp) > System.Diagnostics.Stopwatch.Frequency)
        {
            _rateSamples.Dequeue();
        }

        (long Timestamp, long Frames, long WireNanoseconds) oldest = _rateSamples.Peek();
        double seconds = (now - oldest.Timestamp) / (double)System.Diagnostics.Stopwatch.Frequency;

        if (seconds <= 0)
        {
            return;
        }

        _framesPerSecond = (TotalFrames - oldest.Frames) / seconds;
        _busLoadPercent = (wire - oldest.WireNanoseconds) / 1e9 / seconds * 100;

        OnPropertyChanged(nameof(RateText));
    }

    public string IdCountText => Filter.IsPassAll
        ? _localization.Format("Filter.AllIds", Rows.Count)
        : _localization.Format("Filter.ShownOfTotal", Rows.Count, _aggregator.RowCount);

    [ObservableProperty]
    public partial bool IsSidebarOpen { get; set; } = true;

    // Toggles the selected state.
    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [ObservableProperty]
    public partial int SelectedBottomTab { get; set; }

    [ObservableProperty]
    public partial bool IsBottomPanelOpen { get; set; } = true;

    // Toggles the selected state.
    [RelayCommand]
    private void ToggleBottomPanel() => IsBottomPanelOpen = !IsBottomPanelOpen;

    // Selects the requested view.
    [RelayCommand]
    private void SelectBottomTab(string? index)
    {

        SelectedBottomTab = int.TryParse(index, out int page) ? page : 0;
        IsBottomPanelOpen = true;

        RaiseBottomPanelChanged();

        if (ShowLogPage)
        {
            HasUnreadProblems = false;
        }
    }

    public bool ShowSendTab => IsConnected && !IsListenOnly;

    public bool ShowLogTab => true;

    [ObservableProperty]
    public partial bool HasUnreadProblems { get; set; }

    // Handles journal recorded.
    private void OnJournalRecorded(object? sender, SessionEvent e)
    {
        if (e.Level == SessionEventLevel.Info)
        {
            return;
        }

        if (!ShowLogPage)
        {
            HasUnreadProblems = true;
        }
    }

    public bool IsDetailTab => SelectedBottomTab == 0;

    public bool IsSendTab => SelectedBottomTab == 1 && ShowSendTab;

    public bool IsLogTab => SelectedBottomTab == 2 && ShowLogTab;

    public bool ShowDetailPage => IsBottomPanelOpen && IsDetailTab;

    public bool ShowSendPage => IsBottomPanelOpen && IsSendTab;

    public bool ShowLogPage => IsBottomPanelOpen && IsLogTab;

    // Raises the requested event.
    private void RaiseBottomPanelChanged()
    {
        OnPropertyChanged(nameof(ShowSendTab));
        OnPropertyChanged(nameof(ShowLogTab));
        OnPropertyChanged(nameof(IsDetailTab));
        OnPropertyChanged(nameof(IsSendTab));
        OnPropertyChanged(nameof(IsLogTab));
        OnPropertyChanged(nameof(ShowDetailPage));
        OnPropertyChanged(nameof(ShowSendPage));
        OnPropertyChanged(nameof(ShowLogPage));
    }

    // Selects a fallback view.
    private void FallBackToDetailIfPageIsGone()
    {

        bool gone = (SelectedBottomTab == 1 && !ShowSendTab)
                 || (SelectedBottomTab == 2 && !ShowLogTab)
                 || SelectedBottomTab is < 0 or > 2;

        if (gone)
        {
            SelectedBottomTab = 0;
        }
    }

    // Handles a state change.
    partial void OnSelectedBottomTabChanged(int value)
    {
        FallBackToDetailIfPageIsGone();
        RaiseBottomPanelChanged();
    }

    // Handles a state change.
    partial void OnIsBottomPanelOpenChanged(bool value) => RaiseBottomPanelChanged();

    // Handles a state change.
    partial void OnIsListenOnlyChanged(bool value)
    {
        FallBackToDetailIfPageIsGone();
        RaiseBottomPanelChanged();
    }

    public string ListenOnlyNote => _localization["Send.ListenOnlyNote"];

    public string NotConfirmedWarning => _localization["Warn.NotConfirmed"];

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    public CanFilterSet Filter { get; private set; } = CanFilterSet.PassAll;

    [ObservableProperty]
    public partial string? FilterError { get; set; }

    public bool HasFilterError => FilterError is not null;

    public bool IsFiltering => !Filter.IsPassAll;

    public string FilterLabel => _localization["Filter.Label"];

    public string FilterHint => _localization["Filter.Hint"];

    // Handles filter text changed.
    partial void OnFilterTextChanged(string value)
    {
        if (CanFilterSet.TryParse(value, out CanFilterSet parsed, out CanFilterError? error))
        {
            Filter = parsed;
            FilterError = null;
        }
        else
        {

            FilterError = _localization.Describe(error!, error!.Message);
        }

        OnPropertyChanged(nameof(HasFilterError));
        OnPropertyChanged(nameof(IsFiltering));

        if (IsConnected)
        {
            PullSnapshot();

            RebuildStream();
        }
    }

    // Clears filter.
    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    public ObservableCollection<string> SendHistory { get; } = [];

    public ObservableCollection<string> FilterHistory { get; } = [];

    public ObservableCollection<HistoryItemViewModel> FilterHistoryRows { get; } = [];

    public Func<int, Task<bool>>? RequestClearHistoryConfirmation { get; set; }

    private string? _lastRememberedFilter;

    private const int HistoryVisibleLimit = IHistoryStore.VisibleLimit;

    private readonly IHistoryStore? _history;

    private readonly ISettingsStore? _settings;

    private readonly AppOptions _options;

    private readonly ITransmitQueueStore? _transmitQueue;

    private readonly Action<Action>? _periodicPost;

    // Saves the current choice.
    private static void Remember(ObservableCollection<string> history, string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        history.Remove(entry);
        history.Insert(0, entry);

        while (history.Count > HistoryVisibleLimit)
        {
            history.RemoveAt(history.Count - 1);
        }
    }

    // Applies a saved history entry.
    [RelayCommand]
    private void UseSendHistory(string? entry)
    {

        int split = entry?.IndexOf(' ') ?? -1;

        if (entry is null || split < 0)
        {
            return;
        }

        SendId = entry[..split];
        SendData = entry[(split + 1)..];
    }

    // Applies a saved history entry.
    [RelayCommand]
    private void UseFilterHistory(string? entry)
    {
        if (entry is not null)
        {
            FilterText = entry;
        }
    }

    // Refreshes filter history rows.
    private void RefreshFilterHistoryRows()
    {
        FilterHistoryRows.Clear();

        foreach (string entry in FilterHistory)
        {
            FilterHistoryRows.Add(HistoryItemViewModel.Entry(entry, ForgetFilterHistoryAsync));
        }

        if (FilterHistory.Count > 0)
        {
            FilterHistoryRows.Add(HistoryItemViewModel.ClearAll(ClearFilterHistoryAsync));
        }
    }

    // Removes a saved history entry.
    private async Task ForgetFilterHistoryAsync(string entry)
    {
        if (_history is { } store)
        {
            try
            {
                await store.ForgetAsync(
                    new HistoryEntry(HistoryKind.Filter, entry, null, null, DateTimeOffset.Now, 0));
            }
            catch (Exception ex)
            {

                Journal.Warning($"A filter history entry could not be deleted: {ex.Message}");
                return;
            }
        }

        FilterHistory.Remove(entry);

        _lastRememberedFilter = null;

        RefreshFilterHistoryRows();
    }

    // Clears filter history.
    private async Task ClearFilterHistoryAsync()
    {
        if (_history is not { } store)
        {

            FilterHistory.Clear();
            _lastRememberedFilter = null;
            RefreshFilterHistoryRows();
            return;
        }

        int stored;

        try
        {
            stored = await store.CountAsync(HistoryKind.Filter);
        }
        catch (Exception ex)
        {

            Journal.Warning($"The filter history could not be counted: {ex.Message}");
            return;
        }

        if (RequestClearHistoryConfirmation is not { } ask || !await ask(stored))
        {
            return;
        }

        try
        {
            await store.ClearAsync(HistoryKind.Filter);
        }
        catch (Exception ex)
        {

            TransportMessage = _localization["History.ClearAll.Failed"];
            OnPropertyChanged(nameof(StatusText));
            Journal.Error($"The filter history could not be cleared: {ex.Message}");
            return;
        }

        _lastRememberedFilter = null;

        await ReloadFilterHistoryAsync();
    }

    // Reloads saved data.
    private async Task ReloadFilterHistoryAsync()
    {
        if (_history is not { } store)
        {
            return;
        }

        try
        {
            IReadOnlyList<HistoryEntry> entries =
                await store.ListAsync(HistoryKind.Filter, IHistoryStore.VisibleLimit);

            FilterHistory.Clear();

            foreach (HistoryEntry entry in entries)
            {
                FilterHistory.Add(entry.Text);
            }
        }
        catch (Exception ex)
        {
            Journal.Warning($"The filter history could not be re-read: {ex.Message}");
        }

        RefreshFilterHistoryRows();
    }

    // Commits the requested changes.
    [RelayCommand]
    private void CommitFilter()
    {
        if (HasFilterError || !IsFiltering)
        {
            return;
        }

        string expression = FilterText.Trim();

        if (string.Equals(expression, _lastRememberedFilter, StringComparison.Ordinal))
        {
            return;
        }

        _lastRememberedFilter = expression;

        Remember(FilterHistory, expression);

        RefreshFilterHistoryRows();

        RememberOnDisk(HistoryKind.Filter, expression);
        LogFilterChange();
    }

    // Saves the current choice.
    private void RememberSwitch(BoolSetting setting, bool value)
    {
        if (_settings is not { } store)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await store.SetAsync(setting, value);
            }
            catch (Exception ex)
            {
                Journal.Warning($"The setting {setting.Name} could not be saved: {ex.Message}");
            }
        });
    }

    // Saves the current choice.
    private void RememberFontSize(double size)
    {
        if (_settings is not { } store)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await store.SaveTextAsync(
                    DisplaySettingKeys.FontSize.Name,
                    size.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Journal.Warning($"The table font size could not be saved: {ex.Message}");
            }
        });
    }

    // Saves transmit queue.
    internal async Task SaveTransmitQueueAsync()
    {
        if (_transmitQueue is not { } queue || Periodic is not { } panel)
        {
            return;
        }

        try
        {
            await queue.SaveAsync(panel.Snapshot());
        }
        catch (Exception ex)
        {
            Journal.Warning($"The transmit queue could not be saved: {ex.Message}");
        }
    }

    private static readonly TimeSpan QueueSaveQuietWindow = TimeSpan.FromSeconds(1);

    private CancellationTokenSource? _queueSaveDelay;

    // Handles transmit queue edited.
    private void OnTransmitQueueEdited(object? sender, EventArgs e)
    {
        CancelPendingQueueSave();

        var pending = new CancellationTokenSource();
        _queueSaveDelay = pending;

        _ = SaveTransmitQueueAfterQuietWindowAsync(pending.Token);
    }

    // Saves application data.
    private async Task SaveTransmitQueueAfterQuietWindowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(QueueSaveQuietWindow, cancellationToken);
        }
        catch (OperationCanceledException)
        {

            return;
        }

        await SaveTransmitQueueAsync();
    }

    // Cancels pending queue save.
    private void CancelPendingQueueSave()
    {
        if (_queueSaveDelay is not { } pending)
        {
            return;
        }

        _queueSaveDelay = null;
        pending.Cancel();
        pending.Dispose();
    }

    // Saves the current choice.
    private void RememberOnDisk(HistoryKind kind, string text)
    {
        if (_history is not { } store || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await store.RememberAsync(
                    new HistoryEntry(kind, text, null, null, DateTimeOffset.Now, 0));
            }
            catch (Exception ex)
            {
                Journal.Warning($"The {kind} history could not be saved: {ex.Message}");
            }
        });
    }

    // Loads history.
    public async Task LoadHistoryAsync()
    {
        if (_history is { } store)
        {

            await FillAsync(HistoryKind.Filter, FilterHistory, "filter");
            await FillAsync(HistoryKind.SendFrame, SendHistory, "send");

            RefreshFilterHistoryRows();
        }

        await ApplyStoredDisplaySettingsAsync();

        // Fills the requested list.
        async Task FillAsync(HistoryKind kind, ObservableCollection<string> target, string label)
        {
            try
            {
                IReadOnlyList<HistoryEntry> entries =
                    await store!.ListAsync(kind, IHistoryStore.VisibleLimit);

                target.Clear();

                foreach (HistoryEntry entry in entries)
                {
                    target.Add(entry.Text);
                }
            }
            catch (Exception ex)
            {

                Journal.Warning($"The {label} history could not be read: {ex.Message}");
            }
        }
    }

    // Applies the requested changes.
    private async Task ApplyStoredDisplaySettingsAsync()
    {
        if (_settings is not { } store)
        {
            return;
        }

        try
        {
            Display.Apply(await store.LoadAsync());
        }
        catch (Exception ex)
        {

            Journal.Warning($"The display settings could not be read: {ex.Message}");
        }
    }

    // Logs filter change.
    private void LogFilterChange()
    {
        if (_recordingScope == RecordingScope.FilteredOnly)
        {
            _recordingSession?.FilterChanged(FilterText?.Trim() ?? string.Empty);
        }
    }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    public string RecordLabel => _localization[IsRecording ? "Toolbar.StopRecord" : "Toolbar.Record"];

    public bool IsRecordingWired => _recordings is not null;

    public bool ShowRecordingNotWired => IsRecording && !IsRecordingWired;

    public bool ShowRecordingIndicator => IsRecording && IsRecordingWired;

    // Handles is recording changed.
    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordLabel));
        OnPropertyChanged(nameof(ShowRecordingNotWired));
        OnPropertyChanged(nameof(ShowRecordingIndicator));
    }

    private IRecordingStore? _recordings;

    private IRecordingSession? _recordingSession;

    private volatile IRecordingSession? _liveRecording;

    private RecordingScope _recordingScope = RecordingScope.FilteredOnly;

    private bool _recordingWantsRaw;

    public Func<Task>? RequestRecordings { get; set; }

    public string RecordingAdapterLabel =>
        RecordingNaming.AdapterLabel(
            _session is { } session
                ? DeviceName(session, _sessionNote)
                : _localization["Device.Model.Unknown"]);

    private string? _sessionNote;

    // Toggles the selected state.
    [RelayCommand]
    private async Task ToggleRecordAsync()
    {
        if (IsRecording)
        {

            if (RequestRecordingStopDecision is null)
            {
                await StopRecordingAsync();
                return;
            }

            await ConfirmRecordingStopAsync(closingPort: false);
            return;
        }

        AppOptions recordingOptions = _options;

        if (_settings is { } settingsStore)
        {
            try
            {
                recordingOptions = await settingsStore.LoadOptionsAsync();
            }
            catch (Exception error)
            {

                Journal.Warning($"Recording options could not be re-read: {error.Message}");
            }
        }

        if (_recordings is not { } store || _session is not { } session)
        {

            IsRecording = true;
            return;
        }

        RecordingScope scope = recordingOptions.RecordFilteredOnly
            ? RecordingScope.FilteredOnly
            : RecordingScope.Everything;

        var recordingRequest = new RecordingRequest(
            RecordingAdapterLabel,
            scope,
            recordingOptions.RecordRawText,
            session.Device.RegistryKey,
            session.Generation,

            RecordingNaming.PortLabel(session.Device.PortName),

            session.Configuration,

            SlcanDeviceInfo.TryParse(session.DeviceReport ?? string.Empty, out SlcanDeviceInfo? report)
             && report is not null
                ? report.FirmwareVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null,
            AppVersion,
            DateTimeOffset.Now,
            FilterText?.Trim());

        try
        {
            _recordingSession = await store.StartAsync(recordingRequest);
        }
        catch (Exception error)
        {

            TransportMessage = _localization["Record.StartFailed"];
            OnPropertyChanged(nameof(StatusText));
            Journal.Error($"Recording could not start: {error.Message}");
            return;
        }

        _recordingScope = scope;
        _recordingWantsRaw = recordingOptions.RecordRawText;

        _recordingSession.Fault += OnRecordingFault;

        _liveRecording = _recordingSession;
        IsRecording = true;

        Journal.Info(
            $"Recording started: {recordingRequest.Adapter} on {recordingRequest.Port}, {scope}"
            + (recordingOptions.RecordRawText ? ", raw text kept" : string.Empty));
    }

    // Stops recording.
    private async Task StopRecordingAsync()
    {
        IRecordingSession? session = _recordingSession;

        _liveRecording = null;
        _recordingSession = null;
        IsRecording = false;

        if (session is null)
        {
            return;
        }

        session.Fault -= OnRecordingFault;

        RecordingResult result = await session.StopAsync();
        await session.DisposeAsync();

        if (result.Fault is { Length: > 0 } fault)
        {

            TransportMessage = _localization["Record.Faulted"];
            OnPropertyChanged(nameof(StatusText));
            Journal.Error($"Recording faulted: {fault}");
        }

        Journal.Info(
            $"Recording stopped: {result.Recording.Adapter} on {result.Recording.Port}, "
            + $"{result.FrameCount} frame(s).");
    }

    // Handles recording fault.
    private void OnRecordingFault(object? sender, RecordingFaultEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _liveRecording = null;
            _recordingSession = null;
            IsRecording = false;

            TransportMessage = _localization["Record.Faulted"];
            OnPropertyChanged(nameof(StatusText));

            Journal.Error($"Recording stopped by a fault: {e.Message}");
        });

    private static string AppVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    // Shows the requested dialog.
    [RelayCommand]
    private async Task ShowRecordingsAsync()
    {
        if (RequestRecordings is { } request)
        {
            await request();
        }
    }

    public Func<Task>? RequestAbout { get; set; }

    // Shows the requested dialog.
    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        if (RequestAbout is { } request)
        {
            await request();
        }
    }

    public Func<Task>? RequestOptions { get; set; }

    // Shows the requested dialog.
    [RelayCommand]
    private async Task ShowOptionsAsync()
    {
        if (RequestOptions is { } request)
        {
            await request();
        }
    }

    public Func<Task>? RequestExport { get; set; }

    [ObservableProperty]
    public partial string SendId { get; set; } = "123";

    [ObservableProperty]
    public partial string SendData { get; set; } = "DEADBEEF";

    [ObservableProperty]
    public partial bool SendExtended { get; set; }

    [ObservableProperty]
    public partial string SendIntervalMs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SendError { get; set; }

    public bool HasSendError => SendError is not null;

    public bool CanSend => IsConnected && !IsListenOnly;

    // Starts session.
    [RelayCommand]
    private async Task StartSessionAsync(SessionTypeViewModel? type)
    {
        if (RequestNewSession is not { } request || type is null)
        {
            return;
        }

        NewSessionResult? result = await request(type);

        if (result is null)
        {
            return;
        }

        await CloseSessionAsync();
        await AttachAsync(result);
    }

    // Shows the requested dialog.
    [RelayCommand]
    private async Task ShowSessionChooserAsync() => await CloseSessionAsync();

    // Disconnects the device.
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!await ConfirmRecordingStopAsync())
        {
            return;
        }

        await ClosePortAsync();
        Notify();
    }

    public Func<NewSessionResult, Task<NewSessionResult?>>? RequestReconnect { get; set; }

    // Toggles the selected state.
    [RelayCommand(CanExecute = nameof(CanToggleConnection))]
    private async Task ToggleConnectionAsync()
    {

        StopAutoReconnect();

        if (IsConnected)
        {
            await DisconnectAsync();
            return;
        }

        await ReconnectAsync();
    }

    // Checks toggle connection.
    private bool CanToggleConnection() => HasSession;

    // Reconnects the device.
    private async Task ReconnectAsync()
    {
        if (_session is not { } previous || RequestReconnect is not { } reconnect)
        {
            return;
        }

        TransportMessage = null;
        OnPropertyChanged(nameof(StatusText));

        NewSessionResult? reopened = await reconnect(previous);

        if (reopened is null)
        {

            return;
        }

        await AttachAsync(reopened);
        Journal.Info($"Reconnected: {reopened.Title}");
        Notify();
    }

    private DispatcherTimer? _reconnectTimer;
    private int _reconnectAttempt;

    public bool IsReconnecting => _reconnectTimer is not null;

    public int ReconnectAttempt => _reconnectAttempt;

    // Starts device reconnection.
    internal void BeginAutoReconnect()
    {
        if (_reconnectTimer is not null || !_options.AutoReconnect || _session is null)
        {
            return;
        }

        _reconnectAttempt = 1;

        _reconnectTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_options.ReconnectDelayMs),
        };

        _reconnectTimer.Tick += (_, _) => _ = TryReconnectOnceAsync();
        _reconnectTimer.Start();

        Journal.Info(
            $"Auto-reconnect armed: up to {_options.ReconnectAttempts} attempt(s), "
            + $"{_options.ReconnectDelayMs} ms apart.");

        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(ReconnectAttempt));
        OnPropertyChanged(nameof(StatusText));
    }

    // Stops auto reconnect.
    internal void StopAutoReconnect()
    {
        if (_reconnectTimer is null)
        {
            return;
        }

        _reconnectTimer.Stop();
        _reconnectTimer = null;

        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(StatusText));
    }

    // Tries reconnect once.
    internal async Task TryReconnectOnceAsync()
    {
        if (_reconnectTimer is null)
        {
            return;
        }

        int limit = _options.ReconnectAttempts;

        await ReconnectAsync();

        if (IsConnected)
        {
            StopAutoReconnect();
            Journal.Info($"Auto-reconnect succeeded on attempt {_reconnectAttempt}.");
            return;
        }

        if (_reconnectAttempt < limit)
        {

            _reconnectAttempt++;
            OnPropertyChanged(nameof(ReconnectAttempt));
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        StopAutoReconnect();

        TransportMessage = _localization.Format("Reconnect.GaveUp", limit);
        Journal.Warning($"Auto-reconnect gave up after {limit} attempt(s).");
        Notify();
    }

    public string ConnectionLabel =>
        _localization[IsConnected ? "Toolbar.Disconnect" : "Toolbar.Connect"];

    // Closes session.
    [RelayCommand]
    private async Task CloseSessionAsync()
    {
        if (!await ConfirmRecordingStopAsync())
        {
            return;
        }

        await ClosePortAsync();
        ClearCapturedData();
        EndSession();
        Notify();
    }

    // Closes port.
    private async Task ClosePortAsync()
    {

        StopAutoReconnect();

        CancelPendingQueueSave();

        await SaveTransmitQueueAsync();

        _refresh?.Stop();
        _refresh = null;

        DrainReports();

        _reportPump?.Stop();
        _reportPump = null;

        if (_transport is { } transport)
        {
            transport.FrameReceived -= OnFrameReceived;
            transport.Error -= OnTransportError;

            await transport.CloseAsync();
            await transport.DisposeAsync();
            _transport = null;
        }

        if (Periodic is { } periodic)
        {
            periodic.Running -= OnPeriodicRunningChanged;
            periodic.QueueEdited -= OnTransmitQueueEdited;
            Periodic = null;

            if (periodic.HasRunningTasks)
            {
                Journal.Info("Periodic sending stopped: the port is closing.");
            }

            await periodic.DisposeAsync();
            OnPropertyChanged(nameof(IsTransmittingPeriodically));
        }

        if (IsConnected)
        {
            Journal.Info($"Port closed. {TotalFrames} frame(s) captured, {Rows.Count} id(s).");
        }

        IsConnected = false;
        IsPaused = false;

        IsRecording = false;
    }

    // Clears captured data.
    private void ClearCapturedData()
    {
        Rows.Clear();
        _aggregator.Clear();
        StreamRows.Clear();
        _log.Clear();

        _lastSnapshot = null;
        _drawnSequence = -1;
        _previousStreamTime = null;
        SelectedRow = null;
        SelectedStreamRow = null;
        TotalFrames = 0;
    }

    // Ends the active session.
    private void EndSession()
    {
        ConfigurationConfirmed = false;

        IsNamingOffered = false;
        _session = null;
        SessionTitle = null;
        TransportMessage = null;

        if (HasSession)
        {
            Journal.Info("Session closed.");
        }

        HasSession = false;

    }

    // Confirms the requested action.
    private async Task<bool> ConfirmRecordingStopAsync(bool closingPort = true)
    {
        if (!IsRecording || RequestRecordingStopDecision is not { } ask)
        {
            return true;
        }

        RecordingStopChoice choice = await ask(closingPort);

        if (choice == RecordingStopChoice.Cancel)
        {
            return false;
        }

        await StopRecordingAsync();

        if (choice == RecordingStopChoice.KeepAndExport && RequestExport is { } export)
        {
            await export();
        }

        return true;
    }

    // Confirms the requested action.
    public async Task<bool> ConfirmClosingAsync()
    {
        try
        {
            return await ConfirmRecordingStopAsync(closingPort: true);
        }
        catch (Exception error)
        {

            Journal.Warning($"The running recording could not be closed cleanly: {error.Message}");
            return true;
        }
    }

    public Func<bool, Task<RecordingStopChoice>>? RequestRecordingStopDecision { get; set; }

    // Clears the stored state.
    [RelayCommand]
    private void Clear()
    {
        if (SelectedTab == 1)
        {
            _log.Clear();
            StreamRows.Clear();
            _drawnSequence = -1;
            _previousStreamTime = null;
            return;
        }

        _aggregator.Clear();
        Rows.Clear();

        _lastSnapshot = null;
        SelectedRow = null;
        TotalFrames = 0;
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IdCountText));
    }

    // Toggles the selected state.
    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;

        if (IsPaused)
        {
            _refresh?.Stop();
        }
        else
        {
            _refresh?.Start();
        }
    }

    // Sends the requested data.
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        SendError = null;
        OnPropertyChanged(nameof(HasSendError));

        if (_transport is not { } transport)
        {
            return;
        }

        try
        {
            await transport.SendAsync(SendFrameInput.Parse(SendId, SendData, SendExtended));

            string used = $"{SendId.Trim()} {SendData.Trim()}";

            Remember(SendHistory, used);

            RememberOnDisk(HistoryKind.SendFrame, used);
        }
        catch (Exception ex)
        {

            SendError = ex.Message;
            OnPropertyChanged(nameof(HasSendError));

            Journal.Error($"Send failed: {ex.Message}");
        }
    }

    // Sets language.
    [RelayCommand]
    private void SetLanguage(LanguageMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _localization.SetLanguage(item.Option.Culture);

        foreach (LanguageMenuItemViewModel each in Languages)
        {
            each.Refresh();
        }
    }

    // Attaches the active session.
    private async Task AttachAsync(NewSessionResult result)
    {

        _transport = new FrameLoggingTransport(result.Transport, OnFrameTransmitted, _clock);
        _transport.FrameReceived += OnFrameReceived;
        _transport.Error += OnTransportError;

        IsConnected = true;

        HasSession = true;
        IsListenOnly = result.IsListenOnly;
        ConfigurationConfirmed = result.ConfigurationConfirmed;

        _session = result;

        _sessionNote = null;
        SessionTitle = SessionLine(result, note: null);
        TransportMessage = null;

        var periodic = new PeriodicSendPanelViewModel(
            new PeriodicSendRunner(_transport, canTransmit: !result.IsListenOnly),
            _localization,

            () => _clock.Now,
            post: _periodicPost,
            timerResolution: _timerResolution);

        periodic.Running += OnPeriodicRunningChanged;

        periodic.QueueEdited += OnTransmitQueueEdited;

        periodic.Start();

        IReadOnlyList<TransmitQueueRow>? rows = Periodic?.Snapshot();

        if (rows is null && _transmitQueue is { } queue)
        {
            try
            {
                rows = await queue.LoadAsync();
            }
            catch (Exception ex)
            {

                Journal.Warning($"The transmit queue could not be read: {ex.Message}");
            }
        }

        if (rows is not null)
        {
            periodic.Restore(rows);
        }

        Periodic = periodic;

        Journal.Restart();
        Journal.Info(DescribeSessionOpened(result));

        if (!result.ConfigurationConfirmed)
        {
            Journal.Warning("The adapter cannot confirm its configuration; the rate above is what was asked for.");
        }

        if (result.ReportsErrorCounters == false && !result.IsListenOnly)
        {
            Journal.Warning(
                "This adapter does not report error counters, so diCAN cannot confirm that a frame "
                + "it sent reached the bus.");
        }

        _aggregator.Clear();
        Rows.Clear();
        _log.Clear();
        StreamRows.Clear();
        _drawnSequence = -1;
        _previousStreamTime = null;

        IsBottomPanelOpen = true;
        SelectedBottomTab = 0;

        _nominalBitsPerSecond = result.BitsPerSecond;
        _dataBitsPerSecond = result.DataBitrate;
        Interlocked.Exchange(ref _wireNanoseconds, 0);
        _rateSamples.Clear();
        _framesPerSecond = 0;
        _busLoadPercent = 0;

        _origin = DateTimeOffset.UtcNow;
        TotalFrames = 0;

        _reports.Clear();

        _refresh = new DispatcherTimer { Interval = RefreshInterval };
        _refresh.Tick += (_, _) => PullSnapshot();
        _refresh.Start();

        _reportPump = new DispatcherTimer { Interval = RefreshInterval };
        _reportPump.Tick += (_, _) => DrainReports();
        _reportPump.Start();

        Notify();

        if (_registry is not { } registry || result.IsSimulated)
        {
            return;
        }

        try
        {
            CanDeviceRecord record = await registry.RecordConnectionAsync(new CanConnectionFact(
                Device: result.Device,
                Generation: result.Generation,
                VersionResponse: result.DeviceReport,
                PortName: result.Device.PortName ?? string.Empty,
                NominalBitrate: result.BitsPerSecond,
                DataBitrate: result.DataBitrate,
                Mode: result.Mode));

            _sessionNote = record.Note;
            SessionTitle = SessionLine(result, record.Note);

            IsNamingOffered = record.ConnectCount == 1;
            NameDraft = string.Empty;

            Notify();
        }
        catch (Exception ex)
        {

            TransportMessage = _localization["Session.DeviceRecordSaveFailed"];
            OnPropertyChanged(nameof(StatusText));
            Journal.Error($"The adapter record could not be saved: {ex.Message}");
        }
    }

    private NewSessionResult? _session;

    [ObservableProperty]
    public partial bool IsNamingOffered { get; set; }

    [ObservableProperty]
    public partial string NameDraft { get; set; } = string.Empty;

    public string NamePlaceholder =>
        _session is { } session ? DeviceName(session, note: null) : string.Empty;

    // Saves name.
    [RelayCommand]
    private async Task SaveNameAsync()
    {
        if (_registry is not { } registry || _session is not { } session)
        {
            IsNamingOffered = false;
            return;
        }

        try
        {
            await registry.SetNoteAsync(
                session.Device.RegistryKey, session.Generation, NameDraft);

            CanDeviceRecord? record = await registry.FindAsync(
                session.Device.RegistryKey, session.Generation);

            _sessionNote = record?.Note;
            SessionTitle = SessionLine(session, record?.Note);
        }
        catch (Exception ex)
        {

            TransportMessage = _localization["Naming.SaveFailed"];
            Journal.Error($"The adapter name could not be saved: {ex.Message}");
        }

        IsNamingOffered = false;
        Notify();
    }

    // Dismisses the naming prompt.
    [RelayCommand]
    private void DismissNaming() => IsNamingOffered = false;

    // Gets device name.
    private string DeviceName(NewSessionResult session, string? note) =>
        CanDeviceNaming.DisplayName(_localization, session.Device, session.DeviceReport, note);

    // Gets session line.
    private string SessionLine(NewSessionResult session, string? note) =>
        $"{DeviceName(session, note)} · {session.Title} · {ModeLabel(session.Mode)}";

    // Gets mode label.
    private string ModeLabel(SlcanOpenMode mode) => _localization[mode switch
    {
        SlcanOpenMode.Silent => "Mode.Short.Silent",
        SlcanOpenMode.InternalLoopback => "Mode.Short.InternalLoopback",
        SlcanOpenMode.ExternalLoopback => "Mode.Short.ExternalLoopback",
        _ => "Mode.Short.Normal",
    }];

    // Handles frame received.
    private void OnFrameReceived(object? sender, CanFrameReceivedEventArgs e)
    {
        _aggregator.Add(e.Frame, CanFrameDirection.Received);
        _log.Add(e.Frame, CanFrameDirection.Received);
        AccountForWireTime(e.Frame);
        Record(e.Frame, CanFrameDirection.Received, e.RawText);
    }

    // Records incoming data.
    private void Record(CanFrame frame, CanFrameDirection direction, string? rawText)
    {
        if (_liveRecording is not { } recording)
        {
            return;
        }

        if (_recordingScope == RecordingScope.FilteredOnly && !Filter.Matches(frame))
        {
            return;
        }

        recording.Add(frame, direction, _recordingWantsRaw ? rawText : null);
    }

    // Tracks transmission time.
    private void AccountForWireTime(CanFrame frame)
    {
        if (_nominalBitsPerSecond <= 0)
        {
            return;
        }

        double seconds = CanBusLoad.SecondsOnWire(frame, _nominalBitsPerSecond, _dataBitsPerSecond);

        Interlocked.Add(ref _wireNanoseconds, (long)(seconds * 1_000_000_000));
    }

    // Handles frame transmitted.
    private void OnFrameTransmitted(CanFrame frame)
    {
        _aggregator.Add(frame, CanFrameDirection.Transmitted);
        _log.Add(frame, CanFrameDirection.Transmitted);

        AccountForWireTime(frame);

        Record(frame, CanFrameDirection.Transmitted, null);
    }

    // Handles transport error.
    private void OnTransportError(object? sender, CanTransportErrorEventArgs e) =>
        _reports.Add(_clock.Now, e);

    // Drains queued input data.
    private void DrainReports()
    {
        TransportReportBatch batch = _reports.Drain();

        if (batch.Reports.Count == 0 && batch.Dropped == 0)
        {
            return;
        }

        var writes = new List<JournalWrite>(batch.Reports.Count + 1);

        foreach (PendingTransportReport pending in batch.Reports)
        {
            writes.Add(ApplyOneReport(pending.Report, pending.At));
        }

        if (batch.Dropped > 0)
        {
            writes.Add(new JournalWrite(
                SessionEventLevel.Warning,
                $"{batch.Dropped} more transport report(s) arrived than could be shown and were dropped.",
                "reports dropped",
                _clock.Now));
        }

        Journal.AddRange(writes);
        OnPropertyChanged(nameof(StatusText));
    }

    // Applies transport error.
    internal void ApplyTransportError(CanTransportErrorEventArgs e)
    {
        Journal.AddRange([ApplyOneReport(e, _clock.Now)]);
        OnPropertyChanged(nameof(StatusText));
    }

    // Applies one report.
    private JournalWrite ApplyOneReport(CanTransportErrorEventArgs e, DateTimeOffset at)
    {
        {

            if (e.IsFatal)
            {

                TransportMessage = _localization["Session.ConnectionLost"];
            }

            var write = new JournalWrite(
                e.IsFatal ? SessionEventLevel.Error : SessionEventLevel.Warning,
                e.IsFatal ? $"Port lost: {e.Message}" : $"Transport reported: {e.Message}",
                e.CollapseKey is null
                    ? null
                    : (e.IsFatal ? $"Port lost: {e.CollapseKey}" : $"Transport reported: {e.CollapseKey}"),
                at);

            if (e.IsFatal)
            {

                IsConnected = false;
                _refresh?.Stop();

                BeginAutoReconnect();

                Notify();
            }

            return write;
        }
    }

    private IReadOnlyList<CanIdRow>? _lastSnapshot;

    // Gets the current snapshot.
    private void PullSnapshot()
    {

        IReadOnlyList<CanIdRow> snapshot;
        bool live;

        if (IsPaused && _lastSnapshot is { } held)
        {
            snapshot = held;
            live = false;
        }
        else
        {
            snapshot = _lastSnapshot = _aggregator.Snapshot();
            TotalFrames = _aggregator.TotalFrames;
            live = true;
        }

        List<CanIdRow> visible = Visible(snapshot);

        visible.Sort(static (a, b) => a.Id != b.Id
            ? a.Id.CompareTo(b.Id)
            : a.IsExtended.CompareTo(b.IsExtended));

        RowFormat format = Display.Format;

        for (int i = 0; i < visible.Count; i++)
        {
            if (i < Rows.Count)
            {
                Rows[i].Update(visible[i], format);
            }
            else
            {
                Rows.Add(new CanIdRowViewModel(visible[i], format));
            }
        }

        while (Rows.Count > visible.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        if (SelectedTab == 1)
        {
            SyncStream(StreamCeiling);
        }

        if (live)
        {
            SampleRate();
        }

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IdCountText));
        RaiseDetailChanged();
    }

    private const int StreamRowLimit = 5_000;

    private long StreamCeiling => IsPaused ? _drawnSequence : long.MaxValue;

    // Synchronizes the current view.
    private void SyncStream(long throughSequence = long.MaxValue)
    {
        CanFrameLogEntry[] fresh = _log.Snapshot(_drawnSequence, StreamRowLimit, throughSequence);

        if (fresh.Length == 0)
        {
            return;
        }

        if (_drawnSequence >= 0 && fresh[0].Sequence > _drawnSequence + 1)
        {
            StreamRows.Add(FrameStreamRowViewModel.Gap(
                fresh[0].Sequence,
                _localization.Format("Stream.Gap", fresh[0].Sequence - _drawnSequence - 1)));

            _previousStreamTime = null;
        }

        RowFormat format = Display.Format;

        foreach (CanFrameLogEntry entry in fresh)
        {
            _drawnSequence = entry.Sequence;

            if (!Admits(entry))
            {
                continue;
            }

            StreamRows.Add(
                FrameStreamRowViewModel.FromEntry(entry, _origin, format, _previousStreamTime));

            _previousStreamTime = entry.Frame.Timestamp;
        }

        while (StreamRows.Count > StreamRowLimit)
        {
            StreamRows.RemoveAt(0);
        }
    }

    // Rebuilds the displayed data.
    private void RebuildStream()
    {

        long? reselect = SelectedStreamRow is { IsGap: false } row ? row.Sequence : null;

        long ceiling = StreamCeiling;

        StreamRows.Clear();
        _drawnSequence = -1;
        _previousStreamTime = null;
        SelectedStreamRow = null;

        if (SelectedTab == 1)
        {
            SyncStream(ceiling);

            if (reselect is { } sequence)
            {
                SelectedStreamRow = StreamRows
                    .FirstOrDefault(r => !r.IsGap && r.Sequence == sequence);
            }
        }
    }

    // Checks filter eligibility.
    private bool Admits(CanFrameLogEntry entry)
    {

        if (entry.Direction == CanFrameDirection.Transmitted && !Display.ShowOwnFramesInStream)
        {
            return false;
        }

        return Filter.IsPassAll || Filter.Matches(entry.Frame);
    }

    // Gets visible frame rows.
    private List<CanIdRow> Visible(IReadOnlyList<CanIdRow> snapshot)
    {
        if (Filter.IsPassAll)
        {
            return [.. snapshot];
        }

        var visible = new List<CanIdRow>(snapshot.Count);

        foreach (CanIdRow row in snapshot)
        {
            var probe = new CanFrame
            {
                Id = row.Id,
                IsExtended = row.IsExtended,
                Data = ReadOnlyMemory<byte>.Empty,
            };

            if (Filter.Matches(probe))
            {
                visible.Add(row);
            }
        }

        return visible;
    }

    // Notifies state changes.
    private void Notify()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IdCountText));
        SendCommand.NotifyCanExecuteChanged();

        AddToTransmitListCommand.NotifyCanExecuteChanged();
    }

    // Handles is connected changed.
    partial void OnIsConnectedChanged(bool value)
    {
        FallBackToDetailIfPageIsGone();
        RaiseBottomPanelChanged();
        OnPropertyChanged(nameof(StatusText));

        OnPropertyChanged(nameof(ConnectionLabel));
    }

    // Closes application services.
    public async Task ShutdownAsync()
    {
        Journal.Info("Closing normally.");

        await CloseSessionAsync();
    }
}
