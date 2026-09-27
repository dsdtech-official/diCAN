using DiCAN.Core.Localization;

namespace DiCAN.Core.Filtering;

// Stores can filter error data.
public sealed record CanFilterError(
    string Code,
    string Message,
    IReadOnlyList<object?> Arguments) : ILocalizableFault
{

    // Initializes this instance.
    public CanFilterError(string code, string message, params object?[] arguments)
        : this(code, message, (IReadOnlyList<object?>)arguments)
    {
    }

    // Formats the value as text.
    public override string ToString() => Message;
}
