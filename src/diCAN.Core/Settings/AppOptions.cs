namespace DiCAN.Core.Settings;

// Stores app options data.
public readonly record struct AppOptions(
    int DevicePollMs,
    int StreamCapacity,
    bool AutoReconnect = false,
    int ReconnectAttempts = 5,
    int ReconnectDelayMs = 2_000,
    bool RecordRawText = false,
    bool RecordFilteredOnly = true)
{

    public static AppOptions Default { get; } =
        new(
            AppSettingKeys.DevicePollMs.Default,
            AppSettingKeys.StreamCapacity.Default,
            AppSettingKeys.AutoReconnect.Default,
            AppSettingKeys.ReconnectAttempts.Default,
            AppSettingKeys.ReconnectDelayMs.Default,
            AppSettingKeys.RecordRawText.Default,
            AppSettingKeys.RecordFilteredOnly.Default);

    // Limits the requested value.
    public AppOptions Clamped() =>
        this with
        {
            DevicePollMs = AppSettingKeys.DevicePollMs.Clamp(DevicePollMs),
            StreamCapacity = AppSettingKeys.StreamCapacity.Clamp(StreamCapacity),
            ReconnectAttempts = AppSettingKeys.ReconnectAttempts.Clamp(ReconnectAttempts),
            ReconnectDelayMs = AppSettingKeys.ReconnectDelayMs.Clamp(ReconnectDelayMs),
        };
}

// Manages app setting keys.
public static class AppSettingKeys
{

    public static readonly IntSetting DevicePollMs =
        new("app.devicePollMs", 1500, Minimum: 250, Maximum: 10_000,
            Note: "How often the device list is refreshed, in milliseconds");

    public static readonly IntSetting StreamCapacity =
        new("display.streamCapacity", 10_000, Minimum: 1_000, Maximum: 200_000,
            Note: "Frames kept by the frame stream view");

    public static readonly BoolSetting AutoReconnect =
        new("app.autoReconnect", false, "Retry after an unexpected disconnect");

    public static readonly BoolSetting RecordRawText =
        new("record.rawText", false, "Keep the adapter's own slcan text in recordings");

    public static readonly BoolSetting RecordFilteredOnly =
        new("record.filteredOnly", true, "Record only the frames the filter passes");

    public static readonly IntSetting ReconnectAttempts =
        new("app.reconnectAttempts", 5, Minimum: 1, Maximum: 100,
            Note: "Reconnect attempts before giving up");

    public static readonly IntSetting ReconnectDelayMs =
        new("app.reconnectDelayMs", 2_000, Minimum: 250, Maximum: 60_000,
            Note: "Milliseconds between reconnect attempts");

    public static IReadOnlyList<IntSetting> All { get; } =
        [DevicePollMs, StreamCapacity, ReconnectAttempts, ReconnectDelayMs];
}
