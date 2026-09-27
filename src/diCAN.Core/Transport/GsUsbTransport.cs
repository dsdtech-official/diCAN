using System.Buffers.Binary;
using System.Globalization;
using DiCAN.Core.Protocol;

namespace DiCAN.Core.Transport;

// Manages gs usb transport.
public sealed class GsUsbTransport(IUsbBulkDevice device, string description) : ICanTransport
{

    private const uint HostFormatLittleEndian = 0x0000BEEF;

    private const ushort Channel = 0;

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(1);

    public const int MaxFramesInFlight = 30;

    private static readonly TimeSpan SendSlotTimeout = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _sending;

    private CancellationTokenSource? _loop;
    private Task? _reader;
    private uint _nextEchoId;
    private bool _timestamps;

    private static readonly TimeSpan StrayEchoQuietWindow = TimeSpan.FromSeconds(1);

    private int _strayEchoes;
    private string? _strayFirstDetail;
    private uint _strayFirstEchoId;
    private uint _strayLastEchoId;

    private int _strayLowestFrameId;
    private int _strayHighestFrameId;
    private long _strayOutstanding;
    private DateTimeOffset _strayFirstAt;
    private DateTimeOffset _strayLastAt;

    private bool _fdRecords;

    public string Description { get; } = description;

    public bool IsOpen { get; private set; }

    public GsUsbCapabilities? Capabilities { get; private set; }

    public long OutstandingEchoes => Interlocked.Read(ref _outstandingEchoes);

    internal Action<uint>? EchoObserved { get; set; }

    internal Action<GsUsbFrame>? RecordObserved { get; set; }

    internal Func<uint, uint>? EchoIdStrategy { get; set; }

    internal Action<int>? TransferObserved { get; set; }

    internal Action<byte[]>? ModeWriteObserved { get; set; }

    private long _outstandingEchoes;

    private SemaphoreSlim? _sendSlots;

    public long ErrorFrames => Interlocked.Read(ref _errorFrames);

    private long _errorFrames;

    public GsUsbErrorCounters? LastErrorCounters
    {
        get
        {
            long packed = Interlocked.Read(ref _lastErrorCounters);

            return packed < 0
                ? null
                : new GsUsbErrorCounters((byte)(packed >> 8), (byte)packed);
        }
    }

    private long _lastErrorCounters = -1;

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;

    public event EventHandler<CanTransportErrorEventArgs>? Error;

    // Opens the requested resource.
    public async Task OpenAsync(
        CanBusConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            throw new CanTransportException("Transport.AlreadyOpen", "Already open.");
        }

        configuration.Validate();

        GsUsbCapabilities capabilities = await ProbeAsync(device, cancellationToken);

        double nominalPoint =
            configuration.NominalSamplePoint ?? GsUsbBitTimingSolver.DefaultSamplePoint;

        if (!GsUsbBitTimingSolver.TrySolve(
                capabilities.NominalLimits,
                configuration.NominalBitrate,
                out GsUsbBitTiming nominal,
                nominalPoint))
        {
            throw new CanTransportNotSupportedException(
                "Transport.GsUsb.NoExactNominal",
                $"This device cannot produce {configuration.NominalBitrate} bit/s exactly "
                + $"from its {capabilities.CanClockHz} Hz CAN clock.",
                configuration.NominalBitrate,
                capabilities.CanClockHz);
        }

        await WriteBitTimingAsync(GsUsbProtocol.RequestBitTiming, nominal, cancellationToken);

        if (configuration.DataBitrate is { } dataBitrate)
        {
            await OpenDataPhaseAsync(
                capabilities, dataBitrate, configuration.DataSamplePoint, cancellationToken);
        }

        uint modeFlags = capabilities.ToModeFlags(
            configuration.Mode, configuration.DataBitrate is not null, configuration.AutoRetransmit);

        _timestamps = false;

        _fdRecords = configuration.DataBitrate is not null;

        await WriteModeAsync(GsUsbProtocol.ModeStart, modeFlags, cancellationToken);

        Capabilities = capabilities;
        IsOpen = true;
        _outstandingEchoes = 0;
        _errorFrames = 0;

