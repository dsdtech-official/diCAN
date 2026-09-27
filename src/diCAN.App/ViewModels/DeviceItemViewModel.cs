using CommunityToolkit.Mvvm.ComponentModel;
using DiCAN.App.Localization;
using DiCAN.App.ViewModels.Dialogs;
using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Recordings;

namespace DiCAN.App.ViewModels;

// Manages device item.
public sealed partial class DeviceItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    public DeviceItemViewModel(CanDeviceInfo device, ILocalizationService localization)
    {
        Device = device;
        _localization = localization;

        _localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ModelLabel));
            OnPropertyChanged(nameof(FirmwareLabel));
            OnPropertyChanged(nameof(Advice));
        };
    }

    public CanDeviceInfo Device { get; }

    public CanDeviceIdentity? Identity
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ModelLabel));
            OnPropertyChanged(nameof(FirmwareLabel));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareLabel))]
    public partial bool IsIdentifying { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareLabel))]
    public partial bool IdentifyAttempted { get; set; }

    public bool CanBeAsked =>
        Device.PortName is not null || Device.Kind is CanDeviceKind.Candlelight;

    public string PortText => Device.PortDisplayName
        ?? (Device.Kind is CanDeviceKind.Candlelight ? RecordingNaming.NoPortLabel : "—");

    public string Subtitle => $"{Device.UsbId} · {Device.Description}";

    public bool IsUsable => Device.IsUsable;

    public string ModelLabel =>
        CanDeviceNaming.ModelLabel(_localization, Device, Identity?.Report?.DeviceId);

    [ObservableProperty]
    public partial string NoteText { get; set; } = string.Empty;

    public CanDeviceRecord? Remembered { get; set; }

    public string FirmwareLabel
    {
        get
        {

            if (!CanBeAsked)
            {
                return _localization["Device.State.NoPort"];
            }

            if (Identity is not { } identity)
            {

                return string.Empty;
            }

            if (identity.Capabilities is not null)
            {

                return FirmwareLineLabel(ProtocolFallbackGsUsb);
            }

            if (identity.IsIdentified)
            {

                return FirmwareLineLabel(ProtocolFallbackSlcan);
            }

            return string.Empty;
        }
    }

    private const string ProtocolFallbackGsUsb = "gs_usb";

    private const string ProtocolFallbackSlcan = "SLCAN";

    // Gets firmware line label.
    private string FirmwareLineLabel(string protocolFallback) =>
        CanFirmwareLines.NameOf(Device.FirmwareLine, Device.FirmwareVersion) ?? protocolFallback;

    public string? Advice
    {
        get
        {

            var steps = CanDeviceGuidance.For(
                CanDeviceDiagnosis.ForDevice(Device),
                platform: CanDeviceGuidance.CurrentPlatform);

            if (steps.Count == 0) return null;

            var lines = new List<string>();

            foreach (var step in steps.Where(s => s.IsUniversal))
            {
                lines.Add(_localization[TextKey(step.Claim)]);
            }

            foreach (var family in FamilyOrder)
            {
                var forFamily = steps.Where(s => s.AppliesTo == family).ToArray();

                if (forFamily.Length == 0) continue;

                lines.Add(_localization[FamilyKey(family)]);
                lines.AddRange(forFamily.Select(s => "    " + _localization[TextKey(s.Claim)]));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool HasAdvice => Advice is not null;

    // Gets text key.
    public static string TextKey(CanGuidanceClaim claim) => $"Guidance.{claim}";

    // Gets family key.
    public static string FamilyKey(CanChipFamily family) => $"Guidance.Family.{family}";

    public static readonly CanChipFamily[] FamilyOrder =
        [CanChipFamily.Stm32G431, CanChipFamily.Stm32F072];
}
