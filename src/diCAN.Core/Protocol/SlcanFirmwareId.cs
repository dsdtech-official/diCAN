namespace DiCAN.Core.Protocol;

// Manages slcan firmware.
public enum SlcanFirmwareGeneration
{

    Unknown = 0,

    Canable10,

    Canable20,

    Elmue25,
}

// Manages slcan firmware id.
public static class SlcanFirmwareId
{

    private const string Canable20Marker = "canable2";
    private const string Canable10Marker = "canable";

    // Identifies device firmware.
    public static SlcanFirmwareGeneration Identify(ReadOnlySpan<char> response)
    {
        if (response.Length == 0)
        {
            return SlcanFirmwareGeneration.Unknown;
        }

        if (response[0] == SlcanCommands.TextPrefix)
        {
            return SlcanFirmwareGeneration.Elmue25;
        }

        if (response.Contains(Canable20Marker, StringComparison.OrdinalIgnoreCase))
        {
            return SlcanFirmwareGeneration.Canable20;
        }

        if (response.Contains(Canable10Marker, StringComparison.OrdinalIgnoreCase))
        {
            return SlcanFirmwareGeneration.Canable10;
        }

        return SlcanFirmwareGeneration.Unknown;
    }

    // Builds of.
    public static string? BuildOf(string? versionResponse, SlcanDeviceInfo? report)
    {
        if (report?.FirmwareVersion is { } build)
        {
            return build.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return versionResponse?.Trim().Split(' ').FirstOrDefault() is { Length: > 0 } commit
            ? commit
            : null;
    }

    // Gets short name.
    public static string ShortName(SlcanFirmwareGeneration generation) => generation switch
    {
        SlcanFirmwareGeneration.Elmue25 => "CANable 2.5",
        SlcanFirmwareGeneration.Canable20 => "CANable 2.0",
        SlcanFirmwareGeneration.Canable10 => "CANable 1.0",
        _ => "Unknown",
    };

    // Gets short name with answer.
    public static string ShortNameWithAnswer(SlcanFirmwareGeneration generation, string? response)
    {
        string name = ShortName(generation);

        return string.IsNullOrWhiteSpace(response) ? name : $"{name}: {response.Trim()}";
    }
}
