using System.Globalization;

namespace DiCAN.Core.Abstractions;

// Reads the system UI language.
public interface ISystemUiLanguage
{

    // Gets preferred.
    CultureInfo? GetPreferred();
}

// Reads the system UI language.
public sealed class NullSystemUiLanguage : ISystemUiLanguage
{
    public static NullSystemUiLanguage Instance { get; } = new();

    // Gets preferred.
    public CultureInfo? GetPreferred() => null;
}
