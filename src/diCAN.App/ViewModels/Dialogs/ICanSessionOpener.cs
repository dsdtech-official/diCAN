using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Serial;
using DiCAN.Core.Transport;
using DiCAN.Infrastructure.Serial;
using DiCAN.Infrastructure.Time;
using DiCAN.Infrastructure.Transport;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages can device.
public sealed record CanDeviceIdentity(
    SlcanFirmwareGeneration Generation,
    string? VersionResponse,
    SlcanDeviceInfo? Report,
    GsUsbCapabilities? Capabilities = null)
{

    public static CanDeviceIdentity Unknown { get; } =
        new(SlcanFirmwareGeneration.Unknown, null, null);

    public bool IsIdentified =>
        Generation is not SlcanFirmwareGeneration.Unknown || Capabilities is not null;
}

// Manages i can.
public interface ICanSessionOpener
{

    // Identifies connected devices.
    Task<CanDeviceIdentity> IdentifyAsync(CanDeviceInfo device);

    // Opens the requested resource.
    Task<NewSessionResult> OpenAsync(
        CanDeviceInfo device, SlcanFirmwareGeneration expectedGeneration,
        CanBusConfiguration configuration, string label);
}

// Manages slcan session opener.
public sealed class SlcanSessionOpener : ICanSessionOpener
{
    private readonly Func<string, ISerialPort> _openPort;

    // Initializes this instance.
    public SlcanSessionOpener()

        : this(port => new SystemIoSerialPort(port, new SerialPortSettings(), new MonotonicClock()))
    {
    }

    // Initializes this instance.
    internal SlcanSessionOpener(Func<string, ISerialPort> openPort) => _openPort = openPort;

    // Identifies connected devices.
    public async Task<CanDeviceIdentity> IdentifyAsync(CanDeviceInfo device)
    {
        if (device.PortName is not { } port)
        {
            return CanDeviceIdentity.Unknown;
        }

        try
        {
            ISerialPort serial = _openPort(port);

            await using var transport = new SlcanTransport(serial);

            SlcanDeviceInfo? report = await transport.IdentifyAsync();

            return new CanDeviceIdentity(transport.Generation, transport.VersionResponse, report);
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
        if (device.PortName is not { } port)
        {

            throw new CanTransportException(
                "Error.ContactSupport",
                "Error 1001: Windows has not assigned this adapter a COM port, so there is nothing "
                + "to open. Check Device Manager for a warning icon on the device.",
                "1001");
        }

        ISerialPort serial = _openPort(port);

        return expectedGeneration switch
        {
            SlcanFirmwareGeneration.Elmue25 =>
                await OpenElmue25Async(serial, port, device, configuration, label),

            SlcanFirmwareGeneration.Canable10 or SlcanFirmwareGeneration.Canable20 =>
                await OpenLegacyAsync(serial, port, device, expectedGeneration, configuration, label),

            _ => throw new CanTransportException(
                "Error.ContactSupport",
                "Error 1002: This adapter has not said which firmware it runs, and the two slcan "
                + "dialects cannot be told apart by USB identity. Rescan, then try again.",
                "1002"),
        };
    }

    // Opens elmue25.
    private static async Task<NewSessionResult> OpenElmue25Async(
        ISerialPort serial, string port, CanDeviceInfo device,
        CanBusConfiguration configuration, string label)
    {
        var transport = new SlcanTransport(serial);

        try
        {

            await transport.IdentifyAsync();

            Verify(SlcanFirmwareGeneration.Elmue25, transport.Generation, port);

            await transport.OpenAsync(configuration);

            return new NewSessionResult(
                Transport: transport,
                Title: $"{port} · {label}",
                Generation: transport.Generation,

                Configuration: configuration,

                ConfigurationConfirmed: true,
                DeviceReport: transport.VersionResponse,
                Device: device,

                RateLabel: label);
        }
        catch
        {

            await transport.DisposeAsync();
            throw;
        }
    }

    // Opens legacy.
    private static async Task<NewSessionResult> OpenLegacyAsync(
        ISerialPort serial, string port, CanDeviceInfo device, SlcanFirmwareGeneration expected,
        CanBusConfiguration configuration, string label)
    {
        var transport = new LegacySlcanTransport(serial);

        try
        {

            try
            {
                await transport.IdentifyAsync();
            }
            catch (InvalidOperationException) when (
                transport.Generation is not SlcanFirmwareGeneration.Unknown)
            {

                Verify(expected, transport.Generation, port);
                throw;
            }

            Verify(expected, transport.Generation, port);

            await transport.OpenAsync(configuration);

            return new NewSessionResult(
                Transport: transport,
                Title: $"{port} · {label}",
                Generation: transport.Generation,

                Configuration: configuration,

                ConfigurationConfirmed: false,
                DeviceReport: transport.VersionResponse,
                Device: device,

                RateLabel: label);
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    // Checks stored data.
    private static void Verify(
        SlcanFirmwareGeneration expected, SlcanFirmwareGeneration actual, string port)
    {
        if (expected == actual)
        {
            return;
        }

        throw new CanTransportException(
            "Error.ContactSupport",
            $"Error 1003: The adapter on {port} now reports {SlcanFirmwareId.ShortName(actual)}, "
            + $"but {SlcanFirmwareId.ShortName(expected)} was the one selected. It was probably "
            + "unplugged and replaced. Rescan and choose it again, because the bit rates offered "
            + "here belong to the generation that was identified.",
            "1003");
    }
}
