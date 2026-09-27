using System.Text;
using DiCAN.Core.Protocol;
using DiCAN.Core.Serial;
using DiCAN.Core.Transport;

namespace DiCAN.Infrastructure.Transport;

// Transfers SLCAN frames.
public sealed class LegacySlcanTransport : ICanTransport
{

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _versionTimeout;
    private readonly ISerialPort _port;
    private readonly SlcanLineSplitter _splitter = new();
    private readonly Lock _pendingLock = new();

    private TaskCompletionSource<string>? _pendingVersion;
    private int _disposed;

    // Initializes this instance.
    public LegacySlcanTransport(ISerialPort port, TimeSpan? versionTimeout = null)
    {
        _versionTimeout = versionTimeout ?? VersionTimeout;
        _port = port;
        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnPortError;
    }

    public string Description => Generation switch
    {
        SlcanFirmwareGeneration.Canable10 => $"CANable 1.0 on {_port.PortName}",
        SlcanFirmwareGeneration.Canable20 => $"CANable 2.0 on {_port.PortName}",
        _ => _port.PortName,
    };

    public bool IsOpen { get; private set; }

    public SlcanFirmwareGeneration Generation { get; private set; }

    public string? VersionResponse { get; private set; }

    public bool ConfigurationConfirmed => false;

    public CanBusConfiguration? RequestedConfiguration { get; private set; }

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;

    public event EventHandler<CanTransportErrorEventArgs>? Error;

    // Identifies connected devices.
    public async Task<SlcanFirmwareGeneration> IdentifyAsync(CancellationToken cancellationToken = default)
    {
        await _port.OpenAsync(cancellationToken);

        await SendRawAsync(SlcanCommands.Close(), cancellationToken);

        string answer = await QueryVersionAsync(cancellationToken);

        VersionResponse = answer.Length > 0 ? answer : null;
        Generation = SlcanFirmwareId.Identify(answer);

        if (Generation is SlcanFirmwareGeneration.Canable10 or SlcanFirmwareGeneration.Canable20)
        {
            return Generation;
        }

        string name = SlcanFirmwareId.ShortNameWithAnswer(Generation, VersionResponse);

        throw new CanTransportException(
            "Error.ContactSupport",
            $"Error 1006: diCAN picked the wrong channel for this adapter ({name}). Nothing was "
            + "opened, and the adapter itself is fine.",
            "1006");
    }

    // Opens the requested resource.
    public async Task OpenAsync(
        CanBusConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Generation is SlcanFirmwareGeneration.Unknown)
        {
            await IdentifyAsync(cancellationToken);
        }

        configuration.Validate();

        if (configuration.NominalSamplePoint is not null || configuration.DataSamplePoint is not null)
        {
            throw new CanTransportNotSupportedException(
                "Transport.Legacy.NoSamplePoint",
                "This adapter takes its bit timing from a firmware table, so the sample point "
                + "cannot be chosen. Leave it unset to use the adapter's own value.");
        }

