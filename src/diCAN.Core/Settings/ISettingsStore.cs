namespace DiCAN.Core.Settings;

// Stores bool setting data.
public sealed record BoolSetting(string Name, bool Default, string Note)
{

    // Renders the current view.
    public static string Render(bool value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Describes the requested value.
    public SettingsRowInfo Describe() => new(Note, "bool", Render(Default));
}

// Stores int setting data.
public sealed record IntSetting(
    string Name, int Default, int Minimum, int Maximum, string Note)
{

    // Limits the requested value.
    public int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);

    // Renders the current view.
    public static string Render(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Describes the requested value.
    public SettingsRowInfo Describe() =>
        new($"{Note} ({Minimum}-{Maximum})", "number", Render(Default));
}

// Stores settings row info data.
public sealed record SettingsRowInfo(string Note, string ValueType, string DefaultValue);

// Stores double setting data.
public sealed record DoubleSetting(
    string Name, double Default, double Minimum, double Maximum, string Note)
{

    // Limits the requested value.
    public double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Default;

    // Describes the requested value.
    public SettingsRowInfo Describe() =>
        new($"{Note} ({Minimum:0.##}-{Maximum:0.##})", "number", Render(Default));

    // Renders the current view.
    public static string Render(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

// Manages display setting keys.
public static class DisplaySettingKeys
{
    public static readonly BoolSetting ShowDeltaColumn =
        new("display.showDeltaColumn", false, "Per-id delta column in the aggregated table");

    public static readonly BoolSetting ShowMinMaxDeltaColumns =
        new("display.showMinMaxDeltaColumns", false, "Minimum and maximum delta columns");

    public static readonly BoolSetting ShowOwnFramesInStream =
        new("display.showOwnFramesInStream", true, "Frames this session sent appear in the stream");

    public static readonly BoolSetting DecimalIds =
        new("display.decimalIds", false, "CAN ids shown in decimal instead of hex");

    public static readonly BoolSetting SpacedData =
        new("display.spacedData", true, "Payload bytes separated by spaces");

    public static readonly BoolSetting HighlightChanges =
        new("display.highlightChanges", true, "Mark rows whose payload changed since the last refresh");

    public static readonly BoolSetting StreamDeltaTime =
        new("display.streamDeltaTime", false, "Frame stream time column shows the gap to the row above");

    public static readonly DoubleSetting FontSize =
        new("display.fontSize", 13, Minimum: 11, Maximum: 18, Note: "Table font size in points");

    public static IReadOnlyList<BoolSetting> All { get; } =
    [
        ShowDeltaColumn,
        ShowMinMaxDeltaColumns,
        ShowOwnFramesInStream,
        DecimalIds,
        SpacedData,
        HighlightChanges,
        StreamDeltaTime,
    ];
}

// Manages i settings.
public interface ISettingsStore
{

    // Loads saved data.
    Task<DisplaySettings> LoadAsync(CancellationToken cancellationToken = default);

    // Sets the requested value.
    Task SetAsync(BoolSetting setting, bool value, CancellationToken cancellationToken = default);

    // Loads options.
    Task<AppOptions> LoadOptionsAsync(CancellationToken cancellationToken = default);

    // Saves options.
    Task SaveOptionsAsync(AppOptions options, CancellationToken cancellationToken = default);

    // Synchronizes the current view.
    Task SyncAsync(
        IReadOnlyDictionary<string, SettingsRowInfo> catalogue,
        CancellationToken cancellationToken = default);

    // Loads window.
    Task<WindowPlacement> LoadWindowAsync(CancellationToken cancellationToken = default);

    // Saves window.
    Task SaveWindowAsync(WindowPlacement placement, CancellationToken cancellationToken = default);

    // Loads text.
    Task<string?> LoadTextAsync(string key, CancellationToken cancellationToken = default);

    // Saves text.
    Task SaveTextAsync(string key, string value, CancellationToken cancellationToken = default);
}

// Manages text setting keys.
public static class TextSettingKeys
{

    public const string Language = "ui.language";

    public const string FirmwareNoticeSuppressed = "firmware.notice.suppressed";

    public const string LastExportFolder = "export.lastFolder";
}
