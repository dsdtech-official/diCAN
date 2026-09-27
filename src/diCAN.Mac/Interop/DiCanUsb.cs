using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiCAN.Mac.Interop;

// Manages di can usb.
[SupportedOSPlatform("macos")]
internal static class DiCanUsb
{
    private const string Library = "dican_usb";

    internal const int Success = 0;

    internal const int NotFound = unchecked((int)0xE00002F0);

    internal const int ExclusiveAccess = unchecked((int)0xE00002C5);

    internal const int NoResources = unchecked((int)0xE00002BE);

    internal const int NoDevice = unchecked((int)0xE00002C0);

    internal const int Aborted = unchecked((int)0xE00002EB);

    internal const int NotResponding = unchecked((int)0xE00002ED);

    internal const int PipeStalled = unchecked((int)0xE000404F);

    internal const int TransactionTimeout = unchecked((int)0xE0004051);

    // Opens the requested resource.
    [DllImport(Library, EntryPoint = "dican_usb_open", CharSet = CharSet.Ansi)]
    internal static extern int Open(string serial, out IntPtr device);

    // Gets max packet in.
    [DllImport(Library, EntryPoint = "dican_usb_max_packet_in")]
    internal static extern ushort MaxPacketIn(IntPtr device);

    // Transfers USB control data.
    [DllImport(Library, EntryPoint = "dican_usb_control")]
    internal static extern int Control(
        IntPtr device, byte directionIn, byte request, ushort value, ref byte data, ushort length,
        uint timeoutMs, out uint transferred);

    // Reads input data.
    [DllImport(Library, EntryPoint = "dican_usb_read")]
    internal static extern int Read(IntPtr device, ref byte buffer, uint capacity, out uint transferred);

    // Writes output data.
    [DllImport(Library, EntryPoint = "dican_usb_write")]
    internal static extern int Write(IntPtr device, ref byte buffer, uint length, uint timeoutMs);

    // Aborts the active operation.
    [DllImport(Library, EntryPoint = "dican_usb_abort")]
    internal static extern int Abort(IntPtr device);

    // Closes the active resource.
    [DllImport(Library, EntryPoint = "dican_usb_close")]
    internal static extern void Close(IntPtr device);

    // Describes the requested value.
    internal static string Describe(int code)
    {
        string name = code switch
        {
            NotFound => "kIOReturnNotFound",
            ExclusiveAccess => "kIOReturnExclusiveAccess",
            NoResources => "kIOReturnNoResources",
            NoDevice => "kIOReturnNoDevice",
            Aborted => "kIOReturnAborted",
            NotResponding => "kIOReturnNotResponding",
            PipeStalled => "kIOUSBPipeStalled",
            TransactionTimeout => "kIOUSBTransactionTimeout",
            _ => "IOReturn",
        };

        return $"{name} 0x{(uint)code:X8}";
    }
}
