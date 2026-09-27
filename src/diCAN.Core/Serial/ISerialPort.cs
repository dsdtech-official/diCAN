namespace DiCAN.Core.Serial;

// Manages i serial.
public interface ISerialPort : IAsyncDisposable
{

    string PortName { get; }

    SerialPortSettings Settings { get; }

    bool IsOpen { get; }

    event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    // Opens the requested resource.
    Task OpenAsync(CancellationToken cancellationToken = default);

    // Closes the active resource.
    Task CloseAsync(CancellationToken cancellationToken = default);

    // Writes output data.
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

// Stores event data.
public sealed class SerialChunkReceivedEventArgs(ReadOnlyMemory<byte> data, DateTimeOffset timestamp)
    : EventArgs
{

    public ReadOnlyMemory<byte> Data { get; } = data;

    public DateTimeOffset Timestamp { get; } = timestamp;
}

// Stores event data.
public sealed class SerialErrorEventArgs(
    string message,
    SerialErrorKind kind,
    bool isFatal = false,
    SerialLineError lineError = SerialLineError.None) : EventArgs
{

    public string Message { get; } = message;

    public SerialErrorKind Kind { get; } = kind;

    public bool IsFatal { get; } = isFatal;

    public SerialLineError LineError { get; } = lineError;
}

// Manages i serial.
public interface ISerialPortFactory
{

    // Creates the requested object.
    ISerialPort Create(string portName, SerialPortSettings settings);
}
