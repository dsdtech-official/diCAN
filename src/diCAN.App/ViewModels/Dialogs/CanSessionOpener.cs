using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages can session opener.
public sealed class CanSessionOpener(ICanSessionOpener slcan, ICanSessionOpener gsUsb)
    : ICanSessionOpener, IDfuUnlocker
{

    // Enables firmware update mode.
    public Task<DfuPinStatus?> AllowDfuOnNextPowerUpAsync(CanDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.Kind == CanDeviceKind.Candlelight && gsUsb is IDfuUnlocker unlocker
            ? unlocker.AllowDfuOnNextPowerUpAsync(device)
            : Task.FromResult<DfuPinStatus?>(null);
    }

    // Identifies connected devices.
    public Task<CanDeviceIdentity> IdentifyAsync(CanDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Route(device.Kind) is { } opener
            ? opener.IdentifyAsync(device)
            : Task.FromResult(CanDeviceIdentity.Unknown);
    }

    // Opens the requested resource.
    public Task<NewSessionResult> OpenAsync(
        CanDeviceInfo device, SlcanFirmwareGeneration expectedGeneration,
        CanBusConfiguration configuration, string label)
    {
        ArgumentNullException.ThrowIfNull(device);

        ICanSessionOpener opener = Route(device.Kind)
            ?? throw new InvalidOperationException(
                $"{device.Description} is not an adapter diCAN can open ({device.Kind}). "
                + "Check the device list for what it says about this device.");

        return opener.OpenAsync(device, expectedGeneration, configuration, label);
    }

    // Selects the session opener.
    private ICanSessionOpener? Route(CanDeviceKind kind) => kind switch
    {

        CanDeviceKind.Slcan or CanDeviceKind.LegacyCanable1 => slcan,

        CanDeviceKind.Candlelight => gsUsb,

        _ => null,
    };
}
