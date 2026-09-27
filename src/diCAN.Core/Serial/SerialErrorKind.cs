namespace DiCAN.Core.Serial;

// Manages serial error.
public enum SerialErrorKind
{

    Unknown = 0,

    PortNotFound,

    AccessDenied,

    DeviceRemoved,

    Timeout,

    InvalidSettings,

    LineError,
}

// Manages serial line.
[Flags]
public enum SerialLineError
{
    None = 0,
    Parity = 1 << 0,
    Framing = 1 << 1,

    Overrun = 1 << 2,
}
