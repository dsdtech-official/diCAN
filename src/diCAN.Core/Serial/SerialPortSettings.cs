namespace DiCAN.Core.Serial;

// Manages serial port.
public sealed record SerialPortSettings
{

    public int BaudRate { get; init; } = 921_600;

    public bool AssertDtr { get; init; }

    public bool AssertRts { get; init; }

    public int WriteTimeoutMs { get; init; } = 2_000;
}
