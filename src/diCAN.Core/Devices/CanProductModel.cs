namespace DiCAN.Core.Devices;

// Defines adapter models.
public enum CanProductModel
{

    Unknown = 0,

    ShC30x,

    ShC31x,

    ShC31Bx,

    ShC32A,

    ShC32B,

}

// Maps firmware to models.
public static class CanProductModels
{

    public const int DevIdStm32G431 = 0x468;

    // Maps firmware to a model.
    public static CanProductModel For(CanFirmwareLine line, int? reportedDeviceId = null)
    {

        bool runsOnForeignBoardsToo = line is CanFirmwareLine.ElmueMultiboardSlcan;

        if (runsOnForeignBoardsToo && reportedDeviceId is { } id && id != DevIdStm32G431)
        {
            return CanProductModel.Unknown;
        }

        return line switch
        {

            CanFirmwareLine.CanableClassicCandlelight or CanFirmwareLine.DsdTechClassicCandlelight => CanProductModel.ShC30x,
            CanFirmwareLine.CanableFdCandlelight or CanFirmwareLine.DsdTechFdCandlelight => CanProductModel.ShC31x,

            CanFirmwareLine.DsdTechClassicSlcan => CanProductModel.ShC30x,

            CanFirmwareLine.ElmueMultiboardSlcan => CanProductModel.ShC31Bx,

            CanFirmwareLine.DsdTechC32Candlelight => CanProductModel.ShC32A,
            CanFirmwareLine.DsdTechC32Slcan => CanProductModel.ShC32B,

            CanFirmwareLine.ElmueMultiboardCandlelight => CanProductModel.Unknown,

            _ => CanProductModel.Unknown,
        };
    }

    // Gets the display name.
    public static string? NameOf(CanProductModel model) => model switch
    {
        CanProductModel.ShC30x => "SH-C30x",
        CanProductModel.ShC31x => "SH-C31x",

        CanProductModel.ShC31Bx => "SH-C31B",

        CanProductModel.ShC32A => "SH-C32A",
        CanProductModel.ShC32B => "SH-C32B",
        _ => null,
    };
}
