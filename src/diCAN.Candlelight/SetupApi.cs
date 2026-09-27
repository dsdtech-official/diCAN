

using System;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Win32;

using eApiError           = CANable.Utils.eApiError;

namespace CANable
{

// Manages setup api.
public class SetupApi
{
    #region structs, classes

    // Manages c usb device.
    public class cUsbDevice : IComparable
    {
        public String ms_Product;
        public String ms_Interface;
        public String ms_DevPath;
        public String ms_SerialNo;
        public int    ms32_Interface;

        // Gets can channel.
        public int GetCanChannel()
        {
            switch (ms32_Interface)
            {
                case 0:                                  return  1;
                case Candlelight.FIRMW_UPDATE_INTERFACE: return -1;
                default:                                 return ms32_Interface;
            }
        }

        // Compares the requested values.
        int IComparable.CompareTo(Object o_Comp)
        {
            cUsbDevice i_Dev2 = (cUsbDevice)o_Comp;

            int s32_Diff = ms_SerialNo.CompareTo(i_Dev2.ms_SerialNo);

            if (s32_Diff == 0)
                s32_Diff = ms32_Interface.CompareTo(i_Dev2.ms32_Interface);

            return s32_Diff;
        }
    }

    // Manages sp device.
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public int    cbSize;
        public Guid   InterfaceClassGuid;
        public int    Flags;
        public IntPtr Reserved;
    }

    // Stores sp devinfo data data.
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA
    {
        public int    cbSize;
        public Guid   ClassGuid;
        public int    DevInst;
        public IntPtr Reserved;
    }

