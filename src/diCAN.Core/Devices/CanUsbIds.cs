namespace DiCAN.Core.Devices;

// Manages can usb ids.
public static class CanUsbIds
{

    public const ushort VendorOpenMoko = 0x1D50;

    public const ushort VendorMcs = 0x16D0;

    public const ushort VendorSt = 0x0483;

    public const ushort VendorProtofusion = 0xAD50;

    public const ushort ProductCandlelight = 0x606F;

    public const ushort ProductSlcan = 0x117E;

    public const ushort ProductLegacyCanable1 = 0x60C4;

    public const ushort ProductStmDfu = 0xDF11;

    // Classifies the input value.
    public static CanDeviceKind Classify(ushort vendorId, ushort productId) =>
        (vendorId, productId) switch
        {
            (VendorMcs, ProductSlcan) => CanDeviceKind.Slcan,
            (VendorOpenMoko, ProductCandlelight) => CanDeviceKind.Candlelight,
            (VendorSt, ProductStmDfu) => CanDeviceKind.StmBootloader,
            (VendorProtofusion, ProductLegacyCanable1) => CanDeviceKind.LegacyCanable1,
            _ => CanDeviceKind.Unknown,
        };

    public static IReadOnlyList<(ushort VendorId, ushort ProductId)> Known { get; } =
    [
        (VendorMcs, ProductSlcan),
        (VendorOpenMoko, ProductCandlelight),
        (VendorSt, ProductStmDfu),
        (VendorProtofusion, ProductLegacyCanable1),
    ];
}
