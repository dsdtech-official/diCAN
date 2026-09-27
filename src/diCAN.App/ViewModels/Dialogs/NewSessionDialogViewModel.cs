using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Settings;
using DiCAN.Core.Transport;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages new session dialog.
public sealed partial class NewSessionDialogViewModel : ObservableObject
{
    private readonly ICanDeviceEnumerator _enumerator;
    private readonly ICanSessionOpener _opener;
    private readonly ILocalizationService _localization;
    private readonly ICanDeviceWatcher? _watcher;

    private readonly IDeviceRegistry? _registry;

    private readonly ISettingsStore? _settings;

    // Initializes this instance.
    public NewSessionDialogViewModel(
        ICanDeviceEnumerator enumerator,
        ICanSessionOpener opener,
        SessionTypeViewModel sessionType,
        ILocalizationService localization,
        ICanDeviceWatcher? watcher = null,
        IDeviceRegistry? registry = null,
        TimeSpan? retryDelay = null,
        ISettingsStore? settings = null)
    {
        _settings = settings;
        _enumerator = enumerator;
        _opener = opener;
        _localization = localization;
        _watcher = watcher;
        _registry = registry;

        _retryDelay = retryDelay ?? DefaultRetryDelay;

        SessionType = sessionType;

        if (_watcher is not null)
        {

            _watcher.DevicesChanged += OnDevicesChanged;
        }

        Rescan();
    }

    public SessionTypeViewModel SessionType { get; }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public ObservableCollection<BitrateChoice> Bitrates { get; } = [];

    public ObservableCollection<BitrateChoice> DataBitrates { get; } = [];

    public NewSessionResult? Result { get; private set; }

    public event EventHandler? CloseRequested;

    public event EventHandler? DetailedConfigurationRequested;

    public Func<FirmwareNoticeViewModel, Task>? ShowFirmwareNoticeAsync { get; set; }

    [ObservableProperty]
    public partial DeviceItemViewModel? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial BitrateChoice? SelectedBitrate { get; set; }

    [ObservableProperty]
    public partial BitrateChoice? SelectedDataBitrate { get; set; }

    [ObservableProperty]
    public partial bool UseDataBitrate { get; set; }

    public CanDeviceIdentity Identity => SelectedDevice?.Identity ?? CanDeviceIdentity.Unknown;

    [ObservableProperty]
    public partial bool AutoRetransmit { get; set; } = true;

    public bool IsIdentifying => SelectedDevice?.IsIdentifying ?? false;

    public bool AnyIdentifying => Devices.Any(d => d.IsIdentifying);

    public string RescanLabel =>
        _localization[AnyIdentifying ? "Dialog.Rescan.Busy" : "Dialog.Rescan"];

    public bool CannotBeAsked => SelectedDevice is { CanBeAsked: false };

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? OpenError { get; set; }

    public bool HasOpenError => OpenError is not null;

    public bool HasIdentity => !IsIdentifying && Identity.IsIdentified;

    public bool IsDataBitrateSupported => DataBitrates.Count > 0;

    public string DataBitratePlaceholder =>
        _localization[HasIdentity && !IsDataBitrateSupported
            ? "Dialog.DataBitrate.Unsupported"
            : "Dialog.Bitrate.Placeholder"];

    public bool CanChooseRates => HasIdentity;

    private const double DeviceRowHeight = 30;

    public double DeviceListHeight =>
        (Math.Min(Math.Max(3, Devices.Count) + 1, 9) * DeviceRowHeight) + 2;

    public ObservableCollection<BusModeChoice> Modes { get; } = [];

    [ObservableProperty]
    public partial BusModeChoice? SelectedMode { get; set; }

    public bool CanConfirm =>
        !IsBusy && HasIdentity && SelectedBitrate is not null && SelectedMode is not null &&
        SelectedDevice is { IsUsable: true } &&
        (!UseDataBitrate || SelectedDataBitrate is not null);

