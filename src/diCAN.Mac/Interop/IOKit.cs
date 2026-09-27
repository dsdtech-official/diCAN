using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiCAN.Mac.Interop;

// Manages io kit.
[SupportedOSPlatform("macos")]
internal static class IOKit
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";

    private const uint Recursively = 1;

    private const string ServicePlane = "IOService";

    private const int Success = 0;

    // Gets matching usb devices.
    internal static uint MatchingUsbDevices()
    {
        IntPtr matching = IOServiceMatching("IOUSBHostDevice");
        if (matching == IntPtr.Zero)
        {
            return 0;
        }

        return IOServiceGetMatchingServices(0, matching, out uint iterator) == Success ? iterator : 0;
    }

    // Gets the next item.
    internal static uint Next(uint iterator) => IOIteratorNext(iterator);

    // Releases held resources.
    internal static void Release(uint handle)
    {
        if (handle != 0)
        {
            IOObjectRelease(handle);
        }
    }

    // Gets property.
    internal static IntPtr Property(uint node, string key)
    {
        IntPtr cfKey = CoreFoundation.CreateString(key);
        try
        {
            return cfKey == IntPtr.Zero
                ? IntPtr.Zero
                : IORegistryEntryCreateCFProperty(node, cfKey, IntPtr.Zero, 0);
        }
        finally
        {
            CoreFoundation.Release(cfKey);
        }
    }

    // Finds a device property.
    internal static IntPtr SearchProperty(uint node, string key)
    {
        IntPtr cfKey = CoreFoundation.CreateString(key);
        try
        {
            return cfKey == IntPtr.Zero
                ? IntPtr.Zero
                : IORegistryEntrySearchCFProperty(node, ServicePlane, cfKey, IntPtr.Zero, Recursively);
        }
        finally
        {
            CoreFoundation.Release(cfKey);
        }
    }

    // Finds matching USB services.
    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern IntPtr IOServiceMatching(string className);

    // Finds matching USB services.
    [DllImport(Library)]
    private static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    // Gets io iterator next.
    [DllImport(Library)]
    private static extern uint IOIteratorNext(uint iterator);

    // Releases an IOKit object.
    [DllImport(Library)]
    private static extern int IOObjectRelease(uint handle);

    // Queries device properties.
    [DllImport(Library)]
    private static extern IntPtr IORegistryEntryCreateCFProperty(
        uint node, IntPtr key, IntPtr allocator, uint options);

    // Queries device properties.
    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern IntPtr IORegistryEntrySearchCFProperty(
        uint node, string plane, IntPtr key, IntPtr allocator, uint options);
}
