using System.Reflection;

namespace DiCAN.App;

// Manages app info.
internal static class AppInfo
{

    public static string DisplayVersion { get; } = ComputeDisplayVersion();

    // Computes the requested value.
    private static string ComputeDisplayVersion()
    {
        Assembly assembly = typeof(AppInfo).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