    // Manages sp device.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public int  cbSize;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public Char[] chrDevicePath;
    }

    // Stores devpropkey data.
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public int  pid;

        // Initializes this instance.
        public DEVPROPKEY(Guid guid, int id)
        {
            fmtid = guid;
            pid   = id;
        }
    }

    #endregion

    #region Dll Imports SetupApi

    // Accesses device information.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, int Flags);

    // Accesses device information.
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h_DevInfo, IntPtr devInfo, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    // Accesses device information.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr h_DevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA k_DetailData, int deviceInterfaceDetailDataSize, out int s32_ReqSize, ref SP_DEVINFO_DATA deviceInfoData);

    // Accesses device information.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr h_DevInfo, ref SP_DEVINFO_DATA deviceInfoData, int property, out int s32_RegDataType, StringBuilder propertyBuffer, int propertyBufferSize, out int s32_ReqSize);

    // Accesses device information.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDevicePropertyW(IntPtr h_DevInfo, ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY PropertyKey, out int PropertyType, StringBuilder propertyBuffer, int propertyBufferSize, out int s32_ReqSize, int flags);

    // Accesses device information.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiOpenDeviceInfoW(IntPtr h_DevInfo, String DeviceInstanceId, IntPtr h_WndParent, int OpenFlags, out SP_DEVINFO_DATA deviceInfoData);

    // Accesses device information.
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr k_GuidDummy, IntPtr h_Wnd);

    // Accesses device information.
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr h_DevInfo);

    #endregion

    const String GUID_CANDLE = "{c15b4308-04d3-11e6-b3ea-6057189e6443}";

    const String GUID_DFU    = "{c25b4308-04d3-11e6-b3ea-6057189e6443}";

    const int DIGCF_PRESENT          = 0x02;
    const int DIGCF_DEVICEINTERFACE  = 0x10;
    const int SPDRP_DEVICEDESC       = 0x00;
    const int SPDRP_FRIENDLYNAME     = 0x0C;
    const int SPDRP_BASE_CONTAINERID = 0x24;

    static DEVPROPKEY DEVPKEY_Device_BusReportedDeviceDesc = new DEVPROPKEY(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    static DEVPROPKEY DEVPKEY_Device_Parent                = new DEVPROPKEY(new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"), 8);

    // Enumerates usb devices.
    public static List<cUsbDevice> EnumerateUsbDevices(bool b_Candlelight)
    {
        Dictionary<String, String> i_Serials = EnumSerialNumbers();

        Guid k_Guid = new Guid(b_Candlelight ? GUID_CANDLE : GUID_DFU);

        IntPtr h_DevInfo = SetupDiGetClassDevsW(ref k_Guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (h_DevInfo == Utils.INVALID_HANDLE_VALUE)
            Utils.ThrowApiError(Marshal.GetLastWin32Error(), "Error {0} enumerating USB devices: {1}");

        IntPtr h_ParentInfo = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (h_ParentInfo == Utils.INVALID_HANDLE_VALUE)
        {
            SetupDiDestroyDeviceInfoList(h_DevInfo);
            Utils.ThrowApiError(Marshal.GetLastWin32Error(), "Error {0} enumerating USB devices: {1}");
        }

        SP_DEVICE_INTERFACE_DATA k_InterfaceData = new SP_DEVICE_INTERFACE_DATA();
        k_InterfaceData.cbSize = Marshal.SizeOf(k_InterfaceData);

        SP_DEVINFO_DATA k_DeviceInfo = new SP_DEVINFO_DATA();
        k_DeviceInfo.cbSize = Marshal.SizeOf(k_DeviceInfo);

        SP_DEVICE_INTERFACE_DETAIL_DATA k_DetailData = new SP_DEVICE_INTERFACE_DETAIL_DATA();

        k_DetailData.cbSize = (IntPtr.Size == 8) ? 8 : 6;

        StringBuilder    s_ProductBuf   = new StringBuilder(128);
        StringBuilder    s_InterfaceBuf = new StringBuilder(128);
        StringBuilder    s_ContainerBuf = new StringBuilder(50);
        StringBuilder    s_ParentBuf    = new StringBuilder(256);
        List<cUsbDevice> i_DeviceList   = new List<cUsbDevice>();

        int s32_Error = 0;
        int s32_ReqSize;
        int s32_RegDataType;
        int s32_PropType;

        for (int s32_Idx = 0; true; s32_Idx++)
        {
            if (!SetupDiEnumDeviceInterfaces(h_DevInfo, IntPtr.Zero, ref k_Guid, s32_Idx, ref k_InterfaceData))
            {
                s32_Error = Marshal.GetLastWin32Error();
                if (s32_Error == (int)eApiError.NO_MORE_ITEMS)
                    s32_Error = 0;
                break;
            }

            if (!SetupDiGetDeviceInterfaceDetailW(h_DevInfo, ref k_InterfaceData, ref k_DetailData, 2000,
                                                    out s32_ReqSize, ref k_DeviceInfo))
            {
                s32_Error = Marshal.GetLastWin32Error();
                continue;
            }

            if (!SetupDiGetDeviceRegistryPropertyW(h_DevInfo, ref k_DeviceInfo, SPDRP_BASE_CONTAINERID, out s32_RegDataType,
                                                   s_ContainerBuf, s_ContainerBuf.Capacity * 2, out s32_ReqSize))
            {
                s32_Error = Marshal.GetLastWin32Error();
                continue;
            }

            if (!SetupDiGetDevicePropertyW(h_DevInfo, ref k_DeviceInfo, ref DEVPKEY_Device_BusReportedDeviceDesc, out s32_PropType,
                                          s_InterfaceBuf, s_InterfaceBuf.Capacity * 2, out s32_ReqSize, 0))
            {
                s32_Error = Marshal.GetLastWin32Error();
                continue;
            }

            if (!SetupDiGetDevicePropertyW(h_DevInfo, ref k_DeviceInfo, ref DEVPKEY_Device_Parent, out s32_PropType,
                                           s_ParentBuf, s_ParentBuf.Capacity * 2, out s32_ReqSize, 0))
            {
                s32_Error = Marshal.GetLastWin32Error();
                continue;
            }

            if (!SetupDiOpenDeviceInfoW(h_ParentInfo, s_ParentBuf.ToString(), IntPtr.Zero, 0, out k_DeviceInfo))
            {
                s32_Error = Marshal.GetLastWin32Error();
                continue;
            }

            if (!SetupDiGetDevicePropertyW(h_ParentInfo, ref k_DeviceInfo, ref DEVPKEY_Device_BusReportedDeviceDesc, out s32_PropType,
                                           s_ProductBuf, s_ProductBuf.Capacity * 2, out s32_ReqSize, 0))
            {
                s32_Error = Marshal.GetLastWin32Error();
                continue;
            }

            cUsbDevice i_UsbDev   = new cUsbDevice();
            i_UsbDev.ms_DevPath   = new String(k_DetailData.chrDevicePath).TrimEnd('\0').ToUpper();
            i_UsbDev.ms_Product   = s_ProductBuf  .ToString();
            i_UsbDev.ms_Interface = s_InterfaceBuf.ToString();
            i_Serials.TryGetValue(s_ContainerBuf.ToString(), out i_UsbDev.ms_SerialNo);

            if (i_UsbDev.ms_Interface == i_UsbDev.ms_Product)
                i_UsbDev.ms_Interface = "[N/A]";

            int Pos = i_UsbDev.ms_DevPath.IndexOf("&MI_0");
            if (Pos > 0)
            {

                int.TryParse(i_UsbDev.ms_DevPath.Substring(Pos + 5, 1), out i_UsbDev.ms32_Interface);
            }

            i_DeviceList.Add(i_UsbDev);
        }

        SetupDiDestroyDeviceInfoList(h_DevInfo);
        SetupDiDestroyDeviceInfoList(h_ParentInfo);

        if (s32_Error != 0)
            Utils.ThrowApiError(s32_Error, "Error {0} enumerating USB devices: {1}");

        i_DeviceList.Sort();
        return i_DeviceList;
    }

    // Lists connected devices.
    static Dictionary<String, String> EnumSerialNumbers()
    {
        String s_RootPath = "System\\CurrentControlSet\\Enum\\USB\\VID_1D50&PID_606F";

        using (RegistryKey i_RootKey = Registry.LocalMachine.OpenSubKey(s_RootPath))
        {
            Dictionary<String, String> i_Serials = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            if (i_RootKey == null)
                return i_Serials;

            foreach (String s_Serial in i_RootKey.GetSubKeyNames())
            {
                String s_RegPath = "HKEY_LOCAL_MACHINE\\" + s_RootPath + "\\" + s_Serial;

                Object o_Container = Registry.GetValue(s_RegPath, "ContainerID", null);

                if (o_Container is String)
                    i_Serials[(String)o_Container] = s_Serial;
            }

            return i_Serials;
        }
    }
}
}