        _lastErrorCounters = -1;

        _sending?.Cancel();
        _sending?.Dispose();
        _sending = new CancellationTokenSource();

        _sendSlots = new SemaphoreSlim(MaxFramesInFlight, MaxFramesInFlight);

        _loop = new CancellationTokenSource();

        _reader = Task.Factory.StartNew(
            () => ReceiveLoopAsync(_loop.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    // Closes the active resource.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;

        _sending?.Cancel();

        if (_loop is { } loop)
        {
            await loop.CancelAsync();
        }

        if (_reader is { } reader)
        {
            try
            {
                await reader;
            }
            catch (OperationCanceledException)
            {

            }
        }

        _loop?.Dispose();
        _loop = null;
        _reader = null;

        FlushStrayEchoes(DateTimeOffset.Now, force: true);

        try
        {
            await WriteModeAsync(GsUsbProtocol.ModeReset, 0, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {

            Raise(ex.Message, fatal: false);
        }
    }

    // Sends the requested data.
    public async Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen || Capabilities is not { } capabilities)
        {
            throw new CanTransportException(
                "Transport.NotOpen", "The transport is not open.");
        }

        capabilities.EnsureCanSend(frame);

        byte[] buffer = new byte[GsUsbProtocol.FrameSize(_fdRecords, _timestamps)];

        uint counter = Interlocked.Increment(ref _nextEchoId);
        uint echoId = (EchoIdStrategy ?? GsUsbProtocol.EchoIdFor)(counter);

        int length = GsUsbCodec.Encode(frame, echoId, _timestamps, _fdRecords, buffer);

        SemaphoreSlim slots = _sendSlots
            ?? throw new CanTransportException(
                "Transport.NotOpen", "The transport is not open.");

        CancellationTokenSource? sending = _sending;

        using var wait = sending is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sending.Token);

        if (!await slots.WaitAsync(SendSlotTimeout, wait.Token))
        {

            throw new CanTransportTimeoutException(
                "Transport.GsUsb.NoEchoes",
                $"The adapter did not acknowledge any of the {MaxFramesInFlight} frames already "
                + $"given to it within {SendSlotTimeout.TotalSeconds:0.#} s. The bus may be "
                + "unterminated, bus-off, or have no other node to acknowledge frames.",
                MaxFramesInFlight,
                SendSlotTimeout.TotalSeconds);
        }

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            Interlocked.Increment(ref _outstandingEchoes);
            await device.WriteAsync(buffer.AsMemory(0, length), WriteTimeout, cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _outstandingEchoes);

            slots.Release();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();

        _sendSlots = null;

        _sending?.Dispose();
        _sending = null;

        await device.DisposeAsync();
    }

    // Checks adapter capabilities.
    public static async Task<GsUsbCapabilities> ProbeAsync(
        IUsbBulkDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] hostFormat = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(hostFormat, HostFormatLittleEndian);
        await device.ControlOutAsync(
            GsUsbProtocol.RequestHostFormat, Channel, hostFormat, cancellationToken);

