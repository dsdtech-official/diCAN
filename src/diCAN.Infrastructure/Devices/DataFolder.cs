namespace DiCAN.Infrastructure.Devices;

// Manages data folder.
public static class DataFolder
{

    public const string Default = "diCAN";

    public const string Store = "diCAN-Store";

    private const string StoreFamilyPrefix = "DSDTECH.diCAN_";

    // Gets the data folder name.
    public static string NameFor(string? packageFamilyName) =>
        packageFamilyName is not null
        && packageFamilyName.StartsWith(StoreFamilyPrefix, StringComparison.OrdinalIgnoreCase)
            ? Store
            : Default;
}
