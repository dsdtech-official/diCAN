using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiCAN.App.Localization;

// Manages localization.
public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources = new(
        "DiCAN.App.Resources.Strings", typeof(LocalizationService).Assembly);

    public static LocalizationService? Current { get; private set; }

    // Sets the active data source.
    public static void InstallAsCurrent(LocalizationService instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (Current is not null && !ReferenceEquals(Current, instance))
        {
            throw new InvalidOperationException(
                "A different LocalizationService is already installed as Current. The composition "
                + "root installs exactly one; a second would silently change which resources every "
                + "{loc:Translate} in the application reads.");
        }

        Current = instance;
    }

    private readonly ILogger _logger;

    // Initializes this instance.
    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger ?? NullLogger<LocalizationService>.Instance;
        CurrentCulture = DefaultCulture;
    }

    public static CultureInfo DefaultCulture { get; } = CultureInfo.GetCultureInfo("en");

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [

        new("en", "English"),
        new("zh-Hans", "简体中文"),

        new("zh-Hant", "繁體中文"),
        new("ja", "日本語"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("pt", "Português"),
    ];

    public CultureInfo CurrentCulture { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;

    private readonly ConcurrentDictionary<string, LocalizedString> _bindables = new();

    public string this[string key] =>
        string.IsNullOrEmpty(key) ? string.Empty : Find(key) ?? $"!{key}!";

    // Finds the requested item.
    public string? Find(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        try
        {
            return Resources.GetString(key, CurrentCulture);
        }
        catch (MissingManifestResourceException e)
        {

            _logger.LogDebug(e, "Resource lookup failed for {Key}; falling back.", key);
            return null;
        }
    }

    // Gets bindable.
    public LocalizedString GetBindable(string key) =>
        _bindables.GetOrAdd(key, k => new LocalizedString(this, k));

    // Formats the requested value.
    public string Format(string key, params object?[] args)
    {
        string template = this[key];

        if (args.Length == 0)
        {
            return template;
        }

        try
        {

            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException e)
        {

            _logger.LogDebug(e, "Format failed for {Key}; showing the raw template.", key);
            return template;
        }
    }

    // Sets language.
    public void SetLanguage(CultureInfo culture)
    {
        if (culture.Name == CurrentCulture.Name)
        {

            Announce();
            return;
        }

        Apply(culture);
    }

    // Applies the requested changes.
    private void Apply(CultureInfo culture)
    {
        CurrentCulture = culture;

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        Announce();
    }

    // Reports the current event.
    private void Announce()
    {

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
