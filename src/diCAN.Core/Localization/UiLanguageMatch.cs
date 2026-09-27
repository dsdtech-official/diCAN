using System.Globalization;

namespace DiCAN.Core.Localization;

// Manages ui language match.
public static class UiLanguageMatch
{
    private const string Simplified = "zh-Hans";
    private const string Traditional = "zh-Hant";

    // Selects the best match.
    public static string? Best(CultureInfo os, IReadOnlyList<string> available)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(available);

        string? direct =
            available.FirstOrDefault(c => string.Equals(c, os.Name, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(c =>
                string.Equals(c, os.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));

        if (direct is not null)
        {
            return direct;
        }

        if (!string.Equals(os.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return MatchChinese(os.Name, available);
    }

    // Finds a matching value.
    private static string? MatchChinese(string name, IReadOnlyList<string> available)
    {
        bool saysHans = name.Contains("Hans", StringComparison.OrdinalIgnoreCase);
        bool saysHant = name.Contains("Hant", StringComparison.OrdinalIgnoreCase);

        bool regionSuggestsTraditional =
            name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);

        bool traditional = saysHant || (!saysHans && regionSuggestsTraditional);

        string wanted = traditional ? Traditional : Simplified;
        string other = traditional ? Simplified : Traditional;

        return Find(available, wanted)

            ?? Find(available, other);
    }

    // Finds the requested item.
    private static string? Find(IReadOnlyList<string> available, string name) =>
        available.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
}
