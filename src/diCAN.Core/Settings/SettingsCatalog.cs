using System.Globalization;

namespace DiCAN.Core.Settings;

// Manages settings catalog.
public static class SettingsCatalog
{

    public static IReadOnlyDictionary<string, SettingsRowInfo> All { get; } = Build();

    // Builds the requested result.
    private static Dictionary<string, SettingsRowInfo> Build()
    {
        var catalogue = new Dictionary<string, SettingsRowInfo>(StringComparer.Ordinal);

        foreach (BoolSetting setting in DisplaySettingKeys.All)
        {
            catalogue[setting.Name] = setting.Describe();
        }

        catalogue[DisplaySettingKeys.FontSize.Name] = DisplaySettingKeys.FontSize.Describe();

        foreach (IntSetting setting in AppSettingKeys.All)
        {
            catalogue[setting.Name] = setting.Describe();
        }

        catalogue[AppSettingKeys.AutoReconnect.Name] = AppSettingKeys.AutoReconnect.Describe();
        catalogue[AppSettingKeys.RecordRawText.Name] = AppSettingKeys.RecordRawText.Describe();
        catalogue[AppSettingKeys.RecordFilteredOnly.Name] =
            AppSettingKeys.RecordFilteredOnly.Describe();

        WindowPlacement window = WindowPlacement.Default;

        catalogue["window.x"] = Number("Main window left edge, NaN until it has been placed", window.X);
        catalogue["window.y"] = Number("Main window top edge, NaN until it has been placed", window.Y);
        catalogue["window.width"] = Number("Main window width", window.Width);
        catalogue["window.height"] = Number("Main window height", window.Height);
        catalogue["window.maximized"] = Bool("Main window was maximized when it closed", window.Maximized);
        catalogue["window.bottomPanel"] =
            Number("Bottom panel height, whole panel including its chrome", window.BottomPanelHeight);

        catalogue["window.sidebarWidth"] =
            Number("Filter sidebar width, NaN until the user drags it", window.SidebarWidth);
        catalogue["window.sidebarOpen"] = Bool("Filter sidebar expanded", window.SidebarOpen);

        catalogue[TextSettingKeys.Language] =
            new("Interface language, empty means follow the system", "text", string.Empty);

        catalogue[TextSettingKeys.LastExportFolder] =
            new("Folder the last export was written to, empty until the first one", "text", string.Empty);

        return catalogue;
    }

    // Gets the numeric value.
    private static SettingsRowInfo Number(string note, double value) =>
        new(note, "number", DoubleSetting.Render(value));

    // Creates a boolean setting.
    private static SettingsRowInfo Bool(string note, bool value) =>
        new(note, "bool", BoolSetting.Render(value));
}
