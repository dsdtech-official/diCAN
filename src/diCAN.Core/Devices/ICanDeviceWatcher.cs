namespace DiCAN.Core.Devices;

// Monitors connected devices.
public interface ICanDeviceWatcher : IAsyncDisposable
{

    bool IsRunning { get; }

    event EventHandler<CanDevicesChangedEventArgs>? DevicesChanged;

    // Starts the operation.
    Task StartAsync(CancellationToken cancellationToken = default);

    // Stops the active operation.
    Task StopAsync(CancellationToken cancellationToken = default);
}

// Stores event data.
public sealed record CanDevicesChangedEventArgs(
    IReadOnlyList<CanDeviceInfo> Added,
    IReadOnlyList<CanDeviceInfo> Removed,
    IReadOnlyList<CanDeviceInfo> Current);
