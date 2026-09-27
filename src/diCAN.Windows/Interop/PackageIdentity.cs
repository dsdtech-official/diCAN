using System.Runtime.InteropServices;

namespace DiCAN.Windows.Interop;

// Manages package identity.
public static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;

    // Gets current family name.
    public static string? CurrentFamilyName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            uint length = 0;

            if (GetCurrentPackageFamilyName(ref length, IntPtr.Zero) != ErrorInsufficientBuffer
                || length == 0)
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length * sizeof(char));
            try
            {
                return GetCurrentPackageFamilyName(ref length, buffer) == 0
                    ? Marshal.PtrToStringUni(buffer)
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    // Gets the requested value.
    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, IntPtr packageFamilyName);
}
