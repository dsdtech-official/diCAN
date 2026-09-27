namespace DiCAN.Core.Devices;

// Defines device firmware types.
public enum CanFirmwareLine
{

    Unknown = 0,

    CanableClassicCandlelight,

    DsdTechClassicCandlelight,

    CanableFdCandlelight,

    DsdTechFdCandlelight,

    DsdTechClassicSlcan,

    CanableClassicSlcan,

    CanableFdSlcan,

    ElmueMultiboardCandlelight,

    ElmueMultiboardSlcan,

    DsdTechC32Candlelight,

    DsdTechC32Slcan,
}

// Stores device firmware facts.
public readonly record struct CanFirmwareFacts(
    CanFirmwareLine Line,
    CanChipFamily Chip,
    bool InProduction,
    string? Version = null);

// Identifies device firmware.
public static class CanFirmwareLines
{

    internal const string ElmueSlcanProductString = "Slcan 2.5 - Multiboard";

    internal const string ElmueCandlelightProductString = "Candlelight 2.5 - Multiboard";

    internal const string ElmueCandlelightDsdTechProductString = "Candlelight 2.5 - DsdTechC32A";

    internal const string ElmueSlcanDsdTechProductString = "Slcan 2.5 - DsdTechC32A";

    // Identifies device firmware.
    public static CanFirmwareFacts Identify(
        ushort vendorId,
        ushort productId,
        ushort? deviceRevision,
        string? reportedProductString)
    {
        string? product = reportedProductString?.Trim();

        if (vendorId == CanUsbIds.VendorMcs
            && productId == CanUsbIds.ProductSlcan
            && product == ElmueSlcanProductString)
        {
            return new(CanFirmwareLine.ElmueMultiboardSlcan, CanChipFamily.Stm32G431, true);
        }

        if (vendorId == CanUsbIds.VendorMcs
            && productId == CanUsbIds.ProductSlcan
            && product == ElmueSlcanDsdTechProductString)
        {
            return new(CanFirmwareLine.DsdTechC32Slcan, CanChipFamily.Stm32G431, true);
        }

        if (vendorId == CanUsbIds.VendorOpenMoko
            && productId == CanUsbIds.ProductCandlelight
            && product == ElmueCandlelightProductString)
        {
            return new(CanFirmwareLine.ElmueMultiboardCandlelight, CanChipFamily.Stm32G431, true);
        }

        if (vendorId == CanUsbIds.VendorOpenMoko
            && productId == CanUsbIds.ProductCandlelight
            && product == ElmueCandlelightDsdTechProductString)
        {
            return new(CanFirmwareLine.DsdTechC32Candlelight, CanChipFamily.Stm32G431, true);
        }

        if (deviceRevision is { } revision)
        {

            if (vendorId == CanUsbIds.VendorOpenMoko
                && productId == CanUsbIds.ProductCandlelight
                && DsdTechLine(revision) is { } dsdTech)
            {
                return dsdTech;
            }

            switch ((vendorId, productId, revision))
            {

                case (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, 0x0000):
                    return product switch
                    {
                        "candleLight USB to CAN adapter"
                            => new(CanFirmwareLine.CanableClassicCandlelight, CanChipFamily.Stm32F072, false),
                        "canable2 gs_usb"
                            => new(CanFirmwareLine.CanableFdCandlelight, CanChipFamily.Stm32G431, false),
                        _ => Unresolved,
                    };

                case (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, 0x2608):
                    return new(CanFirmwareLine.ElmueMultiboardCandlelight, CanChipFamily.Stm32G431, true);

                case (CanUsbIds.VendorProtofusion, CanUsbIds.ProductLegacyCanable1, 0x0300):
                    return new(CanFirmwareLine.DsdTechClassicSlcan, CanChipFamily.Stm32F072, false);

                case (CanUsbIds.VendorProtofusion, CanUsbIds.ProductLegacyCanable1, 0x0200):
                    return new(CanFirmwareLine.CanableClassicSlcan, CanChipFamily.Unknown, false);

                case (CanUsbIds.VendorMcs, CanUsbIds.ProductSlcan, 0x0200):
                    return new(CanFirmwareLine.CanableFdSlcan, CanChipFamily.Stm32G431, false);

                case (CanUsbIds.VendorMcs, CanUsbIds.ProductSlcan, 0x2608):
                    return new(CanFirmwareLine.ElmueMultiboardSlcan, CanChipFamily.Stm32G431, true);
            }

            return Unresolved;
        }

        if (string.IsNullOrEmpty(product))
        {
            return Unresolved;
        }

        return (vendorId, productId, product) switch
        {
            (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, "candleLight USB to CAN adapter")
                => new(CanFirmwareLine.CanableClassicCandlelight, CanChipFamily.Stm32F072, false),

            (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, "SH-C30x")
                => new(CanFirmwareLine.DsdTechClassicCandlelight, CanChipFamily.Stm32F072, true),

            (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, "sh-C30x")
                => new(CanFirmwareLine.DsdTechClassicCandlelight, CanChipFamily.Stm32F072, true),

            (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, "canable2 gs_usb")
                => new(CanFirmwareLine.CanableFdCandlelight, CanChipFamily.Stm32G431, false),

            (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, "SH-C31x")
                => new(CanFirmwareLine.DsdTechFdCandlelight, CanChipFamily.Stm32G431, true),

            (CanUsbIds.VendorOpenMoko, CanUsbIds.ProductCandlelight, ElmueCandlelightProductString)
                => new(CanFirmwareLine.ElmueMultiboardCandlelight, CanChipFamily.Stm32G431, true),

            (CanUsbIds.VendorMcs, CanUsbIds.ProductSlcan, ElmueSlcanProductString)
                => new(CanFirmwareLine.ElmueMultiboardSlcan, CanChipFamily.Stm32G431, true),

            _ => Unresolved,
        };
    }

