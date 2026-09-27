using System.Globalization;

namespace DiCAN.Core.Recordings;

// Stores recording volume data.
public readonly record struct RecordingVolume
{
    private const string Prefix = "recordings-";
    private const string Extension = ".db";

    // Initializes this instance.
    public RecordingVolume(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "A month is 1 to 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    // Maps firmware to a model.
    public static RecordingVolume For(DateTimeOffset startedAt) =>
        new(startedAt.Year, startedAt.Month);

    public string Key => $"{Year:D4}-{Month:D2}";

    public string FileName => Prefix + Key + Extension;

    // Tries parse.
    public static bool TryParse(string fileName, out RecordingVolume volume)
    {
        volume = default;

        if (string.IsNullOrEmpty(fileName) ||
            !fileName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> key = fileName.AsSpan(
            Prefix.Length, fileName.Length - Prefix.Length - Extension.Length);

        if (key.Length != 7 || key[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(key[..4], NumberStyles.None, CultureInfo.InvariantCulture, out int year) ||
            !int.TryParse(key[5..], NumberStyles.None, CultureInfo.InvariantCulture, out int month) ||
            month is < 1 or > 12)
        {
            return false;
        }

        volume = new RecordingVolume(year, month);
        return true;
    }

    // Formats the value as text.
    public override string ToString() => Key;
}
