using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DiCAN.Mac.Interop;

// Manages core foundation.
[SupportedOSPlatform("macos")]
internal static class CoreFoundation
{
    private const string Library =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const uint Utf8 = 0x08000100;

    private const nint NumberSInt64 = 4;

    // Creates string.
    internal static IntPtr CreateString(string value) =>
        CFStringCreateWithCString(IntPtr.Zero, value, Utf8);

    // Releases held resources.
    internal static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            CFRelease(value);
        }
    }

    // Formats the value as text.
    internal static string? ToString(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID())
        {
            return null;
        }

        nint capacity = CFStringGetMaximumSizeForEncoding(CFStringGetLength(value), Utf8) + 1;

        if (capacity <= 1 || capacity > 4096)
        {
            return null;
        }

        byte[] buffer = new byte[capacity];
        if (!CFStringGetCString(value, buffer, capacity, Utf8))
        {
            return null;
        }

        int end = Array.IndexOf<byte>(buffer, 0);
        return Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    // Checks array.
    internal static bool IsArray(IntPtr value) =>
        value != IntPtr.Zero && CFGetTypeID(value) == CFArrayGetTypeID();

    // Converts the requested value.
    internal static long? ToInt64(IntPtr value) =>
        value != IntPtr.Zero
        && CFGetTypeID(value) == CFNumberGetTypeID()
        && CFNumberGetValue(value, NumberSInt64, out long result)
            ? result
            : null;

    // Accesses native string data.
    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    // Releases a native value.
    [DllImport(Library)]
    private static extern void CFRelease(IntPtr value);

    // Accesses native preferences.
    [DllImport(Library)]
    private static extern nuint CFGetTypeID(IntPtr value);

    // Accesses native string data.
    [DllImport(Library)]
    private static extern nuint CFStringGetTypeID();

    // Accesses native numeric data.
    [DllImport(Library)]
    private static extern nuint CFNumberGetTypeID();

    // Accesses native array data.
    [DllImport(Library)]
    private static extern nuint CFArrayGetTypeID();

    // Accesses native string data.
    [DllImport(Library)]
    private static extern nint CFStringGetLength(IntPtr value);

    // Accesses native string data.
    [DllImport(Library)]
    private static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    // Accesses native string data.
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint size, uint encoding);

    // Accesses native numeric data.
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr value, nint type, out long result);
}
