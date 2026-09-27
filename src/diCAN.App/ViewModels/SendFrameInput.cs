using System.Globalization;
using DiCAN.Core.Protocol;

namespace DiCAN.App.ViewModels;

// Manages send frame input.
public static class SendFrameInput
{

    // Tries read id.
    public static bool TryReadId(string idText, out int value)
    {
        value = 0;

        string id = idText.Trim();

        if (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            id = id[2..];
        }

        return id.Length > 0
            && int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    // Parses input data.
    public static CanFrame Parse(
        string idText,
        string dataText,
        bool extended,
        bool fd = false,
        bool bitRateSwitched = false,
        bool remote = false,
        string? remoteLengthText = null)
    {
        if (!TryReadId(idText, out int value))
        {
            throw new SendInputFormatException(
                "SendError.IdNotHex", $"'{idText}' is not a hexadecimal CAN id.", idText);
        }

        int max = extended ? 0x1FFFFFFF : 0x7FF;

        if (value > max)
        {

            throw new SendInputRangeException(
                "SendId",
                extended ? "SendError.IdTooLarge" : "SendError.IdTooLargeStandard",

                $"{value:X} is larger than {max:X}, the largest {(extended ? "29" : "11")} bit id."
                + (extended
                    ? string.Empty
                    : " Write the id as 8 hex digits to make it a 29 bit one."),
                $"{value:X}",
                $"{max:X}");
        }

        if (remote && fd)
        {
            throw new SendInputArgumentException(
                nameof(remote),
                "SendError.FdHasNoRemote",
                "CAN FD has no remote frames. Clear either FD or Remote.");
        }

        if (bitRateSwitched && !fd)
        {
            throw new SendInputArgumentException(
                nameof(bitRateSwitched),
                "SendError.BrsNeedsFd",
                "Bit rate switching only exists in CAN FD. Tick FD, or clear BRS.");
        }

        if (remote)
        {
            return ParseRemote(value, extended, dataText, remoteLengthText);
        }

        string hex = dataText.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

        if (hex.Length % 2 != 0)
        {
            throw new SendInputFormatException(
                "SendError.OddHexDigits",
                "Payload needs an even number of hex digits, two per byte.");
        }

        byte[] data;

        try
        {
            data = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new SendInputFormatException(
                "SendError.DataNotHex", $"'{dataText}' is not hexadecimal.", dataText);
        }

        int limit = fd ? SlcanDlc.MaxPayload : SlcanDlc.MaxClassicPayload;

        if (data.Length > limit)
        {

            throw new SendInputRangeException(
                "SendData",
                fd ? "SendError.DataTooLongFd" : "SendError.DataTooLongClassic",
                fd
                    ? $"{data.Length} bytes is more than the {SlcanDlc.MaxPayload} a CAN FD frame carries."
                    : $"{data.Length} bytes is more than the {SlcanDlc.MaxClassicPayload} a classic "
                      + "CAN frame carries. Tick FD for longer payloads.",
                data.Length,
                limit);
        }

        if (fd && !SlcanDlc.IsExactLength(data.Length))
        {
            throw new SendInputRangeException(
                "SendData",
                "SendError.NoSuchFdLength",
                $"CAN FD cannot carry exactly {data.Length} bytes. The lengths it can carry are "
                + "0 to 8, 12, 16, 20, 24, 32, 48 and 64.",
                data.Length);
        }

        return new CanFrame
        {
            Id = value,
            IsExtended = extended,
            Data = data,
            IsFd = fd,
            IsBitRateSwitched = bitRateSwitched,
        };
    }

    // Parses remote.
    private static CanFrame ParseRemote(
        int id, bool extended, string dataText, string? remoteLengthText)
    {
        if (dataText.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().Length > 0)
        {
            throw new SendInputArgumentException(
                "SendData",
                "SendError.RemoteHasNoData",
                "A remote frame carries no data. Clear the payload, or clear Remote.");
        }

        string text = (remoteLengthText ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new SendInputFormatException(
                "SendError.RemoteLengthMissing",
                $"A remote frame needs the number of bytes it is requesting, 0 to "
                + $"{SlcanDlc.MaxClassicPayload}.",
                SlcanDlc.MaxClassicPayload);
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int length))
        {
            throw new SendInputFormatException(
                "SendError.RemoteLengthNotNumber",
                $"'{remoteLengthText}' is not a whole number of bytes.",
                remoteLengthText);
        }

        if (length < 0 || length > SlcanDlc.MaxClassicPayload)
        {
            throw new SendInputRangeException(
                "SendRemoteLength",
                "SendError.RemoteLengthTooLarge",
                $"A remote frame can request 0 to {SlcanDlc.MaxClassicPayload} bytes, not {length}.",
                SlcanDlc.MaxClassicPayload,
                length);
        }

        return new CanFrame
        {
            Id = id,
            IsExtended = extended,
            Data = ReadOnlyMemory<byte>.Empty,
            IsRemote = true,
            RemoteLength = length,
        };
    }
}
