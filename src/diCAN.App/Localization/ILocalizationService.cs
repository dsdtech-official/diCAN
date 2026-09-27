using System.ComponentModel;
using System.Globalization;

namespace DiCAN.App.Localization;

// Manages i localization.
public interface ILocalizationService : INotifyPropertyChanged
{

    string this[string key] { get; }

    // Finds the requested item.
    string? Find(string key);

    // Gets bindable.
    LocalizedString GetBindable(string key);

    // Formats the requested value.
    string Format(string key, params object?[] args);

    CultureInfo CurrentCulture { get; }

    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    // Sets language.
    void SetLanguage(CultureInfo culture);

    event EventHandler? CultureChanged;
}

// Stores language option data.
public sealed record LanguageOption(string CultureName, string NativeName)
{
    public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(CultureName);

    // Formats the value as text.
    public override string ToString() => NativeName;
}

// Manages localized string.
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;
    private readonly string _key;

    // Initializes this instance.
    internal LocalizedString(ILocalizationService localization, string key)
    {
        _localization = localization;
        _key = key;
        _localization.CultureChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public string Value => _localization[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    // Formats the value as text.
    public override string ToString() => Value;
}
