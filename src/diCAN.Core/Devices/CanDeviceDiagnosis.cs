namespace DiCAN.Core.Devices;

// Manages can device.
public enum CanDeviceAdvice
{

    None = 0,

    ReplaceLegacyCanable1,

    ExitBootloader,

    NoPortAssigned,

    NothingFound,
}

// Manages can device diagnosis.
public static class CanDeviceDiagnosis
{

    // Gets for device.
    public static CanDeviceAdvice ForDevice(CanDeviceInfo device) => device.Kind switch
    {
        CanDeviceKind.Slcan => device.PortName is null
            ? CanDeviceAdvice.NoPortAssigned
            : CanDeviceAdvice.None,

        CanDeviceKind.Candlelight => CanDeviceAdvice.None,
        CanDeviceKind.StmBootloader => CanDeviceAdvice.ExitBootloader,
        CanDeviceKind.LegacyCanable1 => CanDeviceAdvice.ReplaceLegacyCanable1,
        _ => CanDeviceAdvice.NothingFound,
    };

    // Diagnoses connected adapters.
    public static CanDeviceAdvice ForCollection(IReadOnlyList<CanDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            return CanDeviceAdvice.NothingFound;
        }

        if (devices.Any(d => d.IsUsable))
        {
            return CanDeviceAdvice.None;
        }

        if (devices.Any(d => d.Kind == CanDeviceKind.Slcan))
        {
            return CanDeviceAdvice.NoPortAssigned;
        }

        if (devices.Any(d => d.Kind == CanDeviceKind.StmBootloader))
        {
            return CanDeviceAdvice.ExitBootloader;
        }

        if (devices.Any(d => d.Kind == CanDeviceKind.LegacyCanable1))
        {
            return CanDeviceAdvice.ReplaceLegacyCanable1;
        }

        return CanDeviceAdvice.NothingFound;
    }
}