    private static CanFirmwareFacts Unresolved =>
        new(CanFirmwareLine.Unknown, CanChipFamily.Unknown, false);

    // Identifies DSD TECH firmware.
    private static CanFirmwareFacts? DsdTechLine(ushort revision)
    {
        (CanFirmwareLine line, CanChipFamily chip) = (revision >> 8) switch
        {
            0x01 => (CanFirmwareLine.DsdTechClassicCandlelight, CanChipFamily.Stm32F072),
            0x02 => (CanFirmwareLine.DsdTechFdCandlelight, CanChipFamily.Stm32G431),
            _ => (CanFirmwareLine.Unknown, CanChipFamily.Unknown),
        };

        if (line == CanFirmwareLine.Unknown)
        {
            return null;
        }

        int low = revision & 0xFF;
        if (low == 0)
        {

            return new(line, chip, true);
        }

        int major = low >> 4, minor = low & 0x0F;
        return major <= 9 && minor <= 9
            ? new(line, chip, true, $"{major}.{minor}")
            : null;
    }

    // Gets the display name.
    public static string? NameOf(CanFirmwareLine line) => line switch
    {

        CanFirmwareLine.CanableClassicCandlelight => "Candlelight(CANable 1.0)",
        CanFirmwareLine.DsdTechClassicCandlelight => "Candlelight(DSD TECH)-v2.1",
        CanFirmwareLine.CanableFdCandlelight => "Candlelight(CANable 2.0)",
        CanFirmwareLine.DsdTechFdCandlelight => "Candlelight-FD(DSD TECH)-v1.4",

        CanFirmwareLine.DsdTechClassicSlcan => "SLCAN(DSD TECH)-v1.3",
        CanFirmwareLine.CanableClassicSlcan => "SLCAN(CANable 1.0)",

        CanFirmwareLine.CanableFdSlcan => "SLCAN(CANable 2.0)",
        CanFirmwareLine.ElmueMultiboardCandlelight => "Candlelight-FD(ElmueSoft)-2.5",
        CanFirmwareLine.ElmueMultiboardSlcan => "SLCAN-FD(ElmueSoft)-2.5",

        CanFirmwareLine.DsdTechC32Candlelight => "Candlelight-FD(ElmueSoft)-2.5",
        CanFirmwareLine.DsdTechC32Slcan => "SLCAN-FD(ElmueSoft)-2.5",
        _ => null,
    };

    // Gets the display name.
    public static string? NameOf(CanFirmwareLine line, string? version) => (line, version) switch
    {
        (_, null) => NameOf(line),
        (CanFirmwareLine.DsdTechClassicCandlelight, _) => $"Candlelight(DSD TECH)-v{version}",
        (CanFirmwareLine.DsdTechFdCandlelight, _) => $"Candlelight-FD(DSD TECH)-v{version}",
        _ => NameOf(line),
    };

    // Gets the adapter product URL.
    public static string? ProductPageFor(CanFirmwareLine line) => line switch
    {
        CanFirmwareLine.CanableClassicCandlelight => "https://github.com/dsdtech-official/can-adapters/tree/main/SH-C30A",
        CanFirmwareLine.CanableFdCandlelight => "https://github.com/dsdtech-official/can-adapters/tree/main/SH-C31A",
        _ => null,
    };

    // Gets the firmware guide URL.
    public static string? DfuGuidePageFor(CanFirmwareLine line) => line switch
    {
        CanFirmwareLine.DsdTechFdCandlelight => "https://github.com/dsdtech-official/can-adapters/tree/main/SH-C31A",
        _ => null,
    };
}
