using DiCAN.Core.Protocol;
using DiCAN.Core.Serial;
using DiCAN.Core.Transport;

namespace DiCAN.Infrastructure.Transport;

// Manages slcan transport.
public sealed class SlcanTransport : ICanTransport
{

    private static readonly TimeSpan DefaultReplyTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _replyTimeout;
    private readonly ISerialPort _port;
    private readonly SlcanLineSplitter _splitter = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Lock _pendingLock = new();

    private TaskCompletionSource<SlcanCommandResult>? _pending;

    private bool _pendingAcceptsUnprefixed;

    private int _disposed;

    // Initializes this instance.
    public SlcanTransport(ISerialPort port, TimeSpan? replyTimeout = null)
    {
        _replyTimeout = replyTimeout ?? DefaultReplyTimeout;
        _port = port;
        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnPortError;
    }

    public string Description => DeviceInfo?.Board is { } board
        ? $"{board} on {_port.PortName}"
        : _port.PortName;

    public bool IsOpen { get; private set; }

    public SlcanDeviceInfo? DeviceInfo { get; private set; }

    public SlcanFirmwareGeneration Generation { get; private set; }

    public string? VersionResponse { get; private set; }

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;

    public event EventHandler<CanTransportErrorEventArgs>? Error;

    // Identifies connected devices.
    public async Task<SlcanDeviceInfo?> IdentifyAsync(CancellationToken cancellationToken = default)
    {
        await _port.OpenAsync(cancellationToken);

        await SendRawAsync(SlcanCommands.Close(), cancellationToken);

        SlcanCommandResult result = await TrySendCommandAsync(
            SlcanCommands.QueryVersion(), cancellationToken, acceptUnprefixedAnswer: true);

        VersionResponse = result.Text.Length > 0 ? result.Text : null;
        Generation = SlcanFirmwareId.Identify(result.Text);

        if (result.Reply == SlcanReply.Text &&
            SlcanDeviceInfo.TryParse(result.Text, out SlcanDeviceInfo? info))
        {
            DeviceInfo = info;
        }

        if (Generation is SlcanFirmwareGeneration.Elmue25)
        {
            await TrySendCommandAsync(SlcanCommands.EnableFeedback(), cancellationToken);
        }

        return DeviceInfo;
    }

    // Checks adapter capabilities.
    public async Task<bool> ProbeIsElmueFirmwareAsync(CancellationToken cancellationToken = default)
    {
        SlcanCommandResult result = await TrySendCommandAsync(
            SlcanCommands.ProbeUnknownCommand(), cancellationToken);

        return result.Reply is not SlcanReply.NotAReply;
    }

    // Opens the requested resource.
    public async Task OpenAsync(CanBusConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (IsOpen)
        {
            return;
        }

        if (DeviceInfo is null)
        {
            await IdentifyAsync(cancellationToken);
        }

        if (Generation is not SlcanFirmwareGeneration.Elmue25)
        {

            string name = SlcanFirmwareId.ShortNameWithAnswer(Generation, VersionResponse);

            throw new CanTransportException(
                "Error.ContactSupport",
                $"Error 1006: diCAN picked the wrong channel for this adapter ({name}). Nothing "
                + "was opened, and the adapter itself is fine.",
                "1006");
        }

        configuration.Validate();

        await SendCommandOrThrowAsync(
            SlcanCommands.AutoRetransmit(configuration.AutoRetransmit), cancellationToken);

        if (configuration.NominalSamplePoint is not null || configuration.DataSamplePoint is not null)
        {
            await SendComputedTimingAsync(configuration, cancellationToken);
        }
        else
        {

            char nominal = SlcanBitrateTables.NominalCommandFor(Generation, configuration.NominalBitrate)
                ?? throw new CanTransportException(
                    "Transport.UnsupportedNominal",
                    UnsupportedNominal(configuration.NominalBitrate),
                    SlcanBitrateTables.Describe(configuration.NominalBitrate),
                    SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedNominalRates(Generation)));

            char? data = null;

            if (configuration.DataBitrate is { } dataBitrate)
            {
                data = SlcanBitrateTables.DataCommandFor(Generation, dataBitrate)
                    ?? throw new CanTransportException(
                        "Transport.UnsupportedData",
                        UnsupportedData(dataBitrate),
                        SlcanBitrateTables.Describe(dataBitrate),
                        SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedDataRates(Generation)));
            }

