using CommunityToolkit.Mvvm.ComponentModel;
using DiCAN.App.Localization;
using DiCAN.Core.Devices;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages firmware notice.
public sealed partial class FirmwareNoticeViewModel : ObservableObject
{

    // Checks firmware eligibility.
    public static bool AppliesTo(CanFirmwareLine line) =>
        line is CanFirmwareLine.CanableClassicCandlelight or CanFirmwareLine.CanableFdCandlelight;

    // Initializes this instance.
    public FirmwareNoticeViewModel(CanFirmwareLine line, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        Line = line;
        Title = localization["FirmwareNotice.Title"];
        Intro = localization["FirmwareNotice.Intro"];
        How = localization["FirmwareNotice.How"];

        Url = CanFirmwareLines.ProductPageFor(line);

        Items = line switch
        {

            CanFirmwareLine.CanableClassicCandlelight =>
            [
                localization["FirmwareNotice.Crystal"],
                localization["FirmwareNotice.StaleFrames"],
                localization["FirmwareNotice.OtherFixes"],
            ],
            CanFirmwareLine.CanableFdCandlelight =>
            [
                localization["FirmwareNotice.CanFd"],
                localization["FirmwareNotice.ErrorCounters"],
                localization["FirmwareNotice.ModesHonoured"],
                localization["FirmwareNotice.StaleFrames"],
            ],

            _ => [],
        };
    }

    public CanFirmwareLine Line { get; }

    public string Title { get; }

    public string Intro { get; }

    public string How { get; }

    public string? Url { get; }

    public bool HasUrl => !string.IsNullOrEmpty(Url);

    public IReadOnlyList<string> Items { get; }

    [ObservableProperty]
    private bool _dontShowAgain;
}
