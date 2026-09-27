namespace DiCAN.Core.Protocol;

// Manages can standard bitrates.
public static class CanStandardBitrates
{

    public static IReadOnlyList<int> Nominal { get; } =
    [
        5_000, 10_000, 20_000, 33_300, 50_000, 62_500, 75_000, 83_300,
        100_000, 125_000, 250_000, 500_000, 750_000, 800_000, 1_000_000,
    ];

    public static IReadOnlyList<int> Data { get; } =
    [
        500_000, 1_000_000, 2_000_000, 4_000_000, 5_000_000, 8_000_000,
    ];
}
