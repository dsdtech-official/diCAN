namespace DiCAN.Core.Protocol;

// Manages slcan bitrate tables.
public static class SlcanBitrateTables
{

    private static readonly (int Rate, char Command)[] Canable10Nominal =
    [
        (10_000, '0'), (20_000, '1'), (50_000, '2'), (100_000, '3'), (125_000, '4'),
        (250_000, '5'), (500_000, '6'), (750_000, '7'), (1_000_000, '8'),
    ];

    private static readonly (int Rate, char Command)[] Canable20Nominal =
    [
        .. Canable10Nominal, (83_300, '9'),
    ];

    private static readonly (int Rate, char Command)[] Elmue25Nominal =
    [
        (5_000, 'D'), (10_000, '0'), (20_000, '1'), (33_300, 'C'), (50_000, '2'),
        (62_500, 'B'), (75_000, 'A'), (83_300, '9'), (100_000, '3'), (125_000, '4'),
        (250_000, '5'), (500_000, '6'), (800_000, '7'), (1_000_000, '8'),
    ];

    private static readonly (int Rate, char Command)[] Canable20Data =
    [
        (2_000_000, '2'), (5_000_000, '5'),
    ];

    private static readonly (int Rate, char Command)[] Elmue25Data =
    [
        (500_000, '0'), (1_000_000, '1'), (2_000_000, '2'),
        (4_000_000, '4'), (5_000_000, '5'), (8_000_000, '8'),
    ];

    // Gets supports can fd.
    public static bool SupportsCanFd(SlcanFirmwareGeneration generation) =>
        generation is not (SlcanFirmwareGeneration.Canable10 or SlcanFirmwareGeneration.Unknown);

    // Gets the bitrate command.
    public static char? NominalCommandFor(SlcanFirmwareGeneration generation, int bitsPerSecond) =>
        Lookup(NominalTable(generation), bitsPerSecond);

    // Gets the data bitrate command.
    public static char? DataCommandFor(SlcanFirmwareGeneration generation, int bitsPerSecond) =>
        Lookup(DataTable(generation), bitsPerSecond);

    // Gets supported nominal rates.
    public static IReadOnlyList<int> SupportedNominalRates(SlcanFirmwareGeneration generation) =>
        [.. NominalTable(generation).Select(e => e.Rate)];

    // Gets supported data rates.
    public static IReadOnlyList<int> SupportedDataRates(SlcanFirmwareGeneration generation) =>
        [.. DataTable(generation).Select(e => e.Rate)];

    // Describes the requested value.
    public static string Describe(IReadOnlyList<int> rates) =>
        rates.Count == 0 ? "(none)" : string.Join(", ", rates.Select(Describe));

    // Describes the requested value.
    public static string Describe(int bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000 when bitsPerSecond % 1_000_000 == 0 => $"{bitsPerSecond / 1_000_000}M",
        _ when bitsPerSecond % 1_000 == 0 => $"{bitsPerSecond / 1_000}k",
        _ => $"{bitsPerSecond / 1000.0:0.#}k",
    };

    // Gets the bit rate.
    public static int? BitsPerSecond(SlcanNominalBitrate rate) => Reverse(Elmue25Nominal, (char)rate);

    // Gets the bit rate.
    public static int? BitsPerSecond(SlcanDataBitrate rate) => Reverse(Elmue25Data, (char)rate);

    // Lists bitrate commands.
    private static (int Rate, char Command)[] NominalTable(SlcanFirmwareGeneration generation) =>
        generation switch
        {
            SlcanFirmwareGeneration.Canable10 => Canable10Nominal,
            SlcanFirmwareGeneration.Canable20 => Canable20Nominal,
            SlcanFirmwareGeneration.Elmue25 => Elmue25Nominal,
            _ => [],
        };

    // Lists data bitrate commands.
    private static (int Rate, char Command)[] DataTable(SlcanFirmwareGeneration generation) =>
        generation switch
        {
            SlcanFirmwareGeneration.Canable20 => Canable20Data,
            SlcanFirmwareGeneration.Elmue25 => Elmue25Data,
            _ => [],
        };

    // Looks up the requested value.
    private static char? Lookup((int Rate, char Command)[] table, int bitsPerSecond)
    {
        foreach ((int candidate, char command) in table)
        {
            if (candidate == bitsPerSecond)
            {
                return command;
            }
        }

        return null;
    }

    // Finds the matching bit rate.
    private static int? Reverse((int Rate, char Command)[] table, char command)
    {
        foreach ((int rate, char candidate) in table)
        {
            if (candidate == command)
            {
                return rate;
            }
        }

        return null;
    }
}