    // Handles selected mode changed.
    partial void OnSelectedModeChanged(BusModeChoice? value) =>
        ConfirmCommand.NotifyCanExecuteChanged();

    // Scans connected devices.
    [RelayCommand]
    private void Rescan() => Apply(_enumerator.Enumerate(), SelectedDevice?.Device.DeviceId);

    // Starts identifying all.
    private void StartIdentifyingAll(IReadOnlyList<DeviceItemViewModel> waiting)
    {

        _identifyCts?.Cancel();
        _identifyCts?.Dispose();
        _identifyCts = null;

        DeviceItemViewModel[] pending = [.. waiting];

        AllIdentified = pending.Length == 0;

        if (pending.Length == 0)
        {
            return;
        }

        foreach (DeviceItemViewModel item in pending)
        {
            item.IsIdentifying = true;
        }

        RaiseIdentityDerived();

        var cts = new CancellationTokenSource();
        _identifyCts = cts;

        IdentificationPass = IdentifyAllAsync(pending, cts.Token);
    }

    public Task IdentificationPass { get; private set; } = Task.CompletedTask;

    private const int IdentifyAttempts = 3;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    private readonly TimeSpan _retryDelay;

    // Identifies connected devices.
    private async Task IdentifyAllAsync(
        IReadOnlyList<DeviceItemViewModel> pending, CancellationToken cancellationToken)
    {

        var remaining = new List<DeviceItemViewModel>(pending);

        for (var round = 1; round <= IdentifyAttempts && remaining.Count > 0; round++)
        {
            if (round > 1)
            {

                await Task.Delay(_retryDelay, cancellationToken);

                remaining = await Task.Run(() => Refresh(remaining), cancellationToken);
            }

            remaining = await IdentifyRoundAsync(remaining, cancellationToken);
        }

        AllIdentified = true;
    }

    // Refreshes the current state.
    private List<DeviceItemViewModel> Refresh(IReadOnlyList<DeviceItemViewModel> waiting)
    {
        Dictionary<string, CanDeviceInfo> current = _enumerator
            .Enumerate()
            .GroupBy(d => d.DeviceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var refreshed = new List<DeviceItemViewModel>(waiting.Count);

        foreach (DeviceItemViewModel item in waiting)
        {
            if (!current.TryGetValue(item.Device.DeviceId, out CanDeviceInfo? now))
            {
                continue;
            }

            refreshed.Add(new DeviceItemViewModel(now, _localization));
        }

        return refreshed;
    }

    // Identifies connected devices.
    private async Task<List<DeviceItemViewModel>> IdentifyRoundAsync(
        IReadOnlyList<DeviceItemViewModel> pending, CancellationToken cancellationToken)
    {
        var stillUnknown = new List<DeviceItemViewModel>();

        foreach (DeviceItemViewModel item in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return stillUnknown;
            }

            CanDeviceIdentity identity = await Task.Run(
                () => _opener.IdentifyAsync(item.Device), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return stillUnknown;
            }

            await ApplyRegistryAsync(item, identity, cancellationToken);

            ApplyIdentity(item, identity);

            if (IsSupported(item))
            {
                Show(item);
            }
            else
            {
                stillUnknown.Add(item);
            }
        }

        return stillUnknown;
    }

    // Checks supported.
    private static bool IsSupported(DeviceItemViewModel item) =>
        item.Identity is { } identity &&
        (identity.IsIdentified || identity.Capabilities is not null);

    // Shows the requested dialog.
    private void Show(DeviceItemViewModel item)
    {
        int position = _enumerationOrder.TryGetValue(item.Device.DeviceId, out int order) ? order : int.MaxValue;

        var index = 0;

        while (index < Devices.Count &&
               (_enumerationOrder.TryGetValue(Devices[index].Device.DeviceId, out int other) ? other : int.MaxValue) <= position)
        {
            index++;
        }

        Devices.Insert(index, item);

        SelectedDevice ??= item;

        OnPropertyChanged(nameof(DeviceListHeight));
        OnPropertyChanged(nameof(ShowNoDevices));
        OnPropertyChanged(nameof(ShowDetecting));
    }

