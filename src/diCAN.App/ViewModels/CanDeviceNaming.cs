using DiCAN.App.Localization;
using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;

namespace DiCAN.App.ViewModels;

// Manages can device naming.
internal static class CanDeviceNaming
{

    // Gets model label.
    public static string ModelLabel(
        ILocalizationService localization,
        CanDeviceInfo device,
        int? reportedDeviceId = null)
    {
        if (CanProductModels.NameOf(CanProductModels.For(device.FirmwareLine, reportedDeviceId))
            is { } model)
        {
            return model;
        }

        return localization[device.Kind switch
        {
            CanDeviceKind.Slcan => "Device.Model.Slcan",
            CanDeviceKind.Candlelight => "Device.Model.Candlelight",
            CanDeviceKind.StmBootloader => "Device.Model.Bootloader",

            CanDeviceKind.LegacyCanable1 => "Device.Model.LegacyCanable1",

            _ => "Device.Model.Unknown",
        }];
    }

    // Gets display name.
    public static string DisplayName(
        ILocalizationService localization,
        CanDeviceInfo device,
        string? versionResponse,
        string? note)
    {
        if (note is { Length: > 0 })
        {
            return note;
        }

        SlcanDeviceInfo.TryParse(versionResponse ?? string.Empty, out SlcanDeviceInfo? report);

        return ModelLabel(localization, device, report?.DeviceId);
    }
}
