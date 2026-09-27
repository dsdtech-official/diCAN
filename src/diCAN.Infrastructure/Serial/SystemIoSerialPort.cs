using System.IO.Ports;
using DiCAN.Core.Abstractions;
using DiCAN.Core.Serial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiCAN.Infrastructure.Serial;

// Manages system io serial port.
public sealed class SystemIoSerialPort : ISerialPort
{
    private const int ReadBufferSize = 4096;

    private static readonly TimeSpan ReadLoopUnwindTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IMonotonicClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;

    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;

    private int _disposed;

    private SerialLineError _lineErrorsSeen = SerialLineError.None;

    // Initializes this instance.
    public SystemIoSerialPort(
        string portName,
        SerialPortSettings settings,
        IMonotonicClock clock,
        ILogger? logger = null)
    {
        PortName = portName;
        Settings = settings;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
    }

    public string PortName { get; }

    public SerialPortSettings Settings { get; }

    public bool IsOpen => _port?.IsOpen == true;

    public event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    public event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    // Opens the requested resource.
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsOpen)
            {
                return;
            }

            ThrowIfDeviceNodeIsMissing();

            var port = new SerialPort(PortName)
            {

                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = Settings.WriteTimeoutMs,

                ReadBufferSize = 1 << 16,
            };

            try
            {
                Apply(port, Settings);
                port.Open();
            }
            catch (Exception ex)
            {

                _logger.LogWarning(
                    ex, "Opening {Port} failed: {ExceptionType}", PortName, ex.GetType().Name);
                port.Dispose();
                throw;
            }

            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            _lineErrorsSeen = SerialLineError.None;
            port.ErrorReceived += OnPortErrorReceived;

            await CollectStaleReadLoopAsync();

            _port = port;
            _readCts = new CancellationTokenSource();
            _readLoop = Task.Run(() => ReadLoopAsync(port, _readCts.Token), CancellationToken.None);

            _logger.LogInformation("Opened {Port} at {Baud} baud", PortName, Settings.BaudRate);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Closes the active resource.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_port is null && _readCts is null)
            {
                return;
            }

            _readCts?.Cancel();

            SerialPort? port = Interlocked.Exchange(ref _port, null);

            if (port is not null)
            {
                port.ErrorReceived -= OnPortErrorReceived;

                try
                {
                    if (port.IsOpen)
                    {
                        port.Close();
                    }
                }
                catch (Exception ex)
                {

                    _logger.LogDebug(
                        ex, "Closing {Port} failed: {ExceptionType}", PortName, ex.GetType().Name);
                }
            }

            bool timedOut = await AwaitReadLoopAsync();

            port?.Dispose();

            if (timedOut)
            {
                _logger.LogWarning("Read loop on {Port} did not unwind within the timeout", PortName);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Writes output data.
    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {

            SerialPort? port = _port;
            if (port is null || !port.IsOpen)
            {
                throw new InvalidOperationException($"Port {PortName} is not open.");
            }

            try
            {
                await port.BaseStream.WriteAsync(data, cancellationToken);
                await port.BaseStream.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Writing {Count} bytes to {Port} failed: {ExceptionType}",
                    data.Length, PortName, ex.GetType().Name);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await CloseAsync();

        DataReceived = null;
        ErrorReceived = null;

    }

    // Reports an operation error.
    private void ThrowIfDeviceNodeIsMissing()
    {
        if (OperatingSystem.IsWindows() || File.Exists(PortName))
        {
            return;
        }

        throw new FileNotFoundException($"Could not find the serial port '{PortName}'.", PortName);
    }

    // Applies the requested changes.
    private static void Apply(SerialPort port, SerialPortSettings settings)
    {
        port.BaudRate = settings.BaudRate;

        port.DataBits = 8;
        port.Parity = Parity.None;
        port.StopBits = StopBits.One;
        port.Handshake = Handshake.None;

        port.DtrEnable = settings.AssertDtr;
        port.RtsEnable = settings.AssertRts;
    }

    // Gets dispose dead port.
    private void DisposeDeadPort(SerialPort dead)
    {
        Interlocked.CompareExchange(ref _port, null, dead);

        dead.ErrorReceived -= OnPortErrorReceived;

        try
        {
            dead.Dispose();
        }
        catch (Exception ex)
        {

            _logger.LogDebug(
                ex, "Disposing the dead port {Port} threw {ExceptionType}", PortName, ex.GetType().Name);
        }
    }

    // Handles port error received.
    private void OnPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        SerialLineError flags = MapLineError(e.EventType);
        if (flags == SerialLineError.None)
        {

            return;
        }

        bool firstOfItsKind = (_lineErrorsSeen & flags) == SerialLineError.None;
        _lineErrorsSeen |= flags;

        if (!firstOfItsKind)
        {
            return;
        }

        _logger.LogWarning("Line error on {Port}: {Error}", PortName, e.EventType);

        ErrorReceived?.Invoke(this, new SerialErrorEventArgs(
            $"Serial line error on {PortName}: {e.EventType}",
            SerialErrorKind.LineError,
            isFatal: false,
            lineError: flags));
    }

    // Maps the requested value.
    private static SerialLineError MapLineError(SerialError error) => error switch
    {
        SerialError.RXParity => SerialLineError.Parity,
        SerialError.Frame => SerialLineError.Framing,

        SerialError.Overrun => SerialLineError.Overrun,
        SerialError.RXOver => SerialLineError.Overrun,

        _ => SerialLineError.None,
    };

    // Reports the requested event.
    private void ReportReadLoopStop(Exception ex, SerialPort port, CancellationToken token, long reads, long bytes)
    {
        SerialErrorKind? kind = SerialErrorClassifier.ClassifyReadLoopStop(ex, token.IsCancellationRequested);

        if (kind is null)
        {
            _logger.LogInformation(
                "Read loop on {Port} exited after {Reads} reads, {Bytes} bytes", PortName, reads, bytes);
            return;
        }

        _logger.LogError(
            ex, "Read loop on {Port} faulted after {Reads} reads, {Bytes} bytes", PortName, reads, bytes);

        DisposeDeadPort(port);

        ErrorReceived?.Invoke(this, new SerialErrorEventArgs(ex.Message, kind.Value, isFatal: true));
    }

    // Reads loop.
    private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
    {
        byte[] buffer = new byte[ReadBufferSize];
        long reads = 0, bytes = 0;

        Stream stream;
        try
        {

            stream = port.BaseStream;
        }
        catch (Exception ex)
        {
            ReportReadLoopStop(ex, port, token, reads, bytes);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            int count;
            try
            {
                count = await stream.ReadAsync(buffer, token);
            }

            catch (Exception ex)
            {
                ReportReadLoopStop(ex, port, token, reads, bytes);
                return;
            }

            DateTimeOffset timestamp = _clock.Now;

            if (count <= 0)
            {
                continue;
            }

            reads++;
            bytes += count;

            byte[] payload = new byte[count];
            buffer.AsSpan(0, count).CopyTo(payload);
            DataReceived?.Invoke(this, new SerialChunkReceivedEventArgs(payload, timestamp));
        }

        _logger.LogInformation(
            "Read loop on {Port} exited after {Reads} reads, {Bytes} bytes", PortName, reads, bytes);
    }

    // Waits for the read loop.
    private async Task<bool> AwaitReadLoopAsync()
    {
        if (_readCts is null)
        {
            return false;
        }

        bool timedOut = false;

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(ReadLoopUnwindTimeout);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                _logger.LogDebug(ex, "Waiting for the read loop on {Port} timed out", PortName);
                timedOut = true;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Read loop on {Port} faulted while stopping", PortName);
            }
        }

        _readCts.Dispose();
        _readCts = null;
        _readLoop = null;

        return timedOut;
    }

    // Collects completed work.
    private async Task CollectStaleReadLoopAsync()
    {
        if (Interlocked.Exchange(ref _readCts, null) is not { } stale)
        {
            return;
        }

        Task? staleLoop = _readLoop;
        _readLoop = null;

        if (staleLoop is not null)
        {
            try
            {
                await staleLoop.WaitAsync(ReadLoopUnwindTimeout);
            }
            catch (Exception ex)
            {

                _logger.LogWarning(
                    ex, "A stale read loop on {Port} was still running at reconnect", PortName);
            }
        }

        stale.Dispose();
    }
}

// Manages serial port.
public sealed class SerialPortFactory(IMonotonicClock clock, ILogger<SystemIoSerialPort>? logger = null)
    : ISerialPortFactory
{

    // Creates the requested object.
    public ISerialPort Create(string portName, SerialPortSettings settings) =>
        new SystemIoSerialPort(portName, settings, clock, logger);
}