    // Applies registry.
    private async Task ApplyRegistryAsync(
        DeviceItemViewModel item, CanDeviceIdentity identity, CancellationToken cancellationToken)
    {
        if (_registry is not { } registry || !identity.IsIdentified)
        {
            return;
        }

        try
        {
            CanDeviceRecord? record = await registry.FindAsync(
                item.Device.RegistryKey, identity.Generation, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                item.Remembered = record;
                item.NoteText = record?.Note ?? string.Empty;
            }
        }
        catch (Exception)
        {

        }
    }

    // Applies identity.
    private void ApplyIdentity(DeviceItemViewModel item, CanDeviceIdentity identity)
    {
        item.IsIdentifying = false;
        item.IdentifyAttempted = true;

        item.Identity = identity;

        if (identity.IsIdentified)
        {
            _identified[item.Device.DeviceId] = identity;
        }

        OnPropertyChanged(nameof(AnyIdentifying));
        OnPropertyChanged(nameof(RescanLabel));

        if (ReferenceEquals(item, SelectedDevice))
        {
            RaiseIdentityDerived();
            RebuildRates();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    // Confirms the requested action.
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {

        if (SelectedDevice?.Device is not { } device ||
            SelectedBitrate is not { } rate ||
            SelectedMode is not { } mode)
        {
            return;
        }

        IsBusy = true;
        OpenError = null;
        OnPropertyChanged(nameof(HasOpenError));
        ConfirmCommand.NotifyCanExecuteChanged();

        try
        {

            int? dataBitrate = UseDataBitrate ? SelectedDataBitrate?.BitsPerSecond : null;

            var configuration = new CanBusConfiguration(
                rate.BitsPerSecond, dataBitrate, mode.Mode, SamplePoint,
                AutoRetransmit: AutoRetransmit);

            await OfferFirmwareNoticeAsync(device.FirmwareLine);

            Result = await _opener.OpenAsync(device, Identity.Generation, configuration, rate.Label);

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {

            OpenError = _localization.Describe(ex);
            OnPropertyChanged(nameof(HasOpenError));
        }
        finally
        {
            IsBusy = false;
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    // Shows the firmware notice.
    private async Task OfferFirmwareNoticeAsync(CanFirmwareLine line)
    {
        if (ShowFirmwareNoticeAsync is null || !FirmwareNoticeViewModel.AppliesTo(line))
        {
            return;
        }

        string suppressed = _settings is null
            ? string.Empty
            : await _settings.LoadTextAsync(TextSettingKeys.FirmwareNoticeSuppressed) ?? string.Empty;

        List<string> silenced = [.. suppressed.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        string token = line.ToString();
        if (silenced.Contains(token, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var notice = new FirmwareNoticeViewModel(line, _localization);
        await ShowFirmwareNoticeAsync(notice);

        if (notice.DontShowAgain && _settings is not null)
        {
            silenced.Add(token);
            await _settings.SaveTextAsync(
                TextSettingKeys.FirmwareNoticeSuppressed, string.Join(',', silenced));
        }
    }

    // Shows the requested dialog.
    [RelayCommand]
    private void ShowDetailedConfiguration() =>
        DetailedConfigurationRequested?.Invoke(this, EventArgs.Empty);

    // Creates the requested object.
    public DetailedConfigurationViewModel CreateDetailedConfiguration() =>
        new(Identity, AutoRetransmit, _localization, IsSamplePointSettable, SamplePoint,
            SelectedDevice?.Device, _opener as IDfuUnlocker);

    // Applies the requested changes.
    public void ApplyDetailedConfiguration(DetailedConfigurationViewModel panel)
    {
        if (!panel.Accepted)
        {
            return;
        }

        AutoRetransmit = panel.AutoRetransmit;

        double? previous = SamplePoint;
        SamplePoint = panel.SelectedSamplePoint.Value;

        if (SamplePoint != previous)
        {
            RebuildRates();
        }
    }

    public double? SamplePoint { get; private set; }

    public bool IsSamplePointSettable =>
        SelectedDevice?.Device.Kind is CanDeviceKind.Candlelight;

    // Cancels the active operation.
    [RelayCommand]
    private void Cancel()
    {

        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Detaches the active session.
    public void Detach()
    {
        if (_watcher is not null)
        {
            _watcher.DevicesChanged -= OnDevicesChanged;
        }

        _identifyCts?.Cancel();
        _identifyCts?.Dispose();
        _identifyCts = null;
    }

    // Handles devices changed.
    private void OnDevicesChanged(object? sender, CanDevicesChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => Apply(e.Current, SelectedDevice?.Device.DeviceId));

    private readonly Dictionary<string, CanDeviceIdentity> _identified = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _enumerationOrder = new(StringComparer.Ordinal);

    private bool AllIdentified
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ShowNoDevices));
            OnPropertyChanged(nameof(ShowDetecting));
        }
    } = true;

    public bool ShowDetecting => !AllIdentified && Devices.Count == 0;

    public bool ShowNoDevices => AllIdentified && Devices.Count == 0;

    private CancellationTokenSource? _identifyCts;

    // Applies the requested changes.
    private void Apply(IReadOnlyList<CanDeviceInfo> found, string? keep)
    {
        Devices.Clear();
        _enumerationOrder.Clear();

        var waiting = new List<DeviceItemViewModel>();

        foreach (CanDeviceInfo device in found)
        {

            if (device.Kind is CanDeviceKind.StmBootloader)
            {
                continue;
            }

            _enumerationOrder[device.DeviceId] = _enumerationOrder.Count;

            var item = new DeviceItemViewModel(device, _localization);

            if (_identified.TryGetValue(device.DeviceId, out CanDeviceIdentity? known))
            {
                item.Identity = known;

                item.IdentifyAttempted = true;

                Devices.Add(item);
                continue;
            }

            waiting.Add(item);
        }

        SelectedDevice =
            Devices.FirstOrDefault(d => d.Device.DeviceId == keep) ??
            Devices.FirstOrDefault(d => d.IsUsable);

        OnPropertyChanged(nameof(DeviceListHeight));

        StartIdentifyingAll(waiting);
    }

    // Handles a state change.
    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {

        OpenError = null;
        OnPropertyChanged(nameof(HasOpenError));

        RaiseIdentityDerived();
        RebuildRates();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    // Handles a state change.
    partial void OnSelectedBitrateChanged(BitrateChoice? value) =>
        ConfirmCommand.NotifyCanExecuteChanged();

    // Handles a state change.
    partial void OnSelectedDataBitrateChanged(BitrateChoice? value) =>
        ConfirmCommand.NotifyCanExecuteChanged();

    // Handles a state change.
    partial void OnUseDataBitrateChanged(bool value) =>
        ConfirmCommand.NotifyCanExecuteChanged();

    // Raises the requested event.
    private void RaiseIdentityDerived()
    {
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(IsIdentifying));
        OnPropertyChanged(nameof(AnyIdentifying));
        OnPropertyChanged(nameof(RescanLabel));
        OnPropertyChanged(nameof(CannotBeAsked));
        OnPropertyChanged(nameof(HasIdentity));
        OnPropertyChanged(nameof(CanChooseRates));

        OnPropertyChanged(nameof(DataBitratePlaceholder));
    }

    // Rebuilds the displayed data.
    private void RebuildRates()
    {
        SlcanFirmwareGeneration generation = Identity.Generation;

        if (Identity.Capabilities is { } capabilities)
        {
            double point = SamplePoint ?? GsUsbBitTimingSolver.DefaultSamplePoint;

            Fill(Bitrates, Solvable(capabilities.NominalLimits, CanStandardBitrates.Nominal, point));

            Fill(DataBitrates, capabilities is { SupportsCanFd: true, DataLimits: { } dataLimits }
                ? Solvable(dataLimits, CanStandardBitrates.Data, GsUsbBitTimingSolver.DataPhaseSamplePoint)
                : []);
        }
        else
        {
            Fill(Bitrates, SlcanBitrateTables.SupportedNominalRates(generation));
            Fill(DataBitrates, SlcanBitrateTables.SupportedDataRates(generation));
        }

        SlcanOpenMode? previous = SelectedMode?.Mode;

        Modes.Clear();

        IReadOnlyList<SlcanOpenMode> modes = Identity.Capabilities is { } caps
            ? GsUsbModes(caps, SelectedDevice?.Device.FirmwareLine ?? CanFirmwareLine.Unknown)
            : SlcanModeTables.SupportedModes(generation);

        foreach (SlcanOpenMode mode in modes)
        {
            Modes.Add(new BusModeChoice(mode, _localization[SlcanModeTables.DescriptionKey(mode)]));
        }

        SelectedMode =
            Modes.FirstOrDefault(m => m.Mode == previous) ??
            Modes.FirstOrDefault(m => m.Mode == SlcanOpenMode.Silent) ??
            Modes.FirstOrDefault();

        CanDeviceRecord? remembered = SelectedDevice?.Remembered;

        SelectedBitrate = Reselect(
            Bitrates, SelectedBitrate?.BitsPerSecond ?? remembered?.LastNominalBitrate, 500_000);
        SelectedDataBitrate = Reselect(
            DataBitrates, SelectedDataBitrate?.BitsPerSecond ?? remembered?.LastDataBitrate, 2_000_000);

        if (remembered is { LastDataBitrate: not null } && IsDataBitrateSupported)
        {
            UseDataBitrate = true;
        }

        if (!IsDataBitrateSupported)
        {

            UseDataBitrate = false;
        }

        OnPropertyChanged(nameof(IsDataBitrateSupported));

        OnPropertyChanged(nameof(DataBitratePlaceholder));
    }

    // Finds valid bitrate choices.
    private static List<int> Solvable(
        GsUsbBitTimingLimits limits, IReadOnlyList<int> candidates, double samplePoint) =>
        [.. candidates.Where(rate =>
            GsUsbBitTimingSolver.TrySolve(limits, rate, out _, samplePoint))];

    // Lists supported bus modes.
    private static List<SlcanOpenMode> GsUsbModes(GsUsbCapabilities capabilities, CanFirmwareLine line)
    {
        var modes = new List<SlcanOpenMode> { SlcanOpenMode.Normal };

        bool modeBitsAreHonoured = line is not CanFirmwareLine.CanableFdCandlelight;

        if (!modeBitsAreHonoured)
        {

            return modes;
        }

        if (capabilities.SupportsListenOnly)
        {
            modes.Add(SlcanOpenMode.Silent);
        }

        if (capabilities.SupportsLoopback)
        {
            if (capabilities.SupportsListenOnly)
            {
                modes.Add(SlcanOpenMode.InternalLoopback);
            }

            modes.Add(SlcanOpenMode.ExternalLoopback);
        }

        return modes;
    }

    // Fills the requested list.
    private static void Fill(ObservableCollection<BitrateChoice> target, IReadOnlyList<int> rates)
    {
        target.Clear();

        foreach (int rate in rates)
        {
            target.Add(new BitrateChoice(rate, SlcanBitrateTables.Describe(rate)));
        }
    }

    // Restores the selected value.
    private static BitrateChoice? Reselect(
        ObservableCollection<BitrateChoice> choices, int? previous, int preferred) =>
        choices.FirstOrDefault(b => b.BitsPerSecond == previous) ??
        choices.FirstOrDefault(b => b.BitsPerSecond == preferred) ??
        choices.FirstOrDefault();
}

// Stores bitrate choice data.
public sealed record BitrateChoice(int BitsPerSecond, string Label);

// Stores bus mode choice data.
public sealed record BusModeChoice(SlcanOpenMode Mode, string Label);
