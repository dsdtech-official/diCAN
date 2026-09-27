namespace DiCAN.Core.Transport;

// Manages usb receive.
public enum UsbReceiveKind
{

    Packet,

    Retry,

    Fault,
}

// Manages usb receive.
public readonly record struct UsbReceiveResult(UsbReceiveKind Kind, int Length, string? Fault)
{
    public static UsbReceiveResult Retry { get; } = new(UsbReceiveKind.Retry, 0, null);

    // Creates a CAN packet.
    public static UsbReceiveResult Packet(int length) => new(UsbReceiveKind.Packet, length, null);

    // Reports operation failure.
    public static UsbReceiveResult Failed(string message) => new(UsbReceiveKind.Fault, 0, message);
}

// Manages usb receive queue.
public sealed class UsbReceiveQueue
{

    public const int DefaultCapacity = 1024;

    private static readonly TimeSpan UnblockInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<byte[], UsbReceiveResult> _read;
    private readonly byte[] _buffer;
    private readonly int _capacity;
    private readonly string _threadName;
    private readonly Queue<byte[]> _packets = new();
    private readonly Lock _gate = new();

    private readonly SemaphoreSlim _signal = new(0);

    private Thread? _thread;
    private volatile bool _stopping;
    private string? _fault;
    private bool _overflowPending;
    private long _dropped;
    private long _reads;

    // Initializes this instance.
    public UsbReceiveQueue(
        Func<byte[], UsbReceiveResult> read, int bufferSize, int capacity = DefaultCapacity, string threadName = "USB IN")
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _read = read;
        _buffer = new byte[bufferSize];
        _capacity = capacity;
        _threadName = threadName;
    }

    public long DroppedPackets => Interlocked.Read(ref _dropped);

    public long ReadsStarted => Interlocked.Read(ref _reads);

    // Starts the operation.
    public void Start()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("The receive queue has already been started.");
        }

        _thread = new Thread(Drain) { IsBackground = true, Name = _threadName };
        _thread.Start();
    }

    // Gets the next available item.
    public int Take(Span<byte> destination, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_overflowPending && _packets.Count == 0)
            {
                _overflowPending = false;
                throw new InvalidOperationException(
                    $"Rx FIFO overflow: {DroppedPackets} USB packet(s) dropped so far because the receive queue was full.");
            }
        }

        if (!_signal.Wait(timeout, cancellationToken))
        {
            return 0;
        }

        byte[]? packet;
        string? fault;

        lock (_gate)
        {
            fault = _packets.TryDequeue(out packet) ? null : _fault;
        }

        if (packet is null)
        {
            if (fault is null)
            {
                return 0;
            }

            _signal.Release();
            throw new IOException(fault);
        }

        if (packet.Length > destination.Length)
        {
            throw new IOException(
                $"A {packet.Length} byte USB packet does not fit the {destination.Length} byte read buffer.");
        }

        packet.CopyTo(destination);
        return packet.Length;
    }

    // Stops the active operation.
    public bool Stop(Action? unblock, TimeSpan timeout)
    {
        _stopping = true;

        if (_thread is null)
        {
            return true;
        }

        DateTime deadline = DateTime.UtcNow + timeout;

        do
        {
            unblock?.Invoke();

            if (_thread.Join(UnblockInterval))
            {
                return true;
            }
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    // Drains queued input data.
    private void Drain()
    {
        while (!_stopping)
        {
            Interlocked.Increment(ref _reads);

            UsbReceiveResult result;

            try
            {
                result = _read(_buffer);
            }
            catch (Exception ex)
            {
                result = UsbReceiveResult.Failed(ex.Message);
            }

            if (_stopping)
            {
                return;
            }

            switch (result.Kind)
            {
                case UsbReceiveKind.Packet when result.Length > _buffer.Length:
                    Fail($"The USB read reported {result.Length} bytes into a {_buffer.Length} byte buffer.");
                    return;

                case UsbReceiveKind.Packet when result.Length > 0:
                    Enqueue(_buffer.AsSpan(0, result.Length).ToArray());
                    break;

                case UsbReceiveKind.Fault:
                    Fail(result.Fault ?? "The USB read failed.");
                    return;

                default:
                    break;
            }
        }
    }

    // Queues the requested data.
    private void Enqueue(byte[] packet)
    {
        lock (_gate)
        {
            if (_packets.Count >= _capacity)
            {
                _dropped++;
                _overflowPending = true;
                return;
            }

            _packets.Enqueue(packet);
        }

        _signal.Release();
    }

    // Reports operation failure.
    private void Fail(string message)
    {
        lock (_gate)
        {
            _fault = message;
        }

        _signal.Release();
    }
}
