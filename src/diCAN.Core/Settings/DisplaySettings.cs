namespace DiCAN.Core.Settings;

// Stores display settings data.
public sealed record DisplaySettings
{

    public bool ShowDeltaColumn { get; init; }

    public bool ShowMinMaxDeltaColumns { get; init; }

    public bool ShowOwnFramesInStream { get; init; } =
        DisplaySettingKeys.ShowOwnFramesInStream.Default;

    public bool DecimalIds { get; init; } = DisplaySettingKeys.DecimalIds.Default;

    public bool SpacedData { get; init; } = DisplaySettingKeys.SpacedData.Default;

    public bool HighlightChanges { get; init; } = DisplaySettingKeys.HighlightChanges.Default;

    public bool StreamDeltaTime { get; init; } = DisplaySettingKeys.StreamDeltaTime.Default;

    public double FontSize { get; init; } = DisplaySettingKeys.FontSize.Default;

    public static DisplaySettings Default { get; } = new();
}
