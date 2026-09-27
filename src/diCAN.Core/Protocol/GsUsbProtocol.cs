using System.Buffers.Binary;

namespace DiCAN.Core.Protocol;

// Manages gs usb protocol.
public static class GsUsbProtocol
{

    public const byte RequestHostFormat = 0;

    public const byte RequestBitTiming = 1;

    public const byte RequestMode = 2;

    public const byte RequestBtConst = 4;

    public const byte RequestDeviceConfig = 5;

    public const byte RequestDataBitTiming = 10;

    public const byte RequestBtConstExt = 11;

    public const byte RequestGetState = 14;

    public const uint ModeReset = 0;

    public const uint ModeStart = 1;

    public const uint FeatureListenOnly = 1u << 0;
    public const uint FeatureLoopBack = 1u << 1;
    public const uint FeatureTripleSample = 1u << 2;
    public const uint FeatureOneShot = 1u << 3;
    public const uint FeatureHardwareTimestamp = 1u << 4;
    public const uint FeatureIdentify = 1u << 5;
    public const uint FeaturePadPacketsToMaxSize = 1u << 7;
    public const uint FeatureCanFd = 1u << 8;
    public const uint FeatureBtConstExt = 1u << 10;
    public const uint FeatureBerrReporting = 1u << 12;
    public const uint FeatureGetState = 1u << 13;

    public const int ErrorFrameTransmitErrorIndex = 6;

    public const int ErrorFrameReceiveErrorIndex = 7;

    public const int ErrorFrameBusStatusIndex = 1;

    public const byte ErrorFlagsWarning = 0x04 | 0x08;

    public const byte ErrorFlagsPassive = 0x10 | 0x20;

    public const int ErrorIdBusOff = 0x0040;

    public const uint FeatureElmueProtocol = 1u << 14;

    public const uint ModeFlagsNeverRequested = FeatureElmueProtocol | FeatureHardwareTimestamp;

    public const uint EchoIdReceived = 0xFFFFFFFF;

    // Gets the transmit echo ID.
    public static uint EchoIdFor(uint counter) => 1 + (counter % 255);

    public const int HeaderSize = 12;

    public const int ClassicFrameSize = HeaderSize + 8;

    public const int ClassicTimestampFrameSize = ClassicFrameSize + 4;

    public const int FdFrameSize = HeaderSize + 64;

    public const int FdTimestampFrameSize = FdFrameSize + 4;

    public const uint CanIdExtendedFlag = 0x80000000;

    public const uint CanIdRemoteFlag = 0x40000000;

    public const uint CanIdErrorFlag = 0x20000000;

    public const uint CanIdMask = 0x1FFFFFFF;

    public const byte FrameFlagOverflow = 1 << 0;

    public const byte FrameFlagFd = 1 << 1;

    public const byte FrameFlagBitRateSwitch = 1 << 2;

    public const byte FrameFlagErrorStateIndicator = 1 << 3;

    // Gets frame size.
    public static int FrameSize(bool fd, bool timestamps) => (fd, timestamps) switch
    {
        (false, false) => ClassicFrameSize,
        (false, true) => ClassicTimestampFrameSize,
        (true, false) => FdFrameSize,
        (true, true) => FdTimestampFrameSize,
    };
}

// Stores gs usb frame data.
public readonly record struct GsUsbFrame(
    uint EchoId,
    CanFrame Frame,
    uint? DeviceTimestampMicroseconds)
{

    public bool IsReceived => EchoId == GsUsbProtocol.EchoIdReceived;

    public bool IsErrorFrame { get; init; }

    public CanBusStatus? BusStatus
    {
        get
        {
            if (!IsErrorFrame)
            {
                return null;
            }

            if ((Frame.Id & GsUsbProtocol.ErrorIdBusOff) != 0)
            {
                return CanBusStatus.BusOff;
            }

            ReadOnlySpan<byte> data = Frame.Data.Span;

            if (data.Length <= GsUsbProtocol.ErrorFrameBusStatusIndex)
            {
                return null;
            }

            byte flags = data[GsUsbProtocol.ErrorFrameBusStatusIndex];

            return (flags & GsUsbProtocol.ErrorFlagsPassive) != 0 ? CanBusStatus.ErrorPassive
                : (flags & GsUsbProtocol.ErrorFlagsWarning) != 0 ? CanBusStatus.Warning
                : CanBusStatus.Active;
        }
    }

    public GsUsbErrorCounters? ErrorCounters
    {
        get
        {
            if (!IsErrorFrame)
            {
                return null;
            }

            ReadOnlySpan<byte> data = Frame.Data.Span;

            return data.Length > GsUsbProtocol.ErrorFrameReceiveErrorIndex
                ? new GsUsbErrorCounters(
                    data[GsUsbProtocol.ErrorFrameTransmitErrorIndex],
                    data[GsUsbProtocol.ErrorFrameReceiveErrorIndex])
                : null;
        }
    }
}