        char nominal = SlcanBitrateTables.NominalCommandFor(Generation, configuration.NominalBitrate)
            ?? throw new CanTransportException(
                "Transport.Legacy.UnsupportedNominal",
                UnsupportedNominal(configuration.NominalBitrate),
                Describe(Generation),
                SlcanBitrateTables.Describe(configuration.NominalBitrate),
                SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedNominalRates(Generation)));

        char? data = null;

        if (configuration.DataBitrate is { } dataBitrate)
        {
            data = SlcanBitrateTables.DataCommandFor(Generation, dataBitrate)
                ?? throw new CanTransportException(
                    "Transport.Legacy.UnsupportedData",
                    UnsupportedData(dataBitrate),
                    Describe(Generation),
                    SlcanBitrateTables.Describe(dataBitrate),
                    SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedDataRates(Generation)));
        }

        char silent = LegacySilentFlag(configuration.Mode);

        await SendRawAsync(SlcanCommands.Close(), cancellationToken);

        await SendRawAsync(
            SlcanCommands.AutoRetransmit(configuration.AutoRetransmit), cancellationToken);

        await SendRawAsync($"S{nominal}", cancellationToken);

        if (data is { } dataCommand)
        {
            await SendRawAsync($"Y{dataCommand}", cancellationToken);
        }

        await SendRawAsync($"M{silent}", cancellationToken);

        await SendRawAsync("O", cancellationToken);

        RequestedConfiguration = configuration;
        IsOpen = true;
    }

    // Closes the active resource.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {

        try
        {
            if (_port.IsOpen)
            {
                await SendRawAsync(SlcanCommands.Close(), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new CanTransportErrorEventArgs(
                $"Leaving the bus failed: {ex.Message}", isFatal: false));
        }

        IsOpen = false;

        await _port.CloseAsync(cancellationToken);
        _splitter.Reset();

        CompletePendingVersion(string.Empty);
    }

    // Sends the requested data.
    public async Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!IsOpen)
        {
            throw new CanTransportException(
                "Transport.NotOnBus", "The adapter is not on the bus.");
        }

        if (frame.IsFd && !SlcanBitrateTables.SupportsCanFd(Generation))
        {
            throw new CanTransportNotSupportedException(
                "Transport.Legacy.NoFd",
                "This adapter is running CANable 1.0, which has no CAN FD support at all. " +
                "A CAN FD frame cannot be sent, and this firmware would neither send nor refuse it.");
        }

        await SendRawAsync(SlcanCodec.Encode(frame), cancellationToken);
    }

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnPortError;

        await CloseAsync();
        await _port.DisposeAsync();

        FrameReceived = null;
        Error = null;
    }

    // Gets the listen-only flag.
    private static char LegacySilentFlag(SlcanOpenMode mode) => mode switch
    {
        SlcanOpenMode.Silent => '1',
        SlcanOpenMode.Normal => '0',

        _ => throw new CanTransportNotSupportedException(
            "Transport.Legacy.NoSuchMode",
            $"The legacy firmware has no '{mode}' mode. It offers Normal and Silent only; " +
            "internal and external loopback are ElmueSoft 2.5 features.",
            mode),
    };

    // Reports unsupported bit rate.
    private string UnsupportedNominal(int requested) =>
        $"{Describe(Generation)} cannot do {SlcanBitrateTables.Describe(requested)}. It supports " +
        $"{SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedNominalRates(Generation))}. " +
        "⛔ Nothing was sent: this firmware ignores a rate it does not support and keeps the " +
        "previous one, without saying so, so guessing a near value would put the adapter on " +
        "the bus at a rate nobody chose.";

    // Reports unsupported data rate.
    private string UnsupportedData(int requested)
    {
        if (Generation is SlcanFirmwareGeneration.Canable10)
        {
            return "CANable 1.0 has no CAN FD support at all, so no data-phase rate can be set. " +
                   "Use a classic configuration, or reflash to CANable 2.5 if the board is an STM32G431.";
        }

        return $"CANable 2.0 cannot do a {SlcanBitrateTables.Describe(requested)} data phase. " +
               $"It has presets only: " +
               $"{SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedDataRates(Generation))}. " +
               "⛔ Nothing was sent.";
    }

    // Describes the requested value.
    private static string Describe(SlcanFirmwareGeneration generation) => generation switch
    {
        SlcanFirmwareGeneration.Canable10 => "CANable 1.0",
        SlcanFirmwareGeneration.Canable20 => "CANable 2.0",
        _ => "This adapter",
    };

    // Queries the requested value.
    private async Task<string> QueryVersionAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingLock)
        {
            _pendingVersion = completion;
        }

        try
        {
            await SendRawAsync(SlcanCommands.QueryVersion(), cancellationToken);

            return await completion.Task.WaitAsync(_versionTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
        finally
        {
            lock (_pendingLock)
            {
                if (ReferenceEquals(_pendingVersion, completion))
                {
                    _pendingVersion = null;
                }
            }
        }
    }

    // Sends raw.
    private Task SendRawAsync(string command, CancellationToken cancellationToken)
    {
        if (command.Length == 0)
        {
            throw new InvalidOperationException(
                "Refusing to send an empty line to a legacy adapter: a bare CR makes this firmware " +
                "retransmit the previous frame. If a caller needs a " +
                "no-op, it must send nothing at all rather than a terminator on its own.");
        }

        byte[] bytes = new byte[command.Length + 1];
        for (int i = 0; i < command.Length; i++)
        {
            bytes[i] = (byte)command[i];
        }

        bytes[^1] = (byte)SlcanCommands.Terminator;

        return _port.WriteAsync(bytes, cancellationToken);
    }

    // Handles data received.
    private void OnDataReceived(object? sender, SerialChunkReceivedEventArgs e)
    {
        foreach (SlcanLine line in _splitter.Append(e.Data.Span, e.Timestamp))
        {
            if (SlcanCodec.TryDecode(line.Text, line.CompletedAt, out CanFrame? frame))
            {
                FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame!, line.Text));
                continue;
            }

            if (line.Text.Length == 0)
            {
                continue;
            }

            if (!CompletePendingVersion(line.Text))
            {
                Error?.Invoke(this, new CanTransportErrorEventArgs(
                    $"The adapter said: {line.Text}", isFatal: false));
            }
        }
    }

    // Completes the pending work.
    private bool CompletePendingVersion(string text)
    {
        lock (_pendingLock)
        {
            if (_pendingVersion is null)
            {
                return false;
            }

            _pendingVersion.TrySetResult(text);
            _pendingVersion = null;
            return true;
        }
    }

    // Handles port error.
    private void OnPortError(object? sender, SerialErrorEventArgs e)
    {
        Error?.Invoke(this, new CanTransportErrorEventArgs(e.Message, e.IsFatal));

        if (e.IsFatal)
        {
            IsOpen = false;
            CompletePendingVersion(string.Empty);
        }
    }
}
