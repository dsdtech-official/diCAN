using DiCAN.Core.Localization;

namespace DiCAN.App.ViewModels;

// Reports an operation error.
public sealed class SendInputFormatException(
    string code, string message, params object?[] arguments)
    : FormatException(message), ILocalizableFault
{

    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;
}

// Reports an operation error.
public sealed class SendInputRangeException(
    string paramName, string code, string message, params object?[] arguments)
    : ArgumentOutOfRangeException(paramName, message), ILocalizableFault
{

    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;
}

// Reports an operation error.
public sealed class SendInputArgumentException(
    string paramName, string code, string message, params object?[] arguments)
    : ArgumentException(message, paramName), ILocalizableFault
{

    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;
}
