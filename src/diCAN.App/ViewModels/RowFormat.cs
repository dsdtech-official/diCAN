namespace DiCAN.App.ViewModels;

// Stores row format data.
public readonly record struct RowFormat(
    bool DecimalIds = false,
    bool SpacedData = false,
    bool ShowDelta = false,
    bool ShowMinMaxDelta = false,
    bool HighlightChanges = false,
    bool StreamDeltaTime = false)
{
    public static readonly RowFormat Default = new();
}
