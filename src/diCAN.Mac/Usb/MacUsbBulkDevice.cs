using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiCAN.Core.Transport;
using DiCAN.Mac.Interop;

namespace DiCAN.Mac.Usb;

// Manages mac usb bulk device.
[SupportedOSPlatform("macos")]
public sealed class MacUsbBulkDevice : IUsbBulkDevice
{

    private const int ReadBufferSize = 512;

    private const uint ControlTimeoutMs = 1000;

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly IntPtr _device;
    private readonly UsbReceiveQueue _received;

    private readonly Lock _io = new();

    private bool _closed;

    // Initializes this instance.
    private MacUsbBulkDevice(IntPtr device, string serialNumber)
    {
        _device = device;
        MaxBulkTransferSize = DiCanUsb.MaxPacketIn(device);
        _received = new UsbReceiveQueue(ReadOnce, ReadBufferSize, threadName: $"gs_usb IN {serialNumber}");

        _received.Start();
    }

    // Opens by serial number.
    public static MacUsbBulkDevice OpenBySerialNumber(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        int code = DiCanUsb.Open(serialNumber, out IntPtr device);

        if (code != DiCanUsb.Success || device == IntPtr.Zero)
        {
            throw code switch
            {
                DiCanUsb.NotFound => new InvalidOperationException(
                    $"No connected gs_usb device has serial number '{serialNumber}'."),
                DiCanUsb.ExclusiveAccess => new InvalidOperationException(
                    $"The gs_usb device '{serialNumber}' is already open in another program."),
                DiCanUsb.NoResources => new IOException(
                    $"macOS refused to open the gs_usb device '{serialNumber}' ({DiCanUsb.Describe(code)}). "
                    + "Under App Sandbox this is what a missing com.apple.security.device.usb entitlement looks like."),
                _ => new IOException(
                    $"Could not open the gs_usb device '{serialNumber}': {DiCanUsb.Describe(code)}."),
            };
        }

        try
        {
            return new MacUsbBulkDevice(device, serialNumber);
        }
        catch
        {
            DiCanUsb.Close(device);
            throw;
        }
    }

    public bool IsOpen => !_closed;

    public int MaxBulkTransferSize { get; }

    // Transfers USB control data.
    public Task ControlOutAsync(
        byte request, ushort value, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Transfer(directionIn: false, request, value, data.ToArray());

        return Task.CompletedTask;
    }

    // Transfers USB control data.
    public Task<int> ControlInAsync(
        byte request, ushort value, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = new byte[destination.Length];
        int received = Transfer(directionIn: true, request, value, buffer);

        buffer.AsSpan(0, received).CopyTo(destination.Span);

        return Task.FromResult(received);
    }

    // Reads input data.
    public Task<int> ReadAsync(
        Memory<byte> destination, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);

        return Task.FromResult(_received.Take(destination.Span, timeout, cancellationToken));
    }

    // Writes output data.
    public Task WriteAsync(
        ReadOnlyMemory<byte> data, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = data.ToArray();

        uint deadline = (uint)Math.Clamp(timeout.TotalMilliseconds, 1, uint.MaxValue);

        lock (_io)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            int code = DiCanUsb.Write(
                _device, ref MemoryMarshal.GetArrayDataReference(buffer), (uint)buffer.Length, deadline);

            if (code != DiCanUsb.Success)
            {
                throw new IOException($"USB bulk write of {buffer.Length} bytes failed: {DiCanUsb.Describe(code)}.");
            }
        }

        return Task.CompletedTask;
    }

    // Releases held resources.
    public ValueTask DisposeAsync()
    {
        lock (_io)
        {
            if (_closed)
            {
                return ValueTask.CompletedTask;
            }

            _closed = true;
        }

        if (_received.Stop(() => DiCanUsb.Abort(_device), StopTimeout))
        {
            DiCanUsb.Close(_device);
        }

        return ValueTask.CompletedTask;
    }

    // Reads once.
    private UsbReceiveResult ReadOnce(byte[] buffer)
    {
        int code = DiCanUsb.Read(
            _device, ref MemoryMarshal.GetArrayDataReference(buffer), (uint)buffer.Length, out uint transferred);

        return code switch
        {
            DiCanUsb.Success => UsbReceiveResult.Packet((int)transferred),

            DiCanUsb.PipeStalled => UsbReceiveResult.Retry,

            _ => UsbReceiveResult.Failed($"USB bulk read failed: {DiCanUsb.Describe(code)}."),
        };
    }

    // Transfers device data.
    private int Transfer(bool directionIn, byte request, ushort value, byte[] buffer)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(buffer.Length, ushort.MaxValue);

        lock (_io)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            int code = DiCanUsb.Control(
                _device,
                directionIn ? (byte)1 : (byte)0,
                request,
                value,
                ref MemoryMarshal.GetArrayDataReference(buffer),
                (ushort)buffer.Length,
                ControlTimeoutMs,
                out uint transferred);

            if (code != DiCanUsb.Success)
            {
                throw new IOException(
                    $"USB control request 0x{request:X2} ({(directionIn ? "In" : "Out")}) failed: {DiCanUsb.Describe(code)}.");
            }

            return (int)Math.Min(transferred, (uint)buffer.Length);
        }
    }
}
