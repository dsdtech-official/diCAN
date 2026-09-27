using DiCAN.Core.Devices;
using DiCAN.Core.Protocol;
using DiCAN.Core.Transport;

namespace DiCAN.App.ViewModels.Dialogs;

// Manages new session.
public sealed record NewSessionResult(
    ICanTransport Transport,
    string Title,
    SlcanFirmwareGeneration Generation,
    CanBusConfiguration Configuration,
    bool ConfigurationConfirmed,
    string? DeviceReport,
    CanDeviceInfo Device,
    bool IsSimulated = false,

    string? RateLabel = null,

    bool? ReportsErrorCounters = null)
{

    public int BitsPerSecond => Configuration.NominalBitrate;

    public SlcanOpenMode Mode => Configuration.Mode;

    public int? DataBitrate => Configuration.DataBitrate;

    public bool AutoRetransmit => Configuration.AutoRetransmit;

    public bool IsListenOnly => Mode == SlcanOpenMode.Silent;

    public string ReconnectLabel =>
        RateLabel ?? BitsPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
