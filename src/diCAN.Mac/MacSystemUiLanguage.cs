using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiCAN.Core.Abstractions;
using DiCAN.Mac.Interop;

namespace DiCAN.Mac;

// Reads the system UI language.
[SupportedOSPlatform("macos")]
public sealed class MacSystemUiLanguage : ISystemUiLanguage
{
    private const string Library =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string AnyApplication = "kCFPreferencesAnyApplication";

    private const string AppleLanguages = "AppleLanguages";

    // Gets preferred.
    public CultureInfo? GetPreferred()
    {
        try
        {
            string? name = FirstPreferredLanguage();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {

            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    // Gets first preferred language.
    private static string? FirstPreferredLanguage()
    {
        IntPtr key = CoreFoundation.CreateString(AppleLanguages);
        IntPtr domain = CoreFoundation.CreateString(AnyApplication);

        try
        {
            if (key == IntPtr.Zero || domain == IntPtr.Zero)
            {
                return null;
            }

            IntPtr list = CFPreferencesCopyAppValue(key, domain);
            if (list == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                if (!CoreFoundation.IsArray(list) || CFArrayGetCount(list) <= 0)
                {
                    return null;
                }

                return CoreFoundation.ToString(CFArrayGetValueAtIndex(list, 0));
            }
            finally
            {
                CoreFoundation.Release(list);
            }
        }
        finally
        {
            CoreFoundation.Release(key);
            CoreFoundation.Release(domain);
        }
    }

    // Accesses native preferences.
    [DllImport(Library)]
    private static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);

    // Accesses native array data.
    [DllImport(Library)]
    private static extern nint CFArrayGetCount(IntPtr array);

    // Accesses native array data.
    [DllImport(Library)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
}
