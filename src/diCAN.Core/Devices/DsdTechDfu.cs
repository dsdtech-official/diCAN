using System.Buffers.Binary;
using DiCAN.Core.Transport;

namespace DiCAN.Core.Devices;

// Stores dfu pin status data.
public readonly record struct DfuPinStatus(ushort Raw)
{

    public bool EnabledForNextPowerUp => (Raw & 0x0002) != 0;

    public bool ChangePending => (Raw & 0x0100) != 0;
}

// Manages dsd tech dfu.
public static class DsdTechDfu
{

    public const byte RequestSetPinStatus = 24;

    public const byte RequestGetPinStatus = 25;

    public const ushort PinBoot0 = 1;

    public const ushort OperationEnable = 6;

    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ConfirmInterval = TimeSpan.FromMilliseconds(50);

    // Checks firmware eligibility.
    public static bool AppliesTo(CanDeviceInfo device) =>
        device.Kind == CanDeviceKind.Candlelight
        && device.FirmwareLine == CanFirmwareLine.DsdTechFdCandlelight
        && device.FirmwareVersion is not null;

    // Enables the selected feature.
    public static byte[] EnableRequestBody()
    {
        byte[] body = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0, 2), OperationEnable);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2, 2), PinBoot0);
        return body;
    }

    // Reads input data.
    public static async Task<DfuPinStatus?> ReadAsync(IUsbBulkDevice device, CancellationToken cancellationToken = default)
    {
        byte[] answer = new byte[2];
        int read = await device.ControlInAsync(RequestGetPinStatus, PinBoot0, answer, cancellationToken);

        return read == 2 ? new DfuPinStatus(BinaryPrimitives.ReadUInt16LittleEndian(answer)) : null;
    }

    // Enables firmware update mode.
    public static async Task<DfuPinStatus?> AllowOnNextPowerUpAsync(
        IUsbBulkDevice device,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        delay ??= Task.Delay;

        await device.ControlOutAsync(RequestSetPinStatus, PinBoot0, EnableRequestBody(), cancellationToken);

        DfuPinStatus? status = null;
        for (TimeSpan waited = TimeSpan.Zero; waited <= ConfirmTimeout; waited += ConfirmInterval)
        {
            await delay(ConfirmInterval, cancellationToken);

            try
            {
                status = await ReadAsync(device, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }

            if (status is { EnabledForNextPowerUp: true })
            {
                return status;
            }
        }

        return status;
    }
}
