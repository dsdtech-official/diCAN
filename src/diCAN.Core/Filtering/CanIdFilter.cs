using System.Globalization;
using DiCAN.Core.Protocol;

namespace DiCAN.Core.Filtering;

// Defines can id width values.
public enum CanIdWidth
{

    Standard,

    Extended,
}

// Stores can id filter data.
public readonly record struct CanIdFilter(int Id, int Mask, CanIdWidth Width)
{

    // Gets max id.
    public static int MaxId(CanIdWidth width) =>
        width == CanIdWidth.Extended ? 0x1FFFFFFF : 0x7FF;

    private const int StandardDigits = 3;

    private const int ExtendedDigits = 8;

    // Checks a matching value.
    public bool Matches(CanFrame frame)
    {
        CanIdWidth width = frame.IsExtended ? CanIdWidth.Extended : CanIdWidth.Standard;

        return width == Width && (frame.Id & Mask) == Id;
    }

    // Tries parse.
    public static bool TryParse(string? text, out CanIdFilter filter, out CanFilterError? error)
    {
        filter = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = new CanFilterError(
                "FilterError.Empty",
                "A filter cannot be empty. Use <id>:<mask>, for example 7E0:7FF.");
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        int colon = span.IndexOf(':');

        if (colon < 0)
        {
            error = new CanFilterError(
                "FilterError.NoColon",
                $"'{text.Trim()}' has no ':'. A filter is <id>:<mask>, for example 7E0:7FF.",
                text.Trim());
            return false;
        }

        ReadOnlySpan<char> idText = span[..colon].Trim();
        ReadOnlySpan<char> maskText = span[(colon + 1)..].Trim();

        if (maskText.Contains(':'))
        {
            error = new CanFilterError(
                "FilterError.TwoColons",
                $"'{text.Trim()}' has more than one ':'. Separate several filters with ';'.",
                text.Trim());
            return false;
        }

        if (!TryParseHex(idText, PartId, out uint id, out error) ||
            !TryParseHex(maskText, PartMask, out uint mask, out error))
        {
            return false;
        }

        CanIdWidth width = idText.Length > StandardDigits || maskText.Length > StandardDigits
            ? CanIdWidth.Extended
            : CanIdWidth.Standard;

        int bits = width == CanIdWidth.Extended ? 29 : 11;
        uint max = (uint)MaxId(width);

        if (id > max)
        {
            error = new CanFilterError(
                "FilterError.IdTooLarge",
                $"id {id:X} does not fit in {bits} bits (largest is {max:X}).",
                $"{id:X}", bits, $"{max:X}");
            return false;
        }

        if (mask > max)
        {
            error = new CanFilterError(
                "FilterError.MaskTooLarge",
                $"mask {mask:X} does not fit in {bits} bits (largest is {max:X}).",
                $"{mask:X}", bits, $"{max:X}");
            return false;
        }

        filter = new CanIdFilter((int)(id & mask), (int)mask, width);
        return true;
    }

    // Tries parse hex.
    private static bool TryParseHex(
        ReadOnlySpan<char> text, string what, out uint value, out CanFilterError? error)
    {
        value = 0;
        error = null;

        string english = what == PartId ? "id" : "mask";

        if (text.IsEmpty)
        {
            error = new CanFilterError(
                "FilterError.PartMissing",
                $"The {english} is missing. A filter is <id>:<mask>, for example 7E0:7FF.",
                what);
            return false;
        }

        if (text.Length > ExtendedDigits)
        {
            error = new CanFilterError(
                "FilterError.PartTooLong",
                $"The {english} '{text}' has {text.Length} hex digits; the most a CAN id can have "
                + $"is {ExtendedDigits}.",
                what, text.ToString(), text.Length, ExtendedDigits);
            return false;
        }

        if (!uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value))
        {
            error = new CanFilterError(
                "FilterError.PartNotHex",
                $"The {english} '{text}' is not hexadecimal. Write it bare, without 0x: 7E0, not "
                + "0x7E0.",
                what, text.ToString());
            return false;
        }

        return true;
    }

    internal const string PartId = "FilterError.Part.Id";

    internal const string PartMask = "FilterError.Part.Mask";

    // Formats the value as text.
    public override string ToString()
    {
        int digits = Width == CanIdWidth.Extended ? ExtendedDigits : StandardDigits;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Id.ToString($"X{digits}", CultureInfo.InvariantCulture)}:"
            + $"{Mask.ToString($"X{digits}", CultureInfo.InvariantCulture)}");
    }
}
