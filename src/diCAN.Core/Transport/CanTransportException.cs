using DiCAN.Core.Localization;

namespace DiCAN.Core.Transport;

// Reports an operation error.
public sealed class CanTransportException(string code, string message, params object?[] arguments)
    : InvalidOperationException(message), ICanTransportFault
{

    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;
}

// Manages i can.
public interface ICanTransportFault : ILocalizableFault
{
}

// Reports an operation error.
public sealed class CanTransportNotSupportedException(
    string code, string message, params object?[] arguments)
    : NotSupportedException(message), ICanTransportFault
{

    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;
}

// Reports a transport timeout.
public sealed class CanTransportTimeoutException(
    string code, string message, params object?[] arguments)
    : TimeoutException(message), ICanTransportFault
{

    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;
}
