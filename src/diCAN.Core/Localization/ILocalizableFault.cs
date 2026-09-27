namespace DiCAN.Core.Localization;

// Manages i localizable.
public interface ILocalizableFault
{

    string Code { get; }

    IReadOnlyList<object?> Arguments { get; }
}
