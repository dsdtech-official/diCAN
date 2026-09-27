using DiCAN.Core.Abstractions;

namespace DiCAN.Infrastructure.Devices;

// Manages app paths.
public sealed class AppPaths : IAppPaths
{

    private const string LegacyName = "devices.db";

    // Initializes this instance.
    public AppPaths(string? root = null, string folderName = DataFolder.Default)
    {
        string folder = Path.Combine(
            root ?? Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create),
            folderName);

        Directory.CreateDirectory(folder);

        UserDataDatabasePath = Path.Combine(folder, "dican.db");

        RecordingsFolder = folder;

        UserDataSchema.MigrateLegacyFile(Path.Combine(folder, LegacyName), UserDataDatabasePath);
    }

    public string UserDataDatabasePath { get; }

    public string RecordingsFolder { get; }
}
