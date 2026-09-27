

#if DEBUG

using DiCAN.App.ViewModels.Dialogs;
using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;
using DiCAN.Infrastructure.Virtual;

namespace DiCAN.App.Demo;

// Manages demo session.
internal static class DemoSession
{
    internal const string DeviceId = @"DEMO\VIRTUAL-BUS";

    internal static CanDeviceInfo Device { get; } = new(
        Kind: CanDeviceKind.Slcan,
        VendorId: 0x16D0,
        ProductId: 0x117E,
        SerialNumber: "DEMO",
        PortName: "DEMO",
        DeviceId: DeviceId,
        Description: "Simulated bus (--demo, no hardware)");
}

// Discovers USB CAN adapters.
internal sealed class DemoDeviceEnumerator : ICanDeviceEnumerator
{

    // Lists the available items.
    public IReadOnlyList<CanDeviceInfo> Enumerate() => [DemoSession.Device];
}

// Manages demo session opener.
internal sealed class DemoSessionOpener : ICanSessionOpener
{
    private readonly double _framesPerSecond;

    // Initializes this instance.
    internal DemoSessionOpener(double framesPerSecond) => _framesPerSecond = framesPerSecond;

    // Identifies connected devices.
    public Task<CanDeviceIdentity> IdentifyAsync(CanDeviceInfo device)
    {
        SlcanDeviceInfo.TryParse(DemoVersionResponse, out SlcanDeviceInfo? report);
        return Task.FromResult(
            new CanDeviceIdentity(SlcanFirmwareGeneration.Elmue25, DemoVersionResponse, report));
    }

    private const string DemoVersionResponse =
        "+Board: SIMULATION\tMCU: none\tFirmware: 2427156\tClock: 160\tChannels: 1\tQuartz: No"
        + "\tLimits: 512,256,128,128,32,32,16,16\tSerial: SIMULATION";

    // Opens the requested resource.
    public async Task<NewSessionResult> OpenAsync(
        CanDeviceInfo device, SlcanFirmwareGeneration expectedGeneration,
        CanBusConfiguration configuration, string label)
    {

        var transport = new DemoTransport(_framesPerSecond);

        await transport.OpenAsync(configuration);

        return (new NewSessionResult(
            Transport: transport,
            Title: $"SIMULATED · {label} · {_framesPerSecond:N0} frames/s",
            Generation: SlcanFirmwareGeneration.Elmue25,

            Configuration: configuration,
            ConfigurationConfirmed: true,

            DeviceReport: DemoVersionResponse,

            Device: DemoSession.Device,

            IsSimulated: true,

            RateLabel: label));
    }
}

// Manages demo transport.
internal sealed class DemoTransport : ICanTransport
{
    private readonly VirtualBus _bus = new();
    private readonly double _framesPerSecond;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pump;

    // Initializes this instance.
    internal DemoTransport(double framesPerSecond) => _framesPerSecond = framesPerSecond;

    public string Description => "Simulated bus";

    public bool IsOpen { get; private set; }

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;

    public event EventHandler<CanTransportErrorEventArgs>? Error;

    // Opens the requested resource.
    public Task OpenAsync(CanBusConfiguration configuration, CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        _pump = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    // Closes the active resource.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = false;
        await _cts.CancelAsync();

        if (_pump is { } pump)
        {
            try
            {
                await pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    // Sends the requested data.
    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default) =>
        IsOpen
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException("The simulated adapter is not open."));

    // Releases held resources.
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _cts.Dispose();
    }

    // Processes queued data.
    private async Task PumpAsync(CancellationToken token)
    {
        const int TickMs = 20;
        int perTick = Math.Max(1, (int)(_framesPerSecond * TickMs / 1000.0));

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMs));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                CanFrame[] batch = _bus.Generate(perTick, DateTimeOffset.UtcNow, _framesPerSecond);

                foreach (CanFrame frame in batch)
                {
                    FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new CanTransportErrorEventArgs(ex.Message, isFatal: true));
        }
    }
}

#endif
