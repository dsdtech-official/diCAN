namespace DiCAN.Core.Protocol;

// Manages slcan nominal.
public enum SlcanNominalBitrate
{
    Kbit10 = '0',
    Kbit20 = '1',
    Kbit50 = '2',
    Kbit100 = '3',
    Kbit125 = '4',
    Kbit250 = '5',
    Kbit500 = '6',
    Kbit800 = '7',
    Mbit1 = '8',

    Kbit83_3 = '9',

    Kbit75 = 'A',

    Kbit62_5 = 'B',

    Kbit33_3 = 'C',

    Kbit5 = 'D',
}

// Manages slcan data.
public enum SlcanDataBitrate
{
    Kbit500 = '0',
    Mbit1 = '1',
    Mbit2 = '2',
    Mbit4 = '4',
    Mbit5 = '5',
    Mbit8 = '8',
}

// Manages slcan open.
public enum SlcanOpenMode
{

    Normal,

    Silent,

    InternalLoopback,

    ExternalLoopback,
}

// Defines slcan reply values.
public enum SlcanReply
{

    NotAReply = 0,

    Accepted,

    Rejected,

    RejectedWithReason,

    Text,
}

// Manages slcan commands.
public static class SlcanCommands
{

    public const char Terminator = '\r';

    public const char Bel = '\a';

    public const char ReplyPrefix = '#';

    public const char TextPrefix = '+';

    // Sets nominal bitrate.
    public static string SetNominalBitrate(SlcanNominalBitrate bitrate) => $"S{(char)bitrate}";

    // Sets data bitrate.
    public static string SetDataBitrate(SlcanDataBitrate bitrate) => $"Y{(char)bitrate}";

    // Opens the requested resource.
    public static string Open(SlcanOpenMode mode = SlcanOpenMode.Normal) => mode switch
    {
        SlcanOpenMode.Normal => "ON",
        SlcanOpenMode.Silent => "OS",
        SlcanOpenMode.InternalLoopback => "OI",
        SlcanOpenMode.ExternalLoopback => "OE",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    // Closes the active resource.
    public static string Close() => "C";

    // Queries the requested value.
    public static string QueryVersion() => "V";

    // Enables the selected feature.
    public static string EnableFeedback() => "MF";

    // Enables the selected feature.
    public static string EnableErrorReports() => "ME";

    // Configures automatic retries.
    public static string AutoRetransmit(bool on) => on ? "A1" : "A0";

    // Gets termination.
    public static string Termination(bool on) => on ? "MR" : "Mr";

    // Checks adapter capabilities.
    public static string ProbeUnknownCommand() => "QQ";

    // Classifies the input value.
    public static SlcanReply Classify(ReadOnlySpan<char> line, out char errorCode)
    {
        errorCode = '\0';

        if (line.Length == 0)
        {
            return SlcanReply.Accepted;
        }

        if (line[0] == Bel)
        {
            return SlcanReply.Rejected;
        }

        if (line[0] == TextPrefix)
        {
            return SlcanReply.Text;
        }

        if (line[0] != ReplyPrefix)
        {
            return SlcanReply.NotAReply;
        }

        if (line.Length == 1)
        {
            return SlcanReply.Accepted;
        }

        errorCode = line[1];
        return SlcanReply.RejectedWithReason;
    }

    // Reports an error.
    public static string ErrorKeyFor(char code) => code switch
    {
        '1' => "Slcan.Err.InvalidCommand",
        '2' => "Slcan.Err.InvalidParameter",
        '3' => "Slcan.Err.MustBeOpen",
        '4' => "Slcan.Err.MustBeClosed",
        '5' => "Slcan.Err.FirmwareInternal",
        '6' => "Slcan.Err.Unsupported",
        '7' => "Slcan.Err.SendBufferFull",
        '8' => "Slcan.Err.BusOff",
        '9' => "Slcan.Err.SilentMode",
        ':' => "Slcan.Err.NoBitrate",
        ';' => "Slcan.Err.OptionBytes",
        '<' => "Slcan.Err.ReplugRequired",
        '=' => "Slcan.Err.OutOfRange",
        _ => "Slcan.Err.Unrecognised",
    };

    // Describes the requested value.
    public static string DescribeError(char code) => code switch
    {
        '1' => "The command is not valid.",
        '2' => "One of the parameters is not valid.",
        '3' => "The adapter must be open first.",
        '4' => "The adapter must be closed first.",
        '5' => "The device firmware reported an internal error.",
        '6' => "This board does not support that feature.",
        '7' => "The device's send buffer is full.",
        '8' => "The CAN controller is bus-off and cannot transmit.",
        '9' => "Sending is not possible in listen only mode.",
        ':' => "No bitrate has been set, so the adapter cannot be opened.",
        ';' => "Programming the option bytes failed.",
        '<' => "Unplug and re-plug the USB cable to finish.",
        '=' => "A parameter is outside the valid range.",
        _ => $"The device reported an unrecognised error code '{code}'.",
    };
}
