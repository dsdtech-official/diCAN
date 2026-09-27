using DiCAN.Core.Devices;
using Microsoft.Extensions.Logging;

namespace DiCAN.Infrastructure.Diagnostics;

// Monitors connected devices.
public static partial class CanDeviceWatcherLog
{

    // Gets devices changed.
    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug,
        Message = "CAN devices changed: +{AddedCount} -{RemovedCount}, total {Total}; "
            + "added=[{Added}] removed=[{Removed}] current=[{Current}]")]
    public static partial void DevicesChanged(
        ILogger logger, int addedCount, int removedCount, int total,
        string added, string removed, string current);

    // Logs unchanged device state.
    [LoggerMessage(EventId = 1302, Level = LogLevel.Trace,
        Message = "Polled {Total} CAN device(s), no change")]
    public static partial void PolledNoChange(ILogger logger, int total);

    // Logs operation startup.
    [LoggerMessage(EventId = 1303, Level = LogLevel.Debug,
        Message = "CAN device watch started, baseline {Total} device(s): [{Baseline}]")]
    public static partial void Started(ILogger logger, int total, string baseline);

    // Logs operation shutdown.
    [LoggerMessage(EventId = 1304, Level = LogLevel.Debug,
        Message = "CAN device watch stopped")]
    public static partial void Stopped(ILogger logger);

    // Reports operation failure.
    [LoggerMessage(EventId = 1305, Level = LogLevel.Error,
        Message = "CAN device watch loop faulted and stopped; hot-plug detection is no longer "
            + "running ({ExceptionType})")]
    public static partial void Faulted(ILogger logger, Exception exception, string exceptionType);

    // Logs a subscriber failure.
    [LoggerMessage(EventId = 1306, Level = LogLevel.Error,
        Message = "A DevicesChanged subscriber threw ({ExceptionType}); that notification was lost "
            + "but hot-plug detection is still running")]
    public static partial void SubscriberFailed(
        ILogger logger, Exception exception, string exceptionType);

    // Logs device discovery failure.
    [LoggerMessage(EventId = 1307, Level = LogLevel.Error,
        Message = "Baseline CAN device enumeration failed ({ExceptionType}); the watch is starting "
            + "without one, and the first poll will adopt whatever it finds")]
    public static partial void BaselineFailed(
        ILogger logger, Exception exception, string exceptionType);

    // Logs discovery recovery.
    [LoggerMessage(EventId = 1308, Level = LogLevel.Debug,
        Message = "Baseline recovered on the first poll: {Total} device(s) [{Baseline}]; "
            + "no change is reported for this round")]
    public static partial void BaselineRecovered(ILogger logger, int total, string baseline);

    // Describes the requested value.
    public static string Describe(IReadOnlyList<CanDeviceInfo> devices) =>
        devices.Count == 0
            ? "-"
            : string.Join(", ", devices.Select(d => d.PortName is { } port
                ? $"{d.UsbId}/{port}"
                : d.UsbId));
}