// Manages gs usb.
public readonly record struct GsUsbErrorCounters(byte TransmitErrors, byte ReceiveErrors);

// Defines gs usb decode values.
public enum GsUsbDecode
{

    Ok,

    Incomplete,

    WrongRecordLayout,
}

// Manages gs usb codec.
public static class GsUsbCodec
{

    // Decodes the input data.
    public static GsUsbDecode Decode(
        ReadOnlySpan<byte> source, bool timestamps, bool fdRecords, out GsUsbFrame frame)
    {
        frame = default;

        int size = GsUsbProtocol.FrameSize(fdRecords, timestamps);

        if (source.Length < size)
        {
            return GsUsbDecode.Incomplete;
        }

        uint echoId = BinaryPrimitives.ReadUInt32LittleEndian(source);
        uint canId = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
        byte dlc = source[8];
        byte flags = source[10];

        bool fd = (flags & GsUsbProtocol.FrameFlagFd) != 0;

        if (fd && !fdRecords)
        {
            return GsUsbDecode.WrongRecordLayout;
        }

        bool remote = (canId & GsUsbProtocol.CanIdRemoteFlag) != 0;

        int payload = fd
            ? SlcanDlc.ToByteCount(dlc & 0x0F)
            : Math.Min((int)dlc, SlcanDlc.MaxClassicPayload);

        if (payload < 0 || GsUsbProtocol.HeaderSize + payload > size)
        {
            return GsUsbDecode.WrongRecordLayout;
        }

        byte[] data = remote || payload == 0
            ? []
            : source.Slice(GsUsbProtocol.HeaderSize, payload).ToArray();

        uint? timestamp = timestamps
            ? BinaryPrimitives.ReadUInt32LittleEndian(source[(size - 4)..])
            : null;

        frame = new GsUsbFrame(
            echoId,
            new CanFrame
            {
                Id = (int)(canId & GsUsbProtocol.CanIdMask),
                IsExtended = (canId & GsUsbProtocol.CanIdExtendedFlag) != 0,
                IsRemote = remote,
                IsFd = fd,
                IsBitRateSwitched = (flags & GsUsbProtocol.FrameFlagBitRateSwitch) != 0,
                IsErrorStateIndicated = (flags & GsUsbProtocol.FrameFlagErrorStateIndicator) != 0,
                RemoteLength = remote ? Math.Min((int)dlc, SlcanDlc.MaxClassicPayload) : 0,
                Data = data,
            },
            timestamp)
        {
            IsErrorFrame = (canId & GsUsbProtocol.CanIdErrorFlag) != 0,
        };

        return GsUsbDecode.Ok;
    }

    // Encodes the requested data.
    public static int Encode(
        CanFrame frame, uint echoId, bool timestamps, bool fdRecords, Span<byte> destination)
    {
        if (!frame.IsValid)
        {
            throw new ArgumentException("Frame flags or payload are not valid for CAN.", nameof(frame));
        }

        if (frame.IsFd && !fdRecords)
        {
            throw new ArgumentException(
                "A CAN FD frame cannot be sent on a session that was not opened for CAN FD.",
                nameof(frame));
        }

        int size = GsUsbProtocol.FrameSize(fdRecords, timestamps);

        if (destination.Length < size)
        {
            throw new ArgumentException(
                $"Need {size} bytes for this frame, got {destination.Length}.", nameof(destination));
        }

        destination[..size].Clear();

        uint canId = (uint)frame.Id & GsUsbProtocol.CanIdMask;

        if (frame.IsExtended)
        {
            canId |= GsUsbProtocol.CanIdExtendedFlag;
        }

        if (frame.IsRemote)
        {
            canId |= GsUsbProtocol.CanIdRemoteFlag;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, echoId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], canId);

        ReadOnlySpan<byte> data = frame.Data.Span;

        destination[8] = (byte)(frame.IsRemote
            ? frame.RemoteLength
            : frame.IsFd ? SlcanDlc.FromByteCount(data.Length) : data.Length);

        destination[9] = 0;

        byte flags = 0;

        if (frame.IsFd)
        {
            flags |= GsUsbProtocol.FrameFlagFd;
        }

        if (frame.IsBitRateSwitched)
        {
            flags |= GsUsbProtocol.FrameFlagBitRateSwitch;
        }

        destination[10] = flags;

        if (!frame.IsRemote && data.Length > 0)
        {
            data.CopyTo(destination[GsUsbProtocol.HeaderSize..]);
        }

        return size;
    }
}
