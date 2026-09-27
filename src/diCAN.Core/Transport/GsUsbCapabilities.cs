using DiCAN.Core.Protocol;

namespace DiCAN.Core.Transport;

// Manages gs usb.
public sealed record GsUsbCapabilities(
    uint Features,
    GsUsbBitTimingLimits NominalLimits,
    GsUsbBitTimingLimits? DataLimits)
{

    public uint CanClockHz => NominalLimits.CanClockHz;

    public bool SupportsElmueProtocol => Has(GsUsbProtocol.FeatureElmueProtocol);

    public bool SupportsCanFd => Has(GsUsbProtocol.FeatureCanFd);

    public bool SupportsListenOnly => Has(GsUsbProtocol.FeatureListenOnly);

    public bool SupportsLoopback => Has(GsUsbProtocol.FeatureLoopBack);

    public bool SupportsOneShot => Has(GsUsbProtocol.FeatureOneShot);

    public bool SupportsHardwareTimestamp => Has(GsUsbProtocol.FeatureHardwareTimestamp);

    public bool SupportsErrorCounters => Has(GsUsbProtocol.FeatureGetState);

    public bool SupportsExtendedBitTiming => Has(GsUsbProtocol.FeatureBtConstExt);

    // Checks the requested state.
    public bool Has(uint feature) => (Features & feature) != 0;

    // Converts the requested value.
    public uint ToModeFlags(SlcanOpenMode mode, bool canFd, bool autoRetransmit)
    {
        uint wanted = mode switch
        {
            SlcanOpenMode.Normal => 0,
            SlcanOpenMode.Silent => GsUsbProtocol.FeatureListenOnly,

            SlcanOpenMode.InternalLoopback =>
                GsUsbProtocol.FeatureLoopBack | GsUsbProtocol.FeatureListenOnly,

            SlcanOpenMode.ExternalLoopback => GsUsbProtocol.FeatureLoopBack,

            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown bus mode."),
        };

        if (canFd)
        {
            wanted |= GsUsbProtocol.FeatureCanFd;
        }

        if (!autoRetransmit)
        {
            if (!SupportsOneShot)
            {
                throw new CanTransportNotSupportedException(
                    "Transport.GsUsb.NoOneShot",
                    "This device cannot switch off automatic retransmission: it does not offer "
                    + "gs_usb one-shot mode. Leave automatic retransmission on, or use an adapter "
                    + "that does.");
            }

            wanted |= GsUsbProtocol.FeatureOneShot;
        }

        uint missing = wanted & ~Features;

        if (missing != 0)
        {
            throw new NotSupportedException(
                $"This device does not support the {mode} bus mode (missing feature bits 0x{missing:X}).");
        }

        uint forbidden = wanted & GsUsbProtocol.ModeFlagsNeverRequested;

        if (forbidden != 0)
        {
            throw new InvalidOperationException(
                $"diCAN never requests these gs_usb mode bits (0x{forbidden:X}): they change the "
                + "wire format away from the one this transport implements.");
        }

        return wanted;
    }

    // Ensures can send.
    public void EnsureCanSend(CanFrame frame)
    {
        if (frame.IsFd && !SupportsCanFd)
        {
            throw new NotSupportedException(
                "This device has no CAN FD support, so a CAN FD frame cannot be sent.");
        }
    }
}
