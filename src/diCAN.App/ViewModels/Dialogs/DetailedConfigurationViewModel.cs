using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiCAN.App.Localization;
using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;

namespace DiCAN.App.ViewModels.Dialogs;

// Stores device detail row data.
public sealed record DeviceDetailRow(string Label, string Value, string? Hint = null, bool Advanced = false)
{

    public bool HasHint => !string.IsNullOrEmpty(Hint);
}

// Manages sample point.
public sealed record SamplePointOption(double? Value, string Label)
{

    // Formats the value as text.
    public override string ToString() => Label;
}

// Manages adapter configuration.
public sealed partial class DetailedConfigurationViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    public DetailedConfigurationViewModel(
        CanDeviceIdentity identity,
        bool autoRetransmit,
        ILocalizationService localization,
        bool samplePointSettable = false,
        double? samplePoint = null,
        CanDeviceInfo? device = null,
        IDfuUnlocker? dfuUnlocker = null)
    {
        _localization = localization;
        _device = device;
        _dfuUnlocker = dfuUnlocker;
        Identity = identity;
        AutoRetransmit = autoRetransmit;
        Details = BuildDetails(identity);
        IsSamplePointSettable = samplePointSettable;

        SamplePoints =
        [
            new(null, localization["Dialog.Detailed.SamplePoint.Default"]),
            new(0.80, "80 %"),
            new(0.875, "87.5 %"),
        ];

        SelectedSamplePoint =
            SamplePoints.FirstOrDefault(o => o.Value == samplePoint) ?? SamplePoints[0];
    }

    public CanDeviceIdentity Identity { get; }

    [ObservableProperty]
    public partial bool AutoRetransmit { get; set; }

    public bool IsIdentified => Identity.IsIdentified;

    public IReadOnlyList<SamplePointOption> SamplePoints { get; }

    [ObservableProperty]
    public partial SamplePointOption SelectedSamplePoint { get; set; }

    public bool IsSamplePointSettable { get; }

    public IReadOnlyList<DeviceDetailRow> Details { get; }

    private static bool ShowAdvancedDetails => false;

    public IReadOnlyList<DeviceDetailRow> VisibleDetails =>
        ShowAdvancedDetails
            ? Details
            : [.. Details.Where(row => !row.Advanced)];

    public bool HasDetails => VisibleDetails.Count > 0;

    public bool ReportsNothingMore => IsIdentified && Details.Count == 0;

    public bool Accepted { get; set; }

    private readonly CanDeviceInfo? _device;
    private readonly IDfuUnlocker? _dfuUnlocker;

    public bool ShowDfuSection => _device is { } device && _dfuUnlocker is not null && DsdTechDfu.AppliesTo(device);

    public string? DfuGuideUrl => _device is { } device ? CanFirmwareLines.DfuGuidePageFor(device.FirmwareLine) : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDfuButtonEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowDfuAllowed))]
    [NotifyPropertyChangedFor(nameof(ShowDfuFailed))]
    public partial DfuRequestState DfuState { get; set; }

    public bool IsDfuButtonEnabled => DfuState is DfuRequestState.Idle or DfuRequestState.Failed;

    public bool ShowDfuAllowed => DfuState == DfuRequestState.Allowed;

    public bool ShowDfuFailed => DfuState == DfuRequestState.Failed;

    // Enables firmware update mode.
    [RelayCommand]
    private async Task AllowDfuAsync()
    {
        if (_device is null || _dfuUnlocker is null)
        {
            return;
        }

        DfuState = DfuRequestState.Working;
        DfuPinStatus? status = await _dfuUnlocker.AllowDfuOnNextPowerUpAsync(_device);
        DfuState = status is { EnabledForNextPowerUp: true } ? DfuRequestState.Allowed : DfuRequestState.Failed;
    }

    // Builds details.
    private IReadOnlyList<DeviceDetailRow> BuildDetails(CanDeviceIdentity identity)
    {
        var rows = new List<DeviceDetailRow>(5);

        if (identity.Capabilities is { } capabilities)
        {
            Add("Dialog.Detailed.Clock", $"{capabilities.CanClockHz / 1_000_000.0:0.###} MHz");
            Add("Dialog.Detailed.CanFd", YesNo(capabilities.SupportsCanFd));
            Add("Dialog.Detailed.ListenOnly", YesNo(capabilities.SupportsListenOnly));
            Add("Dialog.Detailed.Loopback", YesNo(capabilities.SupportsLoopback));

            Add("Dialog.Detailed.HardwareTimestamp",
                capabilities.SupportsHardwareTimestamp
                    ? _localization.Format("Dialog.Detailed.HardwareTimestamp.Unused", YesNo(true))
                    : YesNo(false));
            Add("Dialog.Detailed.ErrorCounters", YesNo(capabilities.SupportsErrorCounters));

            Add(capabilities.DataLimits is null ? "Dialog.Detailed.Limits" : "Dialog.Detailed.NominalLimits",
                FormatLimits(capabilities.NominalLimits),
                "Dialog.Detailed.Limits.Hint",
                advanced: true);

            if (capabilities.DataLimits is { } dataLimits)
            {

                Add("Dialog.Detailed.DataLimits", FormatLimits(dataLimits), advanced: true);
            }

            return rows;
        }

        Add("Dialog.Detailed.Firmware",
            SlcanFirmwareId.BuildOf(identity.VersionResponse, identity.Report));

        if (identity.Report is not { } report)
        {
            return rows;
        }

        Add("Dialog.Detailed.Mcu", FormatMcu(report.Mcu));
        Add("Dialog.Detailed.Clock", FormatClock(report));
        Add("Dialog.Detailed.Serial", report.SerialNumber);

        Add("Dialog.Detailed.Limits", FormatLimits(report.BitTimingLimits),
            "Dialog.Detailed.Limits.Hint",
            advanced: true);

        return rows;

        // Adds the requested entry.
        void Add(string key, string? value, string? hintKey = null, bool advanced = false)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new DeviceDetailRow(
                    _localization[key],
                    value,
                    hintKey is null ? null : _localization[hintKey],
                    advanced));
            }
        }
    }

    // Formats a boolean choice.
    private string YesNo(bool value) =>
        _localization[value ? "Common.Yes" : "Common.No"];

    // Formats limits.
    private static string FormatLimits(GsUsbBitTimingLimits limits) =>
        $"tseg1 {limits.Tseg1Min}..{limits.Tseg1Max} · tseg2 {limits.Tseg2Min}..{limits.Tseg2Max} · "
        + $"sjw ≤{limits.SjwMax} · brp {limits.BrpMin}..{limits.BrpMax}";

    // Formats mcu.
    private string? FormatMcu(string? mcu) =>

        string.Equals(mcu?.Trim(), "none", StringComparison.OrdinalIgnoreCase)
            ? _localization["Common.None"]
            : mcu;

    // Formats clock.
    private string? FormatClock(SlcanDeviceInfo report)
    {
        if (report.ClockMhz is not { } mhz)
        {
            return null;
        }

        string crystal = report.HasQuartz switch
        {
            true => _localization["Dialog.Device.Crystal.Yes"],
            false => _localization["Dialog.Device.Crystal.No"],
            null => string.Empty,
        };

        return crystal.Length == 0 ? $"{mhz} MHz" : $"{mhz} MHz · {crystal}";
    }

    // Formats limits.
    private static string? FormatLimits(IReadOnlyList<int>? limits)
    {
        if (limits is not { Count: > 0 })
        {
            return null;
        }

        if (limits.Count != 8)
        {
            return string.Join(", ", limits);
        }

        return $"{string.Join(", ", limits.Take(4))}  ·  {string.Join(", ", limits.Skip(4))}";
    }
}
