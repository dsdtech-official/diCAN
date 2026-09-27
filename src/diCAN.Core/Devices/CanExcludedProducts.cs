using System.Text.RegularExpressions;

namespace DiCAN.Core.Devices;

/// <summary>
/// Adapters diCAN deliberately does not list: anything whose USB product string names the
/// JHOINRCH brand or one of its RH-xx models.
///
/// <para>⭐ <b>What diCAN supports.</b> Standard CANable 1.0 and 2.0 adapters and DSD TECH's own
/// SH-C3x range -- see <see cref="CanUsbIds"/>. This class is the single exception, and it is a
/// business decision of DSD TECH, not a technical incompatibility (decision W168).</para>
///
/// <para>🔴 <b>Why -- statement by DSD TECH (Dongguan Deshide Technology Co., Ltd.).</b></para>
/// <list type="bullet">
///   <item>The JHOINRCH US trademark (USPTO registration 7530393, filed 2023-11-14) was
///   registered by Dongguan Jiangyin Trading Co., Ltd. That company was set up on 2023-10-17,
///   while -- as DSD TECH stated in court -- one of its founders was employed by DSD TECH
///   (2023-05-22 to 2024-08-30), and it
///   traded in DSD TECH's line of business.</item>
///   <item>In 2025 DSD TECH sued the former employee and the company for unfair competition
///   (Dongguan No. 2 People's Court, case (2025) Yue 1972 Min Chu No. 5217). The case arose
///   from serious misconduct in DSD TECH's sales operations. At the time DSD TECH did not know
///   about the company; it learned later that the misconduct served the employee's own
///   products.</item>
///   <item>The company was deregistered on 2025-02-17, 25 days after the case was accepted, and
///   the trademark passed to another company.</item>
/// </list>
/// <para>DSD TECH believes the JHOINRCH products were created using DSD TECH's supply chain,
/// internal designs and data, and regards this as a serious breach of professional ethics.</para>
///
/// <para>⚠ <b>How it matches, and its limit.</b> Case-insensitive, on the strings the device
/// reports about itself. The brand is matched in both spellings: the registered mark is
/// <c>JHOINRCH</c>, and <c>Johinrch</c> is the common misspelling. Models are <c>RH</c> plus two
/// digits, with or without a hyphen or space (<c>RH02</c>, <c>RH-02</c>). ⭐ ElmueSoft's CANable 2.5
/// firmware has a build for this brand, and it reports <c>Candlelight 2.5 - Jhoinrch</c> /
/// <c>Slcan 2.5 - Jhoinrch</c>, which is caught. 📕 Not caught: a board on stock CANable firmware
/// (the brand's own page says RH-02 ships with the default Candlelight firmware), or an older board
/// on ElmueSoft's <c>Multiboard</c> build -- both report strings shared with other adapters.
/// Deliberately not measured (🗣 user, 2026-09-25).</para>
/// </summary>
public static partial class CanExcludedProducts
{
    /// <summary>
    /// True when <paramref name="usbString"/> -- a product string, or the name the OS shows for
    /// the device -- identifies an excluded product. Null or empty is never excluded.
    /// </summary>
    public static bool IsExcluded(string? usbString) =>
        !string.IsNullOrEmpty(usbString) && Pattern().IsMatch(usbString);

    /// <summary>True when any of the strings identifies an excluded product.</summary>
    public static bool IsExcluded(params string?[] usbStrings) =>
        usbStrings.Any(IsExcluded);

    // ⚠ The model half is bounded on both sides: "RH" must not be the tail of a longer word, and
    // the two digits must not be the head of a longer number.
    [GeneratedRegex(
        @"JHOINRCH|JOHINRCH|(?<![A-Z0-9])RH[- ]?\d{2}(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
