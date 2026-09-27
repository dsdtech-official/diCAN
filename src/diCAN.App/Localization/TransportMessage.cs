using DiCAN.Core.Localization;
using DiCAN.Core.Transport;

namespace DiCAN.App.Localization;

// Manages transport message.
public static class TransportMessage
{

    // Describes the requested value.
    public static string Describe(this ILocalizationService localization, Exception exception) =>

        exception is ILocalizableFault fault
            ? localization.Describe(fault, exception.Message)
            : exception.Message;

    // Describes the requested value.
    public static string Describe(
        this ILocalizationService localization, ILocalizableFault e, string englishFallback)
    {

        object?[] args = [.. e.Arguments.Select(a =>
            a is string s && localization.Find(s) is { } translated ? translated : a)];

        return localization.Find(e.Code) is null
            ? englishFallback
            : localization.Format(e.Code, args);
    }
}
