namespace DiCAN.Core.Protocol;

// Manages slcan mode tables.
public static class SlcanModeTables
{
    private static readonly SlcanOpenMode[] Legacy =
    [
        SlcanOpenMode.Normal,
        SlcanOpenMode.Silent,
    ];

    private static readonly SlcanOpenMode[] Elmue25 =
    [
        SlcanOpenMode.Normal,
        SlcanOpenMode.Silent,
        SlcanOpenMode.InternalLoopback,
        SlcanOpenMode.ExternalLoopback,
    ];

    // Gets supported modes.
    public static IReadOnlyList<SlcanOpenMode> SupportedModes(SlcanFirmwareGeneration generation) =>
        generation switch
        {
            SlcanFirmwareGeneration.Elmue25 => Elmue25,
            SlcanFirmwareGeneration.Canable20 => Legacy,
            SlcanFirmwareGeneration.Canable10 => Legacy,
            _ => [],
        };

    // Gets description key.
    public static string DescriptionKey(SlcanOpenMode mode) => mode switch
    {
        SlcanOpenMode.Normal => "Mode.Normal",
        SlcanOpenMode.Silent => "Mode.Silent",
        SlcanOpenMode.InternalLoopback => "Mode.InternalLoopback",
        SlcanOpenMode.ExternalLoopback => "Mode.ExternalLoopback",
        _ => "Mode.Normal",
    };
}
