using System.ComponentModel;
using DiCAN.App.Localization;

namespace DiCAN.App.ViewModels;

// Manages language menu item.
public sealed class LanguageMenuItemViewModel : INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;

    // Initializes this instance.
    internal LanguageMenuItemViewModel(ILocalizationService localization, LanguageOption option)
    {
        _localization = localization;
        Option = option;

        _localization.CultureChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
    }

    public LanguageOption Option { get; }

    public string Header => Option.NativeName;

    public bool IsCurrent => string.Equals(
        Option.CultureName,
        _localization.CurrentCulture.Name,
        StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    // Refreshes the current state.
    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
}
