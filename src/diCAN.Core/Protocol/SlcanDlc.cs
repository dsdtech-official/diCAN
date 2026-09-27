namespace DiCAN.Core.Protocol;

// Manages slcan dlc.
public static class SlcanDlc
{

    public const int MaxPayload = 64;

    public const int MaxClassicPayload = 8;

    // Gets the payload length.
    public static int ToByteCount(int dlcCode) => dlcCode switch
    {
        >= 0 and <= 8 => dlcCode,
        0x9 => 12,
        0xA => 16,
        0xB => 20,
        0xC => 24,
        0xD => 32,
        0xE => 48,
        0xF => 64,
        _ => -1,
    };

    // Gets the data length code.
    public static int FromByteCount(int byteCount) => byteCount switch
    {
        > 48 => 15,
        > 32 => 14,
        > 24 => 13,
        > 20 => 12,
        > 16 => 11,
        > 12 => 10,
        > 8 => 9,
        >= 0 => byteCount,
        _ => -1,
    };

    // Checks exact length.
    public static bool IsExactLength(int byteCount) =>
        byteCount >= 0 && ToByteCount(FromByteCount(byteCount)) == byteCount;
}
