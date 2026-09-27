using System.Globalization;

namespace DiCAN.App.Composition;

// Manages command line.
public sealed record CommandLineOptions(
    bool DemoRequested,
    double DemoFramesPerSecond,
    string LogLevel,
    string? DataDirectoryRequested = null)
{

    public static bool SimulatorAvailable =>
#if DEBUG
        true;
#else
        false;
#endif

    public bool Demo => DemoRequested && SimulatorAvailable;

    public bool DemoRefused => DemoRequested && !SimulatorAvailable;

    public static bool DataDirectoryAvailable =>
#if DEBUG
        true;
#else
        false;
#endif

    public string? DataDirectory =>
        DataDirectoryAvailable && DataDirectoryRequested is { Length: > 0 } folder ? folder : null;

    public bool DataDirectoryRefused => DataDirectoryRequested is not null && !DataDirectoryAvailable;

    public bool DataDirectoryUnusable => DataDirectoryRequested is { Length: 0 };

    public const double DefaultDemoRate = 4_000;

    public static CommandLineOptions Default { get; } = new(false, DefaultDemoRate, "info");

    // Parses input data.
    public static CommandLineOptions Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return Default;
        }

        bool demo = false;
        double rate = DefaultDemoRate;
        string level = "info";
        string? dataDirectory = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (Matches(args[i], "--demo"))
            {
                demo = true;

                if (TryValue(args, i, out string? value) &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                    parsed > 0)
                {
                    rate = parsed;
                    i++;
                }
            }
            else if (Matches(args[i], "--debug"))
            {
                level = "debug";

                if (TryValue(args, i, out string? value) && IsLevel(value))
                {
                    level = value.ToLowerInvariant();
                    i++;
                }
            }
            else if (Matches(args[i], "--data-dir"))
            {

                dataDirectory = string.Empty;

                if (TryValue(args, i, out string? value))
                {
                    dataDirectory = FullPathOrEmpty(value);
                    i++;
                }
            }
        }

        return new CommandLineOptions(demo, rate, level, dataDirectory);
    }

    // Describes the requested value.
    public string Describe()
    {
        string shape = Demo
            ? string.Create(CultureInfo.InvariantCulture, $"demo @ {DemoFramesPerSecond:N0}/s, log {LogLevel}")
            : DemoRefused
                ? $"real hardware (demo refused: not in this build), log {LogLevel}"
                : $"real hardware, log {LogLevel}";

        return DataDirectoryRefused ? shape + ", data-dir refused: not in this build"
            : DataDirectoryUnusable ? shape + ", data-dir ignored: no usable path"
            : DataDirectory is { } folder ? $"{shape}, data {folder}"
            : shape;
    }

    // Gets full path or empty.
    private static string FullPathOrEmpty(string value)
    {
        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    // Checks a matching value.
    private static bool Matches(string argument, string name) =>
        string.Equals(argument, name, StringComparison.OrdinalIgnoreCase);

    // Tries value.
    private static bool TryValue(string[] args, int index, out string value)
    {
        value = string.Empty;

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        value = args[index + 1];
        return true;
    }

    // Checks level.
    private static bool IsLevel(string value) => value.ToLowerInvariant() is
        "off" or "error" or "warning" or "info" or "debug" or "trace";
}
