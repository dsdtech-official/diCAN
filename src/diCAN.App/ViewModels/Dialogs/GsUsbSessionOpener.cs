using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages gs usb session opener.
public sealed class GsUsbSessionOpener(Func<string, IUsbBulkDevice> openDevice) : ICanSessionOpener, IDfuUnlocker
{

    // Enables firmware update mode.
    public async Task<DfuPinStatus?> AllowDfuOnNextPowerUpAsync(CanDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!DsdTechDfu.AppliesTo(device) || device.SerialNumber is not { Length: > 0 } serial)
        {
            return null;
        }

        try
        {
            await using IUsbBulkDevice usb = openDevice(serial);
            return await DsdTechDfu.AllowOnNextPowerUpAsync(usb);
        }
        catch (Exception)
        {

            return null;
        }
    }

    // Identifies connected devices.
    public async Task<CanDeviceIdentity> IdentifyAsync(CanDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.SerialNumber is not { Length: > 0 } serial)
        {
            return CanDeviceIdentity.Unknown;
        }

        try
        {
            await using IUsbBulkDevice usb = openDevice(serial);

            GsUsbCapabilities capabilities = await GsUsbTransport.ProbeAsync(usb);

            return new CanDeviceIdentity(
                SlcanFirmwareGeneration.Unknown, VersionResponse: null, Report: null, capabilities);
        }
        catch (Exception)
        {

            return CanDeviceIdentity.Unknown;
        }
    }

    // Opens the requested resource.
    public async Task<NewSessionResult> OpenAsync(
        CanDeviceInfo device, SlcanFirmwareGeneration expectedGeneration,
        CanBusConfiguration configuration, string label)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (expectedGeneration is not SlcanFirmwareGeneration.Unknown)
        {

            throw new CanTransportException(
                "Error.ContactSupport",
                $"Error 1004: {device.Description} is a gs_usb device, but it was selected as "
                + $"{SlcanFirmwareId.ShortName(expectedGeneration)}. Rescan and choose it again.",
                "1004");
        }

        if (device.SerialNumber is not { Length: > 0 } serial)
        {

            throw new CanTransportException(
                "Error.ContactSupport",
                "Error 1005: This adapter reports no USB serial number, and a gs_usb device is "
                + "opened by serial number rather than by port name. Re-plug it, and check Device "
                + "Manager for a warning icon.",
                "1005");
        }

        IUsbBulkDevice usb = openDevice(serial);

        var transport = new GsUsbTransport(usb, device.Description);

        try
        {
            await transport.OpenAsync(configuration);

            return new NewSessionResult(
                Transport: transport,

                Title: $"{TitleName(device)} · {label}",

                Generation: SlcanFirmwareGeneration.Unknown,

                Configuration: configuration,

                ConfigurationConfirmed: true,

                DeviceReport: null,
                Device: device,

                RateLabel: label,

                ReportsErrorCounters: transport.Capabilities?.SupportsErrorCounters);
        }
        catch
        {

            await transport.DisposeAsync();
            throw;
        }
    }

    // Gets title name.
    private static string TitleName(CanDeviceInfo device) =>
        CanFirmwareLines.NameOf(device.FirmwareLine, device.FirmwareVersion) ?? "gs_usb";
}
