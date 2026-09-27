using DiCAN.Core.Protocol;

namespace DiCAN.Core.Filtering;

// Manages can filter set.
public sealed class CanFilterSet
{

    public const char Separator = ';';

    private readonly CanIdFilter[] _filters;

    // Initializes this instance.
    private CanFilterSet(CanIdFilter[] filters) => _filters = filters;

    public static CanFilterSet PassAll { get; } = new([]);

    public IReadOnlyList<CanIdFilter> Filters => _filters;

    public bool IsPassAll => _filters.Length == 0;

    // Checks a matching value.
    public bool Matches(CanFrame frame)
    {
        if (_filters.Length == 0)
        {
            return true;
        }

        foreach (CanIdFilter filter in _filters)
        {
            if (filter.Matches(frame))
            {
                return true;
            }
        }

        return false;
    }

    // Tries parse.
    public static bool TryParse(string? text, out CanFilterSet set, out CanFilterError? error)
    {
        set = PassAll;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        text = NormalisePunctuation(text);

        var parsed = new List<CanIdFilter>();

        foreach (string part in text.Split(Separator))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (!CanIdFilter.TryParse(part, out CanIdFilter filter, out CanFilterError? reason))
            {
                error = reason;
                return false;
            }

            if (!parsed.Contains(filter))
            {
                parsed.Add(filter);
            }
        }

        set = parsed.Count == 0 ? PassAll : new CanFilterSet([.. parsed]);
        return true;
    }

    // Formats the value as text.
    public override string ToString() => string.Join(Separator, _filters);

    private const char FullWidthColon = (char)0xFF1A;
    private const char FullWidthSemicolon = (char)0xFF1B;

    // Normalizes input text.
    private static string NormalisePunctuation(string text) =>
        text.Contains(FullWidthColon) || text.Contains(FullWidthSemicolon)
            ? text.Replace(FullWidthColon, ':').Replace(FullWidthSemicolon, Separator)
            : text;
}
