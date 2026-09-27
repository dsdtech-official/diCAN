using System.Runtime.Versioning;
using CANable;
using DiCAN.Core.Transport;

namespace DiCAN.Windows.Usb;

// Manages win usb bulk device.
[SupportedOSPlatform("windows")]
public sealed class WinUsbBulkDevice : IUsbBulkDevice
{

    private const int ReadBufferSize = 512;

    private const int ControlTimeoutMs = 1000;

    private readonly WinUSB _usb;
    private readonly WinUSB.cPipeIn _in;
    private readonly WinUSB.cPipeOut _out;
    private readonly byte _interfaceNumber;

    private bool _closed;

    // Initializes this instance.
    private WinUsbBulkDevice(WinUSB usb, WinUSB.cPipeIn pipeIn, WinUSB.cPipeOut pipeOut, byte interfaceNumber)
    {
        _usb = usb;
        _in = pipeIn;
        _out = pipeOut;
        _interfaceNumber = interfaceNumber;
    }

    // Opens the requested resource.
    public static WinUsbBulkDevice Open(string ntPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ntPath);

        var usb = new WinUSB();

        try
        {
            usb.Open(ntPath, ControlTimeoutMs);

            WinUSB.cPipeIn pipeIn = usb.Interface.GetPipeIn(WinUSB.ePipeType.Bulk)
                ?? throw new InvalidOperationException("The device has no bulk IN endpoint.");

            WinUSB.cPipeOut pipeOut = usb.Interface.GetPipeOut(WinUSB.ePipeType.Bulk)
                ?? throw new InvalidOperationException("The device has no bulk OUT endpoint.");

            pipeIn.StartThread(ReadBufferSize);

            return new WinUsbBulkDevice(usb, pipeIn, pipeOut, usb.Interface.Number);
        }
        catch
        {
            usb.Dispose();
            throw;
        }
    }

    // Opens by serial number.
    public static WinUsbBulkDevice OpenBySerialNumber(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        List<SetupApi.cUsbDevice> present = SetupApi.EnumerateUsbDevices(true);

        SetupApi.cUsbDevice device =
            present.Find(d => string.Equals(d.ms_SerialNo, serialNumber, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No connected gs_usb device has serial number '{serialNumber}'. "
                + $"Present: {(present.Count == 0 ? "none" : string.Join(", ", present.ConvertAll(d => d.ms_SerialNo)))}.");

        return Open(device.ms_DevPath);
    }

    public bool IsOpen => !_closed;

    public int MaxBulkTransferSize => _in.MaxPacketSize;

    // Transfers USB control data.
    public Task ControlOutAsync(
        byte request, ushort value, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = data.ToArray();
        Transfer(WinUSB.eDirection.Out, request, value, ref buffer);

        return Task.CompletedTask;
    }

    // Transfers USB control data.
    public Task<int> ControlInAsync(
        byte request, ushort value, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = new byte[destination.Length];
        Transfer(WinUSB.eDirection.In, request, value, ref buffer);

        buffer.CopyTo(destination.Span);

        return Task.FromResult(buffer.Length);
    }

    // Reads input data.
    public Task<int> ReadAsync(
        Memory<byte> destination, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);

        WinUSB.cUsbInPacket? packet = _in.ReadPipeIn((int)timeout.TotalMilliseconds);

        if (packet is null)
        {
            return Task.FromResult(0);
        }

        int read = packet.ms32_BytesRead;

        if (read > destination.Length)
        {

            throw new IOException(
                $"A {read} byte USB packet does not fit the {destination.Length} byte read buffer.");
        }

        packet.mu8_Buffer.AsSpan(0, read).CopyTo(destination.Span);

        return Task.FromResult(read);
    }

    // Writes output data.
    public Task WriteAsync(
        ReadOnlyMemory<byte> data, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);

        _out.Send(data.ToArray());

        return Task.CompletedTask;
    }

    // Releases held resources.
    public ValueTask DisposeAsync()
    {
        if (_closed)
        {
            return ValueTask.CompletedTask;
        }

        _closed = true;

        _in.Dispose();
        _usb.Dispose();

        return ValueTask.CompletedTask;
    }

    // Transfers device data.
    private void Transfer(WinUSB.eDirection direction, byte request, ushort value, ref byte[] buffer)
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        int error = _usb.CtrlTansfer(
            WinUSB.eSetupRecip.Interface,
            WinUSB.eSetupType.Vendor,
            direction,
            request,
            value,
            _interfaceNumber,
            ref buffer);

        if (error != 0)
        {
            throw new IOException(
                $"USB control request 0x{request:X2} ({direction}) failed with Win32 error {error}.");
        }
    }
}
