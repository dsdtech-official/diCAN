namespace DiCAN.Core.Transport;

// Manages i usb.
public interface IUsbBulkDevice : IAsyncDisposable
{

    bool IsOpen { get; }

    int MaxBulkTransferSize { get; }

    // Transfers USB control data.
    Task ControlOutAsync(
        byte request, ushort value, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // Transfers USB control data.
    Task<int> ControlInAsync(
        byte request, ushort value, Memory<byte> destination, CancellationToken cancellationToken = default);

    // Reads input data.
    Task<int> ReadAsync(
        Memory<byte> destination, TimeSpan timeout, CancellationToken cancellationToken = default);

    // Writes output data.
    Task WriteAsync(
        ReadOnlyMemory<byte> data, TimeSpan timeout, CancellationToken cancellationToken = default);
}
