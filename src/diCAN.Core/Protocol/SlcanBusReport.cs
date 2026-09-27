using System.Globalization;

namespace DiCAN.Core.Protocol;

// Manages slcan app.
[Flags]
public enum SlcanAppFlags
{

    None = 0,

    ReceiveFailed = 0x01,

    TransmitFailed = 0x02,

    TransmitOverflow = 0x04,

    UsbInOverflow = 0x08,

    TransmitTimeout = 0x10,
}

// Stores slcan bus report data.
public readonly record struct SlcanBusReport(
    CanBusStatus Status,
    byte ProtocolError,
    SlcanAppFlags Flags,
    byte TransmitErrors,
    byte ReceiveErrors)
{

    public bool BusIsHealthy =>
        Status == CanBusStatus.Active && TransmitErrors == 0 && ReceiveErrors == 0;

    // Tries parse.
    public static bool TryParse(string text, out SlcanBusReport report)
    {
        report = default;

        if (text.Length != 9 || (text[0] != 'E' && text[0] != 'e'))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];

        for (int i = 0; i < 4; i++)
        {
            if (!byte.TryParse(
                    text.AsSpan(1 + (i * 2), 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out bytes[i]))
            {
                return false;
            }
        }

        CanBusStatus status = (bytes[0] & 0xF0) switch
        {
            0x00 => CanBusStatus.Active,
            0x10 => CanBusStatus.Warning,
            0x20 => CanBusStatus.ErrorPassive,
            0x30 => CanBusStatus.BusOff,

            _ => CanBusStatus.Warning,
        };

        report = new SlcanBusReport(
            status,
            (byte)(bytes[0] & 0x0F),
            (SlcanAppFlags)bytes[1],
            bytes[2],
            bytes[3]);

        return true;
    }

    // Describes the requested value.
    public string Describe()
    {
        string counters = string.Create(
            CultureInfo.InvariantCulture, $"transmit errors {TransmitErrors}, receive errors {ReceiveErrors}");

        if (Status != CanBusStatus.Active)
        {

            string state = CanBusStatusText.Phrase(Status);

            return ProtocolError == AckError
                ? $"The CAN bus {state}. Nothing on the bus acknowledged the last frame ({counters})."
                : $"The CAN bus {state} ({counters}).";
        }

        string health = BusIsHealthy
            ? "the bus is healthy"
            : "the bus is operational but has recorded errors";

        if (Flags == SlcanAppFlags.None)
        {
            return $"The adapter reported a status change; {health} ({counters}).";
        }

        string what = Flags switch
        {
            SlcanAppFlags.TransmitOverflow =>
                "the adapter's send queue is full, so frames were refused rather than lost",
            SlcanAppFlags.UsbInOverflow =>
                "received frames outran USB, so the adapter dropped some before this host saw them",
            SlcanAppFlags.TransmitTimeout =>
                "a frame went unacknowledged for 500 ms and was abandoned; the send buffer was cleared",
            _ => $"the adapter reported {Flags}",
        };

        return $"{char.ToUpperInvariant(health[0])}{health[1..]} ({counters}), but {what}.";
    }

    private const byte AckError = 3;
}
