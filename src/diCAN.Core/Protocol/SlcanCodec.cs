using System.Globalization;

namespace DiCAN.Core.Protocol;

// Manages slcan codec.
public static class SlcanCodec
{

    public const char Terminator = '\r';

    public const char Rejected = '\a';

    // Tries decode.
    public static bool TryDecode(ReadOnlySpan<char> line, DateTimeOffset timestamp, out CanFrame? frame)
    {
        frame = null;

        if (line.Length < 2)
        {
            return false;
        }

        if (!TryReadPrefix(line[0], out bool extended, out bool remote, out bool fd, out bool brs))
        {
            return false;
        }

        int idLength = extended ? 8 : 3;
        if (line.Length < 1 + idLength + 1)
        {
            return false;
        }

        if (!int.TryParse(line.Slice(1, idLength), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id))
        {
            return false;
        }

        if (!TryReadNibble(line[1 + idLength], out int dlcCode))
        {
            return false;
        }

        int byteCount = remote ? 0 : SlcanDlc.ToByteCount(dlcCode);
        if (byteCount < 0)
        {
            return false;
        }

        ReadOnlySpan<char> payload = line[(2 + idLength)..];

        if (payload.Length != byteCount * 2)
        {
            return false;
        }

        byte[] data = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            if (!TryReadNibble(payload[i * 2], out int hi) || !TryReadNibble(payload[(i * 2) + 1], out int lo))
            {
                return false;
            }

            data[i] = (byte)((hi << 4) | lo);
        }

        frame = new CanFrame
        {
            Id = id,
            IsExtended = extended,
            IsRemote = remote,
            IsFd = fd,
            IsBitRateSwitched = brs,
            RemoteLength = remote ? dlcCode : 0,
            Data = data,
            Timestamp = timestamp,
        };

        return frame.IsValid;
    }

    // Encodes the requested data.
    public static string Encode(CanFrame frame)
    {
        if (!frame.IsValid)
        {
            throw new ArgumentException("Frame flags or payload are not valid for CAN.", nameof(frame));
        }

        char prefix = (frame.IsFd, frame.IsRemote, frame.IsBitRateSwitched) switch
        {
            (true, _, true) => 'b',
            (true, _, false) => 'd',
            (false, true, _) => 'r',
            (false, false, _) => 't',
        };

        if (frame.IsExtended)
        {
            prefix = char.ToUpperInvariant(prefix);
        }

        int dlcCode = frame.IsRemote
            ? frame.RemoteLength
            : SlcanDlc.FromByteCount(frame.Data.Length);

        int onWire = frame.IsRemote ? 0 : SlcanDlc.ToByteCount(dlcCode);

        var text = new System.Text.StringBuilder(2 + (frame.IsExtended ? 8 : 3) + (onWire * 2));
        text.Append(prefix);
        text.Append(frame.Id.ToString(frame.IsExtended ? "X8" : "X3", CultureInfo.InvariantCulture));
        text.Append(dlcCode.ToString("X1", CultureInfo.InvariantCulture));

        ReadOnlySpan<byte> data = frame.Data.Span;
        for (int i = 0; i < onWire; i++)
        {
            text.Append((i < data.Length ? data[i] : (byte)0).ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    // Tries read prefix.
    private static bool TryReadPrefix(char prefix, out bool extended, out bool remote, out bool fd, out bool brs)
    {
        extended = char.IsUpper(prefix);
        remote = false;
        fd = false;
        brs = false;

        switch (char.ToLowerInvariant(prefix))
        {
            case 't':
                return true;
            case 'r':
                remote = true;
                return true;
            case 'd':
                fd = true;
                return true;
            case 'b':
                fd = true;
                brs = true;
                return true;
            default:
                extended = false;
                return false;
        }
    }

    // Tries read nibble.
    private static bool TryReadNibble(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => -1,
        };

        return value >= 0;
    }
}
