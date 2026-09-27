using System.Globalization;
using DiCAN.Core.Protocol;

namespace DiCAN.Core.Recordings;

// Manages recording naming.
public static class RecordingNaming
{

    // Gets adapter label.
    public static string AdapterLabel(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentException(
                "A recording needs the adapter's name; the model label is never empty.",
                nameof(deviceName));
        }

        return deviceName.Trim();
    }

    public const string NoPortLabel = "gs_usb";

    // Gets port label.
    public static string PortLabel(string? portName) =>
        string.IsNullOrWhiteSpace(portName) ? NoPortLabel : portName.Trim();

    // Checks portless.
    public static bool IsPortless(string? port) =>
        string.Equals(port, NoPortLabel, StringComparison.Ordinal);

    // Gets generation suffix.
    public static string GenerationSuffix(string? port, SlcanFirmwareGeneration generation) =>
        IsPortless(port) ? string.Empty : $" ({generation})";

    // Exports name.
    public static string ExportName(string adapter, string port, DateTimeOffset startedAt) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{adapter.Trim()} {PortLabel(port)} {startedAt:yyyy-MM-dd HHmm}");
}

// Manages recording time.
public static class RecordingTime
{

    // Formats a time interval.
    public static long Microseconds(DateTimeOffset startedAt, DateTimeOffset at) =>
        (at.UtcTicks - startedAt.UtcTicks) / TimeSpan.TicksPerMicrosecond;
}
