using CommunityToolkit.Mvvm.ComponentModel;
using DiCAN.App.Localization;
using DiCAN.Core.Protocol;
using DiCAN.Core.Sending;

namespace DiCAN.App.ViewModels;

// Manages session type.
public sealed partial class SessionTypeViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    public SessionTypeViewModel(
        ILocalizationService localization,
        SlcanOpenMode mode,
        string titleKey,
        string descriptionKey,
        string iconData)
    {
        _localization = localization;
        Mode = mode;
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        IconData = iconData;

        _localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
        };
    }

    public SlcanOpenMode Mode { get; }

    public string TitleKey { get; }

    public string DescriptionKey { get; }

    public string IconData { get; }

    public string Title => _localization[TitleKey];

    public string Description => _localization[DescriptionKey];

    public bool JoinsBus => Mode == SlcanOpenMode.Normal;

    private const string WatchIcon =
        "M12 5C6.5 5 2.7 9.2 1.5 12 2.7 14.8 6.5 19 12 19s9.3-4.2 10.5-7C21.3 9.2 17.5 5 12 5z "
        + "M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7z";

    private const string NodeOnBusIcon =
        "M2 12h4 M18 12h4 M9 4h6a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z "
        + "M6.5 8.5 4 12l2.5 3.5 M17.5 8.5 20 12l-2.5 3.5";

    // Gets all available items.
    public static IReadOnlyList<SessionTypeViewModel> All(ILocalizationService localization) =>
    [
        new(localization, SlcanOpenMode.Normal,
            "SessionType.Single.Title", "SessionType.Single.Description", NodeOnBusIcon),
    ];
}
