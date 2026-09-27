using DiCAN.Core.Devices;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages i dfu.
public interface IDfuUnlocker
{

    // Enables firmware update mode.
    Task<DfuPinStatus?> AllowDfuOnNextPowerUpAsync(CanDeviceInfo device);
}

// Manages dfu request.
public enum DfuRequestState
{

    Idle,

    Working,

    Allowed,

    Failed,
}