        return await ReadCapabilitiesAsync(device, cancellationToken);
    }

    // Reads capabilities.
    private static async Task<GsUsbCapabilities> ReadCapabilitiesAsync(
        IUsbBulkDevice device, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[GsUsbBitTimingLimits.ExtendedSize];

        int read = await device.ControlInAsync(
            GsUsbProtocol.RequestBtConst,
            Channel,
            buffer.AsMemory(0, GsUsbBitTimingLimits.Size),
            cancellationToken);

        if (read < GsUsbBitTimingLimits.Size ||
            !GsUsbBitTimingLimits.TryParse(buffer, dataPhase: false, out GsUsbBitTimingLimits nominal))
        {
            throw new CanTransportException(
                "Transport.GsUsb.NoLimitsAnswer",
                "The device did not answer the bit timing limits request.");
        }

        GsUsbBitTimingLimits? data = null;

        if ((nominal.Features & GsUsbProtocol.FeatureBtConstExt) != 0)
        {
            int extended = await device.ControlInAsync(
                GsUsbProtocol.RequestBtConstExt, Channel, buffer, cancellationToken);

            if (extended >= GsUsbBitTimingLimits.ExtendedSize &&
                GsUsbBitTimingLimits.TryParse(buffer, dataPhase: true, out GsUsbBitTimingLimits dbtc))
            {
                data = dbtc;
            }
        }

        return new GsUsbCapabilities(nominal.Features, nominal, data);
    }

    // Opens data phase.
    private async Task OpenDataPhaseAsync(
        GsUsbCapabilities capabilities, int dataBitrate, double? samplePoint,
        CancellationToken cancellationToken)
    {
        if (!capabilities.SupportsCanFd)
        {
            throw new CanTransportNotSupportedException(
                "Transport.GsUsb.NoFd",
                "A data phase rate was given but this device has no CAN FD support.");
        }

        if (capabilities.DataLimits is not { } limits)
        {
            throw new CanTransportNotSupportedException(
                "Transport.GsUsb.NoDataLimits",
                "This device claims CAN FD but did not supply data phase bit timing limits.");
        }

        if (!GsUsbBitTimingSolver.TrySolve(
                limits,
                dataBitrate,
                out GsUsbBitTiming timing,
                samplePoint ?? GsUsbBitTimingSolver.DataPhaseSamplePoint))
        {
            throw new CanTransportNotSupportedException(
                "Transport.GsUsb.NoExactData",
                $"This device cannot produce a {dataBitrate} bit/s data phase exactly.",
                dataBitrate);
        }

        await WriteBitTimingAsync(GsUsbProtocol.RequestDataBitTiming, timing, cancellationToken);
    }

    // Writes bit timing.
    private Task WriteBitTimingAsync(
        byte request, GsUsbBitTiming timing, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[GsUsbBitTiming.Size];
        timing.WriteTo(payload);

        return device.ControlOutAsync(request, Channel, payload, cancellationToken);
    }

    // Writes mode.
    private Task WriteModeAsync(uint mode, uint features, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, mode);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), features);

        ModeWriteObserved?.Invoke(payload);

        return device.ControlOutAsync(GsUsbProtocol.RequestMode, Channel, payload, cancellationToken);
    }

    // Receives loop.
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {

        int size = Math.Max(device.MaxBulkTransferSize, GsUsbProtocol.FdTimestampFrameSize);
        byte[] buffer = new byte[size];

        while (!cancellationToken.IsCancellationRequested)
        {

            FlushStrayEchoes(DateTimeOffset.Now, force: false);

            int read;

            try
            {
                read = await device.ReadAsync(buffer, ReadTimeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {

                Raise(ex.Message, fatal: ex is IOException);

                if (ex is IOException)
                {
                    return;
                }

                continue;
            }

            if (read <= 0)
            {
                continue;
            }

            TransferObserved?.Invoke(read);

            Dispatch(buffer.AsSpan(0, read), DateTimeOffset.Now);
        }
    }

    // Dispatches received events.
    private void Dispatch(ReadOnlySpan<byte> transfer, DateTimeOffset arrived)
    {

        bool fdRecords = _fdRecords
            && transfer.Length != GsUsbProtocol.FrameSize(fd: false, _timestamps);

        int offset = 0;

        while (offset < transfer.Length)
        {
            GsUsbDecode status = GsUsbCodec.Decode(
                transfer[offset..], _timestamps, fdRecords, out GsUsbFrame frame);

            if (status == GsUsbDecode.WrongRecordLayout)
            {

                Raise(
                    "The device sent a frame that does not fit this session's record layout, so it "
                    + "is in a mode this host did not ask for.",
                    fatal: true);

                return;
            }

            if (status != GsUsbDecode.Ok)
            {

                return;
            }

            offset += GsUsbProtocol.FrameSize(fdRecords, _timestamps);

            RecordObserved?.Invoke(frame);

            if (!frame.IsReceived)
            {

                EchoObserved?.Invoke(frame.EchoId);

                long left = Interlocked.Decrement(ref _outstandingEchoes);

                if (left < 0)
                {

                    AccumulateStrayEcho(frame, left, DateTimeOffset.Now);
                }

                ReleaseSendSlot();
                continue;
            }

            if (frame.IsErrorFrame)
            {
                Interlocked.Increment(ref _errorFrames);

                if (frame.ErrorCounters is { } counters)
                {
                    Interlocked.Exchange(
                        ref _lastErrorCounters,
                        ((long)counters.TransmitErrors << 8) | counters.ReceiveErrors);
                }

                RaiseErrorFrame(frame.BusStatus, frame.ErrorCounters);
                continue;
            }

            if (frame.Frame.IsFd && Capabilities is { SupportsCanFd: false })
            {
                Raise("The device sent a CAN FD frame but does not advertise CAN FD.", fatal: true);
                return;
            }

            FrameReceived?.Invoke(
                this, new CanFrameReceivedEventArgs(frame.Frame with { Timestamp = arrived }));
        }
    }

    // Releases held resources.
    private void ReleaseSendSlot()
    {
        try
        {
            _sendSlots?.Release();
        }
        catch (SemaphoreFullException)
        {

        }
        catch (ObjectDisposedException)
        {

        }
    }

    // Raises the requested event.
    private void Raise(string message, bool fatal) =>
        Error?.Invoke(this, new CanTransportErrorEventArgs(message, fatal));

    // Collects transmit echoes.
    private void AccumulateStrayEcho(GsUsbFrame frame, long outstanding, DateTimeOffset now)
    {
        if (_strayEchoes == 0)
        {
            _strayFirstAt = now;
            _strayFirstEchoId = frame.EchoId;
            _strayLowestFrameId = frame.Frame.Id;
            _strayHighestFrameId = frame.Frame.Id;

            _strayFirstDetail = string.Create(
                CultureInfo.InvariantCulture,
                $"The device echoed a frame this host never sent (echo id {frame.EchoId}, "
                + $"id 0x{frame.Frame.Id:X3}, len {frame.Frame.Data.Length}, "
                + $"fd {frame.Frame.IsFd}, error {frame.IsErrorFrame}).");
        }
        else
        {
            _strayLowestFrameId = Math.Min(_strayLowestFrameId, frame.Frame.Id);
            _strayHighestFrameId = Math.Max(_strayHighestFrameId, frame.Frame.Id);
        }

        _strayEchoes++;
        _strayLastEchoId = frame.EchoId;
        _strayLastAt = now;

        _strayOutstanding = outstanding;
    }

    // Flushes buffered data.
    private void FlushStrayEchoes(DateTimeOffset now, bool force)
    {
        if (_strayEchoes == 0)
        {
            return;
        }

        if (!force && now - _strayLastAt < StrayEchoQuietWindow)
        {
            return;
        }

        string message = _strayEchoes == 1
            ? $"{_strayFirstDetail} Outstanding echoes is now {_strayOutstanding}."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The device echoed {_strayEchoes} frames this host never sent in "
                + $"{(_strayLastAt - _strayFirstAt).TotalSeconds:0.###} s "
                + $"(echo ids {_strayFirstEchoId}-{_strayLastEchoId}, "
                + $"ids 0x{_strayLowestFrameId:X3}-0x{_strayHighestFrameId:X3}). "
                + $"Outstanding echoes is now {_strayOutstanding}.");

        _strayEchoes = 0;
        _strayFirstDetail = null;

        Raise(message, fatal: false);
    }

    // Raises the requested event.
    private void RaiseErrorFrame(CanBusStatus? status, GsUsbErrorCounters? counters)
    {
        string detail = counters is { } c
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" (transmit errors {c.TransmitErrors}, receive errors {c.ReceiveErrors})")
            : string.Empty;

        string message = status is CanBusStatus.Warning or CanBusStatus.ErrorPassive or CanBusStatus.BusOff
            ? $"The CAN bus {CanBusStatusText.Phrase(status.Value)}{detail}."
            : $"CAN error frame received{detail}.";

        Error?.Invoke(this, new CanTransportErrorEventArgs(message, isFatal: false, collapseKey: message));
    }
}
