using System.Globalization;

namespace DiCAN.Windows;

// Stores parsed device id data.
public readonly record struct ParsedDeviceId(
    ushort VendorId,
    ushort ProductId,
    string? SerialNumber,
    int? InterfaceNumber);

// Manages windows device id.
public static class WindowsDeviceId
{

    // Tries parse.
    public static bool TryParse(string deviceId, out ParsedDeviceId parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        string[] parts = deviceId.Split('\\');
        if (parts.Length < 2)
        {
            return false;
        }

        ushort? vendorId = null;
        ushort? productId = null;
        int? interfaceNumber = null;

        foreach (string field in parts[1].Split('&'))
        {
            if (TryReadHex(field, "VID_", out ushort vid))
            {
                vendorId = vid;
            }
            else if (TryReadHex(field, "PID_", out ushort pid))
            {
                productId = pid;
            }
            else if (field.StartsWith("MI_", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(field[3..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int mi))
            {
                interfaceNumber = mi;
            }
        }

        if (vendorId is null || productId is null)
        {
            return false;
        }

        parsed = new ParsedDeviceId(
            vendorId.Value,
            productId.Value,
            parts.Length >= 3 ? ReadSerial(parts[2]) : null,
            interfaceNumber);

        return true;
    }

    // Reads serial.
    private static string? ReadSerial(string instanceId) =>
        string.IsNullOrEmpty(instanceId) || instanceId.Contains('&') ? null : instanceId;

    // Reads revision.
    public static ushort? ReadRevision(IReadOnlyList<string>? hardwareIds)
    {
        if (hardwareIds is null)
        {
            return null;
        }

        foreach (string id in hardwareIds)
        {

            string[] parts = id.Split('\\');
            string fields = parts.Length >= 2 ? parts[1] : id;

            foreach (string field in fields.Split('&'))
            {
                if (TryReadHex(field, "REV_", out ushort revision))
                {
                    return revision;
                }
            }
        }

        return null;
    }

    // Tries read hex.
    private static bool TryReadHex(string field, string prefix, out ushort value)
    {
        value = 0;

        return field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               ushort.TryParse(
                   field[prefix.Length..],
                   NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture,
                   out value);
    }
}
