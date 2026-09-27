using System.Runtime.InteropServices;

namespace DiCAN.Windows.Interop;

// Manages cfg mgr32.
internal static class CfgMgr32
{

    internal const int Success = 0;

    private const uint FilterEnumerator = 0x00000001;

    private const uint FilterPresent = 0x00000100;

    // Accesses device information.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_ID_List_SizeW(
        out uint pulLen, string? pszFilter, uint ulFlags);

    // Accesses device information.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_ID_ListW(
        string? pszFilter, char[] buffer, uint bufferLen, uint ulFlags);

    // Gets present device ids.
    internal static string[] GetPresentDeviceIds(string enumerator)
    {
        uint flags = FilterEnumerator | FilterPresent;

        if (CM_Get_Device_ID_List_SizeW(out uint length, enumerator, flags) != Success || length == 0)
        {
            return [];
        }

        char[] buffer = new char[length];

        if (CM_Get_Device_ID_ListW(enumerator, buffer, length, flags) != Success)
        {
            return [];
        }

        return new string(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private const uint LocateNormal = 0x00000000;

    private const int DeviceIdBufferChars = 512;

    // Accesses device information.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    // Accesses device information.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    // Accesses device information.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(
        uint dnDevInst, char[] buffer, uint bufferLen, uint ulFlags);

    // Gets parent device id.
    internal static string? GetParentDeviceId(string deviceId)
    {
        if (CM_Locate_DevNodeW(out uint node, deviceId, LocateNormal) != Success)
        {
            return null;
        }

        if (CM_Get_Parent(out uint parent, node, 0) != Success)
        {
            return null;
        }

        char[] buffer = new char[DeviceIdBufferChars];

        if (CM_Get_Device_IDW(parent, buffer, DeviceIdBufferChars, 0) != Success)
        {
            return null;
        }

        int end = Array.IndexOf(buffer, '\0');
        string id = new(buffer, 0, end < 0 ? buffer.Length : end);

        return id.Length == 0 ? null : id;
    }

    private const int BufferSmall = 26;

    internal const uint DevPropTypeString = 0x00000012;

    private static readonly DevPropKey BusReportedDeviceDesc = new(
        new Guid(0x540b947e, 0x8b40, 0x45bc, 0xa8, 0xa2, 0x6a, 0x0b, 0x89, 0x4c, 0xbd, 0xa2),
        4);

    // Stores dev prop key data.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid formatId, uint propertyId)
    {
        private readonly Guid _formatId = formatId;
        private readonly uint _propertyId = propertyId;
    }

    // Accesses device information.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        in DevPropKey propertyKey,
        out uint propertyType,
        byte[]? propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);

    // Gets bus reported device desc.
    internal static string? GetBusReportedDeviceDesc(string deviceId)
    {
        if (CM_Locate_DevNodeW(out uint node, deviceId, LocateNormal) != Success)
        {
            return null;
        }

        uint size = 0;
        int probe = CM_Get_DevNode_PropertyW(
            node, in BusReportedDeviceDesc, out uint type, null, ref size, 0);

        if (probe != BufferSmall || size == 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];

        if (CM_Get_DevNode_PropertyW(
                node, in BusReportedDeviceDesc, out type, buffer, ref size, 0) != Success)
        {
            return null;
        }

        return DecodeStringProperty(buffer, size, type);
    }

    internal const uint DevPropTypeStringList = 0x00002012;

    private static readonly DevPropKey HardwareIds = new(
        new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        3);

    // Gets hardware ids.
    internal static string[] GetHardwareIds(string deviceId)
    {
        if (CM_Locate_DevNodeW(out uint node, deviceId, LocateNormal) != Success)
        {
            return [];
        }

        uint size = 0;
        int probe = CM_Get_DevNode_PropertyW(node, in HardwareIds, out uint type, null, ref size, 0);

        if (probe != BufferSmall || size == 0)
        {
            return [];
        }

        byte[] buffer = new byte[size];

        if (CM_Get_DevNode_PropertyW(node, in HardwareIds, out type, buffer, ref size, 0) != Success)
        {
            return [];
        }

        return DecodeStringListProperty(buffer, size, type);
    }

    // Decodes the input data.
    internal static string[] DecodeStringListProperty(byte[] buffer, uint byteCount, uint propertyType)
    {
        if (propertyType != DevPropTypeStringList || byteCount < sizeof(char))
        {
            return [];
        }

        int chars = (int)Math.Min(byteCount, (uint)buffer.Length) / sizeof(char);
        string value = System.Text.Encoding.Unicode.GetString(buffer, 0, chars * sizeof(char));

        return value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    // Decodes the input data.
    internal static string? DecodeStringProperty(byte[] buffer, uint byteCount, uint propertyType)
    {
        if (propertyType != DevPropTypeString || byteCount < sizeof(char))
        {
            return null;
        }

        int chars = (int)Math.Min(byteCount, (uint)buffer.Length) / sizeof(char);
        string value = System.Text.Encoding.Unicode.GetString(buffer, 0, chars * sizeof(char));

        int end = value.IndexOf('\0');
        if (end >= 0)
        {
            value = value[..end];
        }

        value = value.Trim();
        return value.Length == 0 ? null : value;
    }
}
