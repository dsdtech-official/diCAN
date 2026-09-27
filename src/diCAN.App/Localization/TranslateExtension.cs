using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace DiCAN.App.Localization;

// Manages translate extension.
public sealed class TranslateExtension : MarkupExtension
{

    // Initializes this instance.
    public TranslateExtension()
    {
    }

    // Initializes this instance.
    public TranslateExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    // Provides the requested value.
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        LocalizedString? source = LocalizationService.Current?.GetBindable(Key);

        if (source is null)
        {
            return Key;
        }

        return new Binding
        {
            Mode = BindingMode.OneWay,
            Source = source,
            Path = nameof(LocalizedString.Value),
        };
    }
}
