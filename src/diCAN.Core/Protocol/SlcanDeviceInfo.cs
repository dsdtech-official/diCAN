using System.Globalization;

namespace DiCAN.Core.Protocol;

// Stores slcan device info data.
public sealed record SlcanDeviceInfo
{

    public string? Board { get; init; }

    public string? Mcu { get; init; }

    public int? DeviceId { get; init; }

    public int? FirmwareVersion { get; init; }

    public int? SlcanVersion { get; init; }

    public int? ClockMhz { get; init; }

    public int? Channels { get; init; }

    public bool? HasQuartz { get; init; }

    public IReadOnlyList<int>? BitTimingLimits { get; init; }

    public string? HalVersion { get; init; }

    public string? SerialNumber { get; init; }

    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // Tries parse.
    public static bool TryParse(ReadOnlySpan<char> line, out SlcanDeviceInfo? info)
    {
        info = null;

        if (line.Length < 2 || line[0] != SlcanCommands.TextPrefix)
        {
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Range range in line[1..].Split('\t'))
        {
            ReadOnlySpan<char> pair = line[1..][range];
            int colon = pair.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = pair[..colon].Trim().ToString();
            string value = pair[(colon + 1)..].Trim().ToString();

            if (key.Length > 0)
            {
                fields[key] = value;
            }
        }

        if (fields.Count == 0)
        {
            return false;
        }

        info = new SlcanDeviceInfo
        {
            Board = Text(fields, "Board"),
            Mcu = Text(fields, "MCU"),
            DeviceId = Number(fields, "DevID"),
            FirmwareVersion = Number(fields, "Firmware"),
            SlcanVersion = Number(fields, "Slcan"),
            ClockMhz = Number(fields, "Clock"),
            Channels = Number(fields, "Channels"),
            HasQuartz = Text(fields, "Quartz") is { } q
                ? string.Equals(q, "Yes", StringComparison.OrdinalIgnoreCase)
                : null,
            BitTimingLimits = Numbers(fields, "Limits"),
            HalVersion = Text(fields, "HAL"),
            SerialNumber = Text(fields, "Serial"),
            Fields = fields,
        };

        return true;
    }

    // Gets text.
    private static string? Text(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

    // Gets the numeric value.
    private static int? Number(Dictionary<string, string> fields, string key) =>
        Text(fields, key) is { } text &&
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    // Gets numeric values.
    private static IReadOnlyList<int>? Numbers(Dictionary<string, string> fields, string key)
    {
        if (Text(fields, key) is not { } text)
        {
            return null;
        }

        string[] parts = text.Split(',');
        var values = new List<int>(parts.Length);

        foreach (string part in parts)
        {
            if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {

                continue;
            }

            values.Add(value);
        }

        return values.Count > 0 ? values : null;
    }
}