            await SendCommandOrThrowAsync($"S{nominal}", cancellationToken);

            if (data is { } dataCommand)
            {
                await SendCommandOrThrowAsync($"Y{dataCommand}", cancellationToken);
            }
        }

        await TrySendCommandAsync(SlcanCommands.EnableErrorReports(), cancellationToken);

        await SendCommandOrThrowAsync(SlcanCommands.Open(configuration.Mode), cancellationToken);

        IsOpen = true;
    }

    // Sends computed timing.
    private async Task SendComputedTimingAsync(
        CanBusConfiguration configuration, CancellationToken cancellationToken)
    {
        await SendOnePhaseAsync(
            dataPhase: false,
            configuration.NominalBitrate,
            configuration.NominalSamplePoint ?? GsUsbBitTimingSolver.DefaultSamplePoint,
            cancellationToken);

        if (configuration.DataBitrate is { } dataBitrate)
        {
            await SendOnePhaseAsync(
                dataPhase: true,
                dataBitrate,
                configuration.DataSamplePoint ?? GsUsbBitTimingSolver.DataPhaseSamplePoint,
                cancellationToken);
        }
    }

    // Sends one phase.
    private async Task SendOnePhaseAsync(
        bool dataPhase, int bitsPerSecond, double samplePoint, CancellationToken cancellationToken)
    {
        string phase = dataPhase ? "data" : "arbitration";

        string phaseKey = dataPhase ? "Transport.Phase.Data" : "Transport.Phase.Arbitration";

        if (!SlcanBitTiming.TryLimits(DeviceInfo, dataPhase, out GsUsbBitTimingLimits limits))
        {

            throw new CanTransportException(
                "Transport.NoTimingLimits",

                $"This adapter did not report the CAN clock and bit timing limits, so the "
                + $"{phase} sample point cannot be computed for it. Leave the sample point unset "
                + $"to use the adapter's own timing table.",
                phaseKey);
        }

        if (!GsUsbBitTimingSolver.TrySolve(limits, bitsPerSecond, out GsUsbBitTiming timing, samplePoint))
        {

            throw new CanTransportException(
                "Transport.NoExactTiming",
                $"This adapter's {limits.CanClockHz / 1_000_000} MHz CAN clock cannot produce "
                + $"{bitsPerSecond} bit/s exactly in the {phase} phase, so no bit timing exists "
                + $"for it.",
                limits.CanClockHz / 1_000_000,
                bitsPerSecond,
                phaseKey);
        }

        string command = dataPhase
            ? SlcanBitTiming.SetDataBitTiming(timing)
            : SlcanBitTiming.SetNominalBitTiming(timing);

        await SendCommandOrThrowAsync(command, cancellationToken);
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

        CompletePending(new SlcanCommandResult(SlcanReply.NotAReply, '\0', string.Empty));
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

    // Sends command or throw.
    private async Task SendCommandOrThrowAsync(string command, CancellationToken cancellationToken)
    {
        SlcanCommandResult result = await TrySendCommandAsync(command, cancellationToken);

        switch (result.Reply)
        {
            case SlcanReply.Accepted:
            case SlcanReply.Text:
                return;

            case SlcanReply.RejectedWithReason:

                throw new CanTransportException(
                    "Transport.RejectedWithReason",
                    $"The adapter rejected '{command}'. {SlcanCommands.DescribeError(result.ErrorCode)}",
                    command,
                    SlcanCommands.ErrorKeyFor(result.ErrorCode));

            case SlcanReply.Rejected:
                throw new CanTransportException(
                    "Transport.Rejected",
                    $"The adapter rejected '{command}' without giving a reason.",
                    command);

            default:

                throw new CanTransportException(
                    "Transport.NoAnswer",
                    $"The adapter did not answer '{command}' within {_replyTimeout.TotalSeconds:0.#} s. " +
                    "Every slcan firmware generation answers its commands or its version query, so " +
                    "this port is probably not an slcan adapter.",
                    command,
                    _replyTimeout.TotalSeconds);
        }
    }

    // Tries send command.
    private async Task<SlcanCommandResult> TrySendCommandAsync(
        string command, CancellationToken cancellationToken, bool acceptUnprefixedAnswer = false)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var completion = new TaskCompletionSource<SlcanCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_pendingLock)
            {
                _pending = completion;
                _pendingAcceptsUnprefixed = acceptUnprefixedAnswer;
            }

            try
            {
                await SendRawAsync(command, cancellationToken);

                return await completion.Task.WaitAsync(_replyTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return new SlcanCommandResult(SlcanReply.NotAReply, '\0', string.Empty);
            }
            finally
            {
                lock (_pendingLock)
                {

                    if (ReferenceEquals(_pending, completion))
                    {
                        _pending = null;
                        _pendingAcceptsUnprefixed = false;
                    }
                }
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    // Sends raw.
    private Task SendRawAsync(string command, CancellationToken cancellationToken)
    {

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

            HandleNonFrameLine(line.Text);
        }
    }

    // Handles non frame line.
    private void HandleNonFrameLine(string text)
    {
        SlcanReply reply = SlcanCommands.Classify(text, out char errorCode);

        switch (reply)
        {
            case SlcanReply.NotAReply:

                if (_pending is not null && _pendingAcceptsUnprefixed)
                {
                    break;
                }

                ReportDeviceLine(text);
                return;

            case SlcanReply.Rejected when _pending is null:
            case SlcanReply.RejectedWithReason when _pending is null:

                Error?.Invoke(this, new CanTransportErrorEventArgs(
                    errorCode == '\0'
                        ? "The adapter rejected something it was sent."
                        : SlcanCommands.DescribeError(errorCode),
                    isFatal: false));
                return;
        }

        CompletePending(new SlcanCommandResult(reply, errorCode, text));
    }

    // Reports the requested event.
    private void ReportDeviceLine(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        switch (text[0])
        {
            case 'E':

                if (SlcanBusReport.TryParse(text, out SlcanBusReport report))
                {
                    string reading = report.Describe();

                    Error?.Invoke(this, new CanTransportErrorEventArgs(
                        $"{reading} [{text}]", isFatal: false, collapseKey: reading));

                    return;
                }

                Error?.Invoke(this, new CanTransportErrorEventArgs(
                    $"The adapter sent a status report this host cannot read: {text}",
                    isFatal: false));
                return;

            case '>':

                return;

            default:
                return;
        }
    }

    // Handles port error.
    private void OnPortError(object? sender, SerialErrorEventArgs e)
    {
        if (e.IsFatal)
        {
            IsOpen = false;

            CompletePending(new SlcanCommandResult(SlcanReply.NotAReply, '\0', string.Empty));
        }

        Error?.Invoke(this, new CanTransportErrorEventArgs(e.Message, e.IsFatal));
    }

    // Completes the pending work.
    private void CompletePending(SlcanCommandResult result)
    {
        TaskCompletionSource<SlcanCommandResult>? pending;

        lock (_pendingLock)
        {
            pending = _pending;
            _pending = null;
        }

        pending?.TrySetResult(result);
    }

    // Reports unsupported bit rate.
    private string UnsupportedNominal(int requested) =>
        $"This adapter cannot do {SlcanBitrateTables.Describe(requested)}. It supports " +
        $"{SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedNominalRates(Generation))}. " +
        "⛔ Nothing was sent.";

    // Reports unsupported data rate.
    private string UnsupportedData(int requested) =>
        $"This adapter cannot do a {SlcanBitrateTables.Describe(requested)} data phase. It supports " +
        $"{SlcanBitrateTables.Describe(SlcanBitrateTables.SupportedDataRates(Generation))}. " +
        "⚠ The board's transceiver may be a lower ceiling still than this list. ⛔ Nothing was sent.";

    // Manages slcan command.
    private readonly record struct SlcanCommandResult(SlcanReply Reply, char ErrorCode, string Text);
}
