

using System;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using eApiError           = CANable.Utils.eApiError;
using cInterface          = CANable.WinUSB.cInterface;
using cPipeIn             = CANable.WinUSB.cPipeIn;
using cUsbInPacket        = CANable.WinUSB.cUsbInPacket;
using cPipeOut            = CANable.WinUSB.cPipeOut;
using ePipeType           = CANable.WinUSB.ePipeType;
using ePipePolicy         = CANable.WinUSB.ePipePolicy;
using eDirection          = CANable.WinUSB.eDirection;
using eSetupType          = CANable.WinUSB.eSetupType;
using eSetupRecip         = CANable.WinUSB.eSetupRecip;
using eDfuRequest         = CANable.WinUSB.eDfuRequest;
using kDeviceDescriptor   = CANable.WinUSB.kDeviceDescriptor;

namespace CANable
{

// Manages candlelight.
public class Candlelight : IDisposable
{
    #region enums legacy protocol

    // Defines e usb request values.
    enum eUsbRequest : byte
    {
        SetHostFormat = 0,
        SetBitTiming,
        SetDeviceMode,
        BerrReport,
        GetCapability,
        GetDeviceVersion,
        GetTimestamp,
	    Identify,
	    GetUserID,
	    SetUserID,
	    SetBitTimingFD,
	    GetCapabilityFD,
	    SetTermination,
	    GetTermination,
	    GetState,

        GetBoardInfo = 20,
        SetFilter,
        GetLastError,
        SetBusLoadReport,
        SetPinStatus,
        GetPinStatus,
        ReadFlash,
        WriteFlash,
    }

    // Defines e dev mode values.
    enum eDevMode : int
    {
        Reset = 0,
        Start,
    }

    // Defines e device flags values.
    [FlagsAttribute]
    public enum eDeviceFlags : int
    {
        None                = 0,
        ListenOnly          = 0x00001,
        Loopback            = 0x00002,
        TripleSample        = 0x00004,
        OneShot             = 0x00008,
        HwTimestamp         = 0x00010,
        Identify            = 0x00020,
        UserID              = 0x00040,
        PadPktsToMaxPktSize = 0x00080,
        CanFD               = 0x00100,
        QuirkLPC546XX       = 0x00200,
        BitTimingFD         = 0x00400,
        Termination         = 0x00800,
        BerrReporting       = 0x01000,
        GetState            = 0x02000,

        ProtocolElmue       = 0x04000,
        SendUsbBlobs        = 0x08000,
        LegacyFilters       = 0x10000,
    }

    // Defines e termination values.
    enum eTermination : int
    {
	    Off = 0,
	    On  = 1,
    }

    // Defines e can id flags values.
    [FlagsAttribute]
    enum eCanIdFlags : uint
    {
        Extended = 0x80000000,
        RTR      = 0x40000000,
        Error    = 0x20000000,
        MASK_11  = 0x000007FF,
        MASK_29  = 0x1FFFFFFF,
    };

    // Defines e frame flags values.
    [FlagsAttribute]
    public enum eFrameFlags : byte
    {
        Overflow = 0x01,
        FDF      = 0x02,
        BRS      = 0x04,
        ESI      = 0x08,
    };

    // Manages e err.
    [FlagsAttribute]
    public enum eErrFlagsCanID : int
    {

        Tx_Timeout           = 0x0001,
        Arbitration_lost     = 0x0002,

        Controller_problem   = 0x0004,
        Protocol_violation   = 0x0008,
        Transceiver_error    = 0x0010,

        No_ACK_received      = 0x0020,
        Bus_Off              = 0x0040,
        Bus_Error            = 0x0080,
        Controller_restarted = 0x0100,
        CRC_Error            = 0x0200,
        MASK_Display         = 0xFFFF & ~(Tx_Timeout | Bus_Off | Controller_problem | Protocol_violation | Transceiver_error),

    }

    // Manages e err.
    [FlagsAttribute]
    enum eErrFlagsByte1 : byte
    {
        Rx_Buffer_Overflow   = 0x01,
        Tx_Buffer_Overflow   = 0x02,
        Rx_Warning_Level     = 0x04,
        Tx_Warning_Level     = 0x08,
        Rx_Bus_Passive       = 0x10,
        Tx_Bus_Passive       = 0x20,
        Bus_is_back_active   = 0x40,
    }

    // Manages e err.
    [FlagsAttribute]
    enum eErrFlagsByte2 : byte
    {
        Single_bit_error          = 0x01,
        Frame_format_error        = 0x02,
        Bit_stuffing_error        = 0x04,
        Dominant_bit_error        = 0x08,
        Recessive_bit_error       = 0x10,
        Bus_overload              = 0x20,
        Active_error_announcement = 0x40,
        Transmission_error        = 0x80,
    }

    // Manages e err.
    enum eErrFlagsByte3 : byte
    {
        at_ID_bits_28__21    = 0x02,
        at_SOF               = 0x03,
        at_RTR_substitute    = 0x04,
        at_IDE_bit           = 0x05,
        at_ID_bits_20__18    = 0x06,
        at_ID_bits_17__13    = 0x07,
        at_CRC_Sequence      = 0x08,
        at_Reserved_bit_0    = 0x09,
        in_data_section      = 0x0A,
        at_DLC_bit           = 0x0B,
        at_RTR_bit           = 0x0C,
        at_Reserved_bit_1    = 0x0D,
        at_ID_bits_4__0      = 0x0E,
        at_ID_bits_12__5     = 0x0F,
        Intermission         = 0x12,
        at_CRC_delimiter     = 0x18,
        at_ACK_slot          = 0x19,
        at_EOF               = 0x1A,
        at_ACK_delimiter     = 0x1B,
    }

    // Manages e err.
    enum eErrFlagsByte4_Hi : byte
    {
        CAN__H_No_wire         = 0x04,
        CAN__H_Shortcut_to_Bat = 0x05,
        CAN__H_Shortcut_to_VCC = 0x06,
        CAN__H_Shortcut_to_GND = 0x07,
        MASK                   = 0x0F,
    }

    // Manages e err.
    enum eErrFlagsByte4_Lo : byte
    {
        CAN__L_No_wire         = 0x40,
        CAN__L_Shortcut_to_Bat = 0x50,
        CAN__L_Shortcut_to_VCC = 0x60,
        CAN__L_Shortcut_to_GND = 0x70,
        CAN__L_Shortcut_CAN__H = 0x80,
        MASK                   = 0xF0,
    }

    #endregion

    #region enums ElmüSoft protocol

    // Defines e board flags values.
    [FlagsAttribute]
    public enum eBoardFlags : uint
    {
        Quartz_In_Use  = 0x00000001,
        USB_HighSpeed  = 0x00000002,
    }

    // Defines e feedback values.
    public enum eFeedback
    {
        None           =  0,
        Success        =  2,

        Invalid_command = '1',
        Invalid_parameter,
        Adapter_must_be_open,
        Adapter_must_be_closed,
        Error_from_HAL,
        Unsupported_feature,
        Tx_buffer_overflow,
        Bus_is_off,
        No_Tx_in_silent_mode,
        Baudrate_not_set,
        Option_bytes_programming_failed,
        Please_reconnect_the_USB_cable,
        Parameter_outside_valid_range,
    }

    // Manages e filter.
    enum eFilterOperation : byte
    {
        HostClear = 0,
        HostPass_11,
        HostPass_29,

        BridgeClear = 10,
        BridgePass_11,
        BridgePass_29,
        BridgeBlock_11,
        BridgeBlock_29,
    }

    // Manages e pin.
    enum ePinOperation : ushort
    {
        Reset = 0,
        Set,
        Tristate,
        PullDown,
        PullUp,
        Disable,
        Enable,
    }

    // Defines e pin id values.
    enum ePinID : ushort
    {
        BOOT0 = 1,
    }

    // Defines e pin status values.
    [FlagsAttribute]
    enum ePinStatus : ushort
    {
        High     = 0x0001,
        Enabled  = 0x0002,
    }

    // Defines e message type values.
    public enum eMessageType : byte
    {

        TxFrame = 10,

        TxEcho,
        RxFrame,
        Error,
        String,
        Busload,
        TxBlob,
        RxBlob,
    }

    // Manages e error.
    [FlagsAttribute]
    enum eErrorAppFlags : byte
    {
        None             = 0,
        Rx_Failed        = 0x01,
        Tx_Failed        = 0x02,
        CAN_Tx_overflow  = 0x04,
        USB_IN_overflow  = 0x08,
        Tx_Timeout       = 0x10,

    }

    // Defines e bus status values.
    public enum eBusStatus : byte
    {
        Active   = 0x00,
        Warning  = 0x10,
        Passive  = 0x20,
        Off      = 0x30,

    }

    // Defines e error level values.
    public enum eErrorLevel
    {
        Low,
        Medium,
        High,
    }

    #endregion

    #region structs legacy protcol

    // Stores k timing min max data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct kTimingMinMax
    {
	    public int    ms32_Seg1_Min;
	    public int    ms32_Seg1_Max;
	    public int    ms32_Seg2_Min;
	    public int    ms32_Seg2_Max;
	    public int    ms32_Sjw_Max;
	    public int    ms32_Brp_Min;
	    public int    ms32_Brp_Max;
	    public UInt32 ms32_Brp_Inc;
    }

    // Manages k capability.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct kCapabilityClassic
    {
        public eDeviceFlags  me_Feature;
        public int           ms32_CanClock;
        public kTimingMinMax mk_TimeMinMax;
    }

    // Stores k capability fd data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct kCapabilityFD
    {
	    public eDeviceFlags  me_Feature;
	    public int           ms32_CanClock;
	    public kTimingMinMax mk_NominalMinMax;
	    public kTimingMinMax mk_DataMinMax;
    }

    // Stores k device mode data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct kDeviceMode
    {
        public eDevMode     me_Mode;
        public eDeviceFlags me_Flags;

        // Initializes this instance.
        public kDeviceMode(eDevMode e_Mode, eDeviceFlags e_Flags)
        {
            me_Mode  = e_Mode;
            me_Flags = e_Flags;
        }
    }

    // Stores k device version data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct kDeviceVersion
    {
        public Byte   mu8_HAL_Version_High;
        public Byte   mu8_HAL_Version_Mid;
        public Byte   mu8_HAL_Version_Low;
        public Byte   mu8_Icount;
        public UInt32 mu32_SoftVersionBcd;
        public UInt32 mu32_HardVersionBcd;

        public String HalVersion
        {

            get { return String.Format("{0}.{1}.{2}", mu8_HAL_Version_High, mu8_HAL_Version_Mid, mu8_HAL_Version_Low); }
        }
        public String SoftVersion
        {
            get { return Utils.FormatBcdVersion(mu32_SoftVersionBcd); }
        }
        public String HardVersion
        {
            get { return Utils.FormatBcdVersion(mu32_HardVersionBcd); }
        }
        public int ChannelCount
        {
            get { return mu8_Icount + 1; }
        }
    }

    // Stores k bit timing data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct kBitTiming
    {
        public int ms32_Prop;
        public int ms32_Seg1;
        public int ms32_Seg2;
        public int ms32_Sjw;
        public int ms32_Brp;

        // Formats the value as text.
        public override string ToString()
        {
            return String.Format("BRP: {0}, Seg1: {1}, Seg2: {2}, SJW: {3}", ms32_Brp, ms32_Seg1, ms32_Seg2, ms32_Sjw);
        }
    }

    #endregion

    #region structs ElmüSoft protocol

    // Stores k board info data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct kBoardInfo
    {
        public UInt16      mu16_McuDeviceID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public Byte[]      mu8_McuName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public Byte[]      mu8_BoardName;
        public eBoardFlags me_BoardFlags;

        public String McuName
        {
            get { return Encoding.UTF8.GetString(mu8_McuName).TrimEnd('\0'); }
        }
        public String BoardName
        {
            get { return Encoding.UTF8.GetString(mu8_BoardName).TrimEnd('\0'); }
        }
    }

    // Stores k filter data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct kFilter
    {

        public eFilterOperation me_Operation;
        public int              ms32_Filter;
        public int              ms32_Mask;

        public Byte             mu8_Index;
        public Byte             mu8_DestChannel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public Byte[]           mu8_Reserved;
    }

    // Stores k pin status data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct kPinStatus
    {
        public ePinOperation me_Operation;
        public ePinID        me_PinID;
        public int           ms32_Reserved1;
        public int           ms32_Reserved2;
    }

    // Manages c blob.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    class cBlob
    {
        public Byte         mu8_FrameCount;
        public eMessageType me_MesgType;
    }

    // Manages c header.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class cHeader
    {
        public Byte         mu8_Size;
        public eMessageType me_MesgType;

        // Gets min size.
        public virtual int GetMinSize(bool b_McuTimestamp)
        {
            return 2;
        }
    }

    // Manages c tx frame elmue.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private class cTxFrameElmue : cHeader
    {
        public eFrameFlags  me_Flags;
        public UInt32       mu32_CanID;
        public Byte         mu8_Marker;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public Byte[]       mu8_Data;

        // Gets min size.
        public override int GetMinSize(bool b_McuTimestamp)
        {
            return (int)Marshal.OffsetOf(GetType(), "mu8_Data");
        }

        // Initializes this instance.
        public cTxFrameElmue(CanPacket i_Packet)
        {
            mu8_Size    = (Byte)(GetMinSize(false) + i_Packet.mi_Data.Count);
            me_MesgType = eMessageType.TxFrame;
            mu32_CanID  = (UInt32)i_Packet.ms32_ID;
            if (i_Packet.mb_29bit) mu32_CanID |= (UInt32)eCanIdFlags.Extended;
            if (i_Packet.mb_RTR)   mu32_CanID |= (UInt32)eCanIdFlags.RTR;
            if (i_Packet.mb_FDF)   me_Flags   |= eFrameFlags.FDF;
            if (i_Packet.mb_BRS)   me_Flags   |= eFrameFlags.BRS;

            mu8_Data = new Byte[64];
            Array.Copy(i_Packet.mi_Data.ToArray(), mu8_Data, i_Packet.mi_Data.Count);
        }
    }

    // Manages c rx frame elmue.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class cRxFrameElmue : cHeader
    {
        public eFrameFlags  me_Flags;
        public UInt32       mu32_CanID;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4 + 64)]
        public Byte[]       mu8_TimeStampAndData;

        // Gets min size.
        public override int GetMinSize(bool b_McuTimestamp)
        {

            int s32_MinSize = (int)Marshal.OffsetOf(GetType(), "mu8_TimeStampAndData");
            if (b_McuTimestamp) s32_MinSize += 4;
            return s32_MinSize;
        }

        public UInt32 Timestamp
        {
            get { return BitConverter.ToUInt32(mu8_TimeStampAndData, 0); }
        }
    }

    // Manages c tx echo elmue.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class cTxEchoElmue : cHeader
    {
        public Byte    mu8_Marker;

        public UInt32  mu32_Timestamp;

        // Gets min size.
        public override int GetMinSize(bool b_McuTimestamp)
        {
            int s32_MinSize = (int)Marshal.OffsetOf(GetType(), "mu32_Timestamp");
            if (b_McuTimestamp) s32_MinSize += 4;
            return s32_MinSize;
        }
    };

    // Manages c error elmue.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class cErrorElmue : cHeader
    {
        public  eErrFlagsCanID  me_ErrID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public  Byte[]          mu8_ErrData;

        public  UInt32          mu32_Timestamp;

        // Gets min size.
        public override int GetMinSize(bool b_McuTimestamp)
        {
            int s32_MinSize = (int)Marshal.OffsetOf(GetType(), "mu32_Timestamp");
            if (b_McuTimestamp) s32_MinSize += 4;
            return s32_MinSize;
        }
    };

    // Manages c string elmue.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class cStringElmue : cHeader
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 200)]

        public Byte[]  mu8_AsciiMsg;

        // Gets min size.
        public override int GetMinSize(bool b_McuTimestamp)
    {
            return (int)Marshal.OffsetOf(GetType(), "mu8_AsciiMsg");
        }

        public String Message
        {
            get { return Encoding.ASCII.GetString(mu8_AsciiMsg, 0, mu8_Size - GetMinSize(false)); }
        }
    };

    // Manages c busload elmue.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class cBusloadElmue : cHeader
    {
        public Byte mu8_BusLoad;

        // Gets min size.
        public override int GetMinSize(bool b_McuTimestamp)
        {
            return Marshal.SizeOf(GetType());
        }
    };

    #endregion

    #region Firmware Update

    // Defines e dfu status values.
    enum eDfuStatus : byte
    {
        OK = 0,
        ErrTarget,
        ErrFile,
        ErrWrite,
        ErrErase,
        ErrCheckErased,
        ErrProg,
        ErrVerify,
        ErrAddress,
        ErrNotDone,
        ErrFirmware,
        ErrVendor,
        ErrUSBR,
        ErrPOR,
        ErrUnknown,
        ErrStallEP,
    }

    // Defines e dfu state values.
    enum eDfuState : byte
    {
        AppIdle = 0,
        AppDetach,
        DfuIdle,
        DownloadSync,
        DownloadBusy,
        DownloadIdle,
        ManifestSync,
        Manifest,
        ManifestWaitReset,
        UploadIdle,
        Error,

        UploadSync  = 0x91,
        UploadBusy  = 0x92,
    }

    // Stores k dfu status data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct kDfuStatus
    {
        public eDfuStatus  me_Status;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public Byte[]      mu8_PollTimeout;
        public eDfuState   me_State;
        public Byte        mu8_StringIdx;
    }

    #endregion

    #region struct kDevInfo

    // Stores k dev info data.
    public struct kDevInfo
    {
        public String               ms_Vendor;
        public String               ms_Product;
        public String               ms_SerialNo;
        public String               ms_Interface;
        public Byte                 mu8_EndpointIN;
        public Byte                 mu8_EndpointOUT;
        public UInt16               mu16_MaxPackSizeIN;
        public UInt16               mu16_MaxPackSizeOUT;
        public bool                 mb_IsElmueSoft;
        public bool                 mb_SupportsFD;
        public Byte                 mu8_Channel;
        public kDeviceDescriptor    mk_DeviceDescr;
        public kCapabilityClassic   mk_Capability;
        public kCapabilityFD        mk_CapabilityFD;
        public kDeviceVersion       mk_DeviceVersion;
        public kBoardInfo           mk_BoardInfo;
    };

    #endregion

    #region class CanPacket

    // Manages can packet.
    public class CanPacket
    {
        public int        ms32_ID = -1;
        public bool       mb_29bit;
        public bool       mb_RTR;
        public bool       mb_FDF;
        public bool       mb_BRS;
        public bool       mb_ESI;
        public List<Byte> mi_Data = new List<Byte>();

        // Copies the current object.
        public CanPacket Clone()
        {
            CanPacket i_Clone = new CanPacket();
            i_Clone.ms32_ID  = ms32_ID;
            i_Clone.mb_29bit = mb_29bit;
            i_Clone.mb_RTR   = mb_RTR;
            i_Clone.mb_FDF   = mb_FDF;
            i_Clone.mb_BRS   = mb_BRS;
            i_Clone.mb_ESI   = mb_ESI;
            i_Clone.mi_Data.AddRange(mi_Data);
            return i_Clone;
        }

        // Formats the value as text.
        public override string ToString()
        {
            String s_Frame;
            if (mb_29bit) s_Frame = String.Format("{0:X8}: ", ms32_ID & (int)eCanIdFlags.MASK_29);
            else          s_Frame = String.Format("{0:X3}: ", ms32_ID & (int)eCanIdFlags.MASK_11);

            if (mb_RTR)
            {

                if (mi_Data.Count > 0) s_Frame += String.Format("RTR [{0}]", mi_Data[0]);
                else                   s_Frame += "RTR [0]";
            }
            else
            {
                s_Frame += Utils.BytesToHex(mi_Data.ToArray());

                if (mb_FDF || mb_BRS || mb_ESI) s_Frame += " -";

                if (mb_FDF) s_Frame += " FDF";
                if (mb_BRS) s_Frame += " BRS";
                if (mb_ESI) s_Frame += " ESI";
            }
            return s_Frame;
        }
    }

    #endregion

    #region class cDetail

    // Manages c detail.
    public class cDetail
    {
        String ms_Name;
        String ms_Value;

        // Initializes this instance.
        public cDetail(String s_Name, String s_Value)
        {
            ms_Name  = s_Name + ':';
            ms_Value = s_Value;
        }

        // Formats the requested value.
        public String Format(int s32_ColumnWidth)
        {
            return ms_Name.PadRight(s32_ColumnWidth) + ms_Value;
        }
    };

    #endregion

    public const Byte FIRMW_UPDATE_INTERFACE = 1;

    const int MIN_FIRMWARE = 0x260618;

    const int MAX_BLOB_SIZE = 2048;

    const int CAN_QUEUE_SIZE = 64;

    WinUSB           mi_WinUSB;
    kDevInfo         mk_Info;
    List<cDetail>    mi_Details;
    cPipeIn          mi_PipeIn;
    cPipeOut         mi_PipeOut;
    Byte             mu8_Channel;
    bool             mb_InitDone;
    bool             mb_Started;
    bool             mb_BaudFDSet;
    Stopwatch        mi_TxOverflow;
    Byte             mu8_EchoMarker;
    CanPacket[]      mi_TxEcho;
    bool             mb_EnableTxEcho;
    bool             mb_McuTimestamp;
    Int64            ms64_LastMcuStamp;
    Int64            ms64_McuRollOver;
    int              ms32_BlobOffset;
    int              ms32_BlobFrames;
    cUsbInPacket     mi_UsbInPacket;

    public long RxDiscardedPackets
    {
        get { return mi_PipeIn != null ? mi_PipeIn.DiscardedPackets : 0; }
    }

    public kDevInfo DeviceInfo
    {
        get { return mk_Info; }
    }
    public List<cDetail> DeviceDetails
    {
        get { return mi_Details; }
    }

    // Formats the value as text.
    public override string ToString()
    {
        if (mi_WinUSB != null)
            return mi_WinUSB.ToString();
        else
            return "Not open";
    }

    // Releases native resources.
    ~Candlelight()
    {
        Dispose();
    }

    // Releases held resources.
    public void Dispose()
    {
        if (mi_WinUSB != null)
        {
            try { Reset(); }
            catch {}

            mi_WinUSB.Dispose();
            mi_WinUSB = null;
        }
        mb_InitDone = false;
        mi_TxEcho   = null;
    }

    // Opens the requested resource.
    public void Open(String s_NtPath)
    {
        if (mi_WinUSB != null)
            throw new Exception("The Candlelight adapter is already open");

        mk_Info           = new kDevInfo();
        mi_Details        = new List<cDetail>();
        mi_TxOverflow     = new Stopwatch();
        mb_InitDone       = false;
        mb_BaudFDSet      = false;
        mb_Started        = false;
        mb_EnableTxEcho   = true;
        ms64_LastMcuStamp = 0;
        ms64_McuRollOver  = 0;
        ms32_BlobOffset   = 0;
        ms32_BlobFrames   = 0;

        mi_WinUSB = new WinUSB();
        mi_WinUSB.Open(s_NtPath, 500);

        mk_Info.ms_Vendor      = mi_WinUSB.Vendor;
        mk_Info.ms_Product     = mi_WinUSB.Product;
        mk_Info.ms_SerialNo    = mi_WinUSB.SerialNo;
        mk_Info.ms_Interface   = mi_WinUSB.Interface.String;
        mk_Info.mk_DeviceDescr = mi_WinUSB.DeviceDescriptor;

        mi_Details.Add(new cDetail("Device Path",          '"' + s_NtPath             + '"'));
        mi_Details.Add(new cDetail("USB Vendor",           '"' + mk_Info.ms_Vendor    + '"'));
        mi_Details.Add(new cDetail("USB Product",          '"' + mk_Info.ms_Product   + '"'));
        mi_Details.Add(new cDetail("USB Serial  Nº",       '"' + mk_Info.ms_SerialNo  + '"'));
        mi_Details.Add(new cDetail("USB Interface Name",   '"' + mk_Info.ms_Interface + '"'));
        mi_Details.Add(new cDetail("USB Vendor  ID",       mk_Info.mk_DeviceDescr.idVendor .ToString("X4")));
        mi_Details.Add(new cDetail("USB Product ID",       mk_Info.mk_DeviceDescr.idProduct.ToString("X4")));
        mi_Details.Add(new cDetail("USB Device Version",   Utils.FormatBcdVersion(mk_Info.mk_DeviceDescr.bcdDevice)));

        if (mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
        {

            if (mi_WinUSB.Interface.EndpointCount != 0)
                throw new Exception("The USB device is not a valid Candlelight adapter");

            mb_InitDone = true;
            return;
        }

        mu8_Channel = mi_WinUSB.Interface.Number;
        if (mu8_Channel > 0)
            mu8_Channel --;

        mk_Info.mu8_Channel = mu8_Channel;

        if (mi_WinUSB.Interface.EndpointCount != 2)
            throw new Exception("The USB device is not a valid Candlelight adapter");

        mi_PipeIn  = mi_WinUSB.Interface.GetPipeIn (ePipeType.Bulk);
        mi_PipeOut = mi_WinUSB.Interface.GetPipeOut(ePipeType.Bulk);
        if (mi_PipeIn == null || mi_PipeOut == null)
            throw new Exception("The USB device is not a valid Candlelight adapter");

        mk_Info.mu8_EndpointIN      = mi_PipeIn .Endpoint;
        mk_Info.mu8_EndpointOUT     = mi_PipeOut.Endpoint;
        mk_Info.mu16_MaxPackSizeIN  = mi_PipeIn .MaxPacketSize;
        mk_Info.mu16_MaxPackSizeOUT = mi_PipeOut.MaxPacketSize;

        mi_Details.Add(new cDetail("USB Endpoint CTRL", String.Format(    "00,  max packet size: {0} byte", mk_Info.mk_DeviceDescr.bMaxPacketSize0)));
        mi_Details.Add(new cDetail("USB Endpoint IN",   String.Format("{0:X2},  max packet size: {1} byte", mk_Info.mu8_EndpointIN,  mk_Info.mu16_MaxPackSizeIN)));
        mi_Details.Add(new cDetail("USB Endpoint OUT",  String.Format("{0:X2},  max packet size: {1} byte", mk_Info.mu8_EndpointOUT, mk_Info.mu16_MaxPackSizeOUT)));

        mi_PipeIn.SetPolicy(ePipePolicy.RawIO, true);

        mi_PipeOut.SetTransferTimeout(500);

        Reset();

        mk_Info.mk_Capability = CtrlTransfer<kCapabilityClassic>((Byte)eUsbRequest.GetCapability, eDirection.In, mu8_Channel);

        mk_Info.mb_IsElmueSoft =  (mk_Info.mk_Capability.me_Feature & eDeviceFlags.ProtocolElmue) > 0;
        mk_Info.mb_SupportsFD  = ((mk_Info.mk_Capability.me_Feature & eDeviceFlags.CanFD)         > 0 &&
                                  (mk_Info.mk_Capability.me_Feature & eDeviceFlags.BitTimingFD)   > 0);

        if (mk_Info.mb_SupportsFD)
            mk_Info.mk_CapabilityFD = CtrlTransfer<kCapabilityFD> ((Byte)eUsbRequest.GetCapabilityFD,  eDirection.In, mu8_Channel);

        mk_Info.mk_DeviceVersion    = CtrlTransfer<kDeviceVersion>((Byte)eUsbRequest.GetDeviceVersion, eDirection.In, mu8_Channel);

        mi_Details.Add(new cDetail("Hardware Version",     mk_Info.mk_DeviceVersion.HardVersion));
        mi_Details.Add(new cDetail("Firmware Version",     mk_Info.mk_DeviceVersion.SoftVersion));

        if (mk_Info.mb_IsElmueSoft)
            mi_Details.Add(new cDetail("HAL      Version", mk_Info.mk_DeviceVersion.HalVersion));

        mi_Details.Add(new cDetail("Firmware Type",        mk_Info.mb_IsElmueSoft ? "CANable 2.5" : "Legacy"));
        mi_Details.Add(new cDetail("Supports CAN FD",      mk_Info.mb_SupportsFD  ? "Yes"         : "No"));

        if (!mk_Info.mb_IsElmueSoft)
        {
            mi_Details.Add(new cDetail("CAN Clock", String.Format("{0} MHz", mk_Info.mk_Capability.ms32_CanClock / 1000000)));
            throw new Exception("This class supports only devices that have the CANable 2.5 firmware from ElmüSoft.");
        }

        mk_Info.mk_BoardInfo = CtrlTransfer<kBoardInfo>((Byte)eUsbRequest.GetBoardInfo, eDirection.In, mu8_Channel);
        UInt16 u16_PinStatus = CtrlTransfer<UInt16>((Byte)eUsbRequest.GetPinStatus, eDirection.In, (UInt16)ePinID.BOOT0);
        bool   b_Enabled     = (u16_PinStatus & (UInt16)ePinStatus.Enabled) > 0;

        bool b_UseQuartz = (mk_Info.mk_BoardInfo.me_BoardFlags & eBoardFlags.Quartz_In_Use) > 0;

        mi_Details.Add(new cDetail("Target Board",  mk_Info.mk_BoardInfo.BoardName));
        mi_Details.Add(new cDetail("Processor", String.Format("{0}, CAN Clock: {1} MHz, MCU DeviceID: 0x{2:X}",
                                                              mk_Info.mk_BoardInfo.McuName,
                                                              mk_Info.mk_Capability.ms32_CanClock / 1000000,
                                                              mk_Info.mk_BoardInfo.mu16_McuDeviceID)));
        mi_Details.Add(new cDetail("Quartz in use", b_UseQuartz ? "Yes" : "No"));
        mi_Details.Add(new cDetail("CAN Channel",   String.Format("{0} of {1}", mu8_Channel + 1, mk_Info.mk_DeviceVersion.ChannelCount)));
        mi_Details.Add(new cDetail("Pin BOOT0",     b_Enabled ? "Enabled" : "Disabled"));

        if (mk_Info.mk_DeviceVersion.mu32_SoftVersionBcd < MIN_FIRMWARE)
            throw new Exception("Please upload the latest firmware.");

        Debug.Assert(mk_Info.mk_DeviceVersion.mu32_SoftVersionBcd == MIN_FIRMWARE, "Update MIN_FIRMWARE to the latest firmware version!");

        mi_TxEcho = new CanPacket[256];
        mi_PipeIn.StartThread(MAX_BLOB_SIZE);

        mb_InitDone = true;
    }

    // Enables transmit echoes.
    public void EnableTxEcho(bool b_Enable)
    {
        mb_EnableTxEcho = b_Enable;
    }

    // Sets bitrate.
    public void SetBitrate(bool b_FD, int s32_BRP, int s32_Seg1, int s32_Seg2, out String s_Display)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        if (b_FD && !mk_Info.mb_SupportsFD)
            throw new Exception("The board does not support CAN FD.");

        kBitTiming k_Timing;
        k_Timing.ms32_Brp  = s32_BRP;
        k_Timing.ms32_Prop = 0;
        k_Timing.ms32_Seg1 = s32_Seg1;
        k_Timing.ms32_Seg2 = s32_Seg2;
        k_Timing.ms32_Sjw  = Math.Min(s32_Seg1, s32_Seg2);

        eUsbRequest e_Requ = b_FD ? eUsbRequest.SetBitTimingFD : eUsbRequest.SetBitTiming;
        CtrlTransfer((Byte)e_Requ, eDirection.Out, mu8_Channel, k_Timing);

        int s32_TotTQ  = 1 + s32_Seg1 + s32_Seg2;
        int s32_Baud   = mk_Info.mk_Capability.ms32_CanClock / s32_BRP / s32_TotTQ;
        int s32_Sample = 1000 * (1 + s32_Seg1) / s32_TotTQ;

        String s_Unit = "";
                if (s32_Baud >= 1000000 && (s32_Baud % 1000000) == 0) { s32_Baud /= 1000000; s_Unit = "M"; }
        else if (s32_Baud >= 1000    && (s32_Baud % 1000)    == 0) { s32_Baud /= 1000;    s_Unit = "k"; }

        String s_Type = b_FD ? "Data   " : "Nominal";
        s_Display = String.Format("{0} Baudrate: {1}{2}, Samplepoint: {3}.{4}%", s_Type, s32_Baud, s_Unit, s32_Sample / 10, s32_Sample % 10);

        if (b_FD) mb_BaudFDSet = true;
    }

    // Adds host filter.
    public void AddHostFilter(bool b_29bit, int s32_Filter, int s32_Mask)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        kFilter k_Filter;
        k_Filter.ms32_Filter     = s32_Filter;
        k_Filter.ms32_Mask       = s32_Mask;
        k_Filter.me_Operation    = b_29bit ? eFilterOperation.HostPass_29 : eFilterOperation.HostPass_11;
        k_Filter.mu8_DestChannel = 0;
        k_Filter.mu8_Index       = 0;
        k_Filter.mu8_Reserved    = new Byte[6];

        CtrlTransfer((Byte)eUsbRequest.SetFilter, eDirection.Out, mu8_Channel, k_Filter);
    }

    // Sets bridge filter.
    public void SetBridgeFilter(Byte u8_FilterIndex, Byte u8_DestChannel, bool b_Enable, bool b_Block, bool b_29bit, int s32_Filter, int s32_Mask)
    {
        kFilter k_Filter;
        k_Filter.ms32_Filter     = s32_Filter;
        k_Filter.ms32_Mask       = s32_Mask;
        k_Filter.me_Operation    = eFilterOperation.BridgeClear;
        k_Filter.mu8_DestChannel = u8_DestChannel;
        k_Filter.mu8_Index       = u8_FilterIndex;
        k_Filter.mu8_Reserved    = new Byte[6];

        if (b_Enable)
        {
            if (b_Block)
            {
                if (b_29bit) k_Filter.me_Operation = eFilterOperation.BridgeBlock_29;
                else         k_Filter.me_Operation = eFilterOperation.BridgeBlock_11;
            }
            else
            {
                if (b_29bit) k_Filter.me_Operation = eFilterOperation.BridgePass_29;
                else         k_Filter.me_Operation = eFilterOperation.BridgePass_11;
            }
        }

        CtrlTransfer((Byte)eUsbRequest.SetFilter, eDirection.Out, mu8_Channel, k_Filter);
    }

    // Starts the operation.
    public void Start(eDeviceFlags e_Flags)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        e_Flags |= eDeviceFlags.ProtocolElmue;
        if ((mk_Info.mk_Capability.me_Feature & eDeviceFlags.SendUsbBlobs) > 0)
            e_Flags |= eDeviceFlags.SendUsbBlobs;

        CtrlTransfer((Byte)eUsbRequest.SetDeviceMode, eDirection.Out, mu8_Channel, new kDeviceMode(eDevMode.Start, e_Flags));

        mb_McuTimestamp = (e_Flags & eDeviceFlags.HwTimestamp) > 0;
        mb_Started = true;
    }

    // Resets the current state.
    public void Reset()
    {
        mb_Started = false;

        CtrlTransfer((Byte)eUsbRequest.SetDeviceMode, eDirection.Out, mu8_Channel,
                        new kDeviceMode(eDevMode.Reset, eDeviceFlags.ProtocolElmue));
    }

    // Identifies device firmware.
    public void Identify(bool b_Blink)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        int s32_Mode = b_Blink ? 1 : 0;
        CtrlTransfer((Byte)eUsbRequest.Identify, eDirection.Out, mu8_Channel, s32_Mode);
    }

    // Enables the selected feature.
    public void EnableBusLoadReport(Byte u8_Interval)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        CtrlTransfer((Byte)eUsbRequest.SetBusLoadReport, eDirection.Out, mu8_Channel, u8_Interval);
    }

    // Disables the boot pin.
    public void DisableBootPin()
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        kPinStatus k_PinStatus;
        k_PinStatus.me_Operation    = ePinOperation.Disable;
        k_PinStatus.me_PinID        = ePinID.BOOT0;
        k_PinStatus.ms32_Reserved1  = 0;
        k_PinStatus.ms32_Reserved2  = 0;
        CtrlTransfer((Byte)eUsbRequest.SetPinStatus, eDirection.Out, mu8_Channel, k_PinStatus);
    }

    // Checks boot pin enabled.
    public bool IsBootPinEnabled()
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        UInt16  u16_PinStatus = CtrlTransfer<UInt16>((Byte)eUsbRequest.GetPinStatus, eDirection.In, (UInt16)ePinID.BOOT0);
        return (u16_PinStatus & (UInt16)ePinStatus.Enabled) > 0;
    }

    // Writes flash.
    public void WriteFlash(Byte u8_Segment, Byte[] u8_Buffer)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        CtrlTransfer<Byte[]>((Byte)eUsbRequest.WriteFlash, eDirection.Out, u8_Segment, u8_Buffer);
    }

    // Reads flash.
    public Byte[] ReadFlash(Byte u8_Segment)
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the Candlelight interface.");

        return CtrlTransfer<Byte[]>((Byte)eUsbRequest.ReadFlash, eDirection.In, u8_Segment);
    }

    // Transfers USB control data.
    T CtrlTransfer<T>(Byte u8_Request, eDirection e_Dir, UInt16 u16_Value, T k_Struct = default(T))
    {
        eSetupType e_Type;
        String s_Request;
        if (mi_WinUSB.Interface.Number == FIRMW_UPDATE_INTERFACE)
        {
            e_Type = eSetupType.Class;
            s_Request = ((eDfuRequest)u8_Request).ToString();
        }
        else
        {
            e_Type = eSetupType.Vendor;
            s_Request = ((eUsbRequest)u8_Request).ToString();
        }

        UInt16 wValue = u16_Value;

        UInt16 wIndex = mi_WinUSB.Interface.Number;

        int s32_SizeIN = 4096;
        if (typeof(T) != typeof(Byte[]))
            s32_SizeIN = Marshal.SizeOf(typeof(T));

        Byte[] u8_Buffer;
        if (e_Dir == eDirection.Out) u8_Buffer = Utils.StructureToBytesFix(k_Struct);
        else                         u8_Buffer = new Byte[s32_SizeIN];

        int s32_CmdError = mi_WinUSB.CtrlTansfer(eSetupRecip.Interface, e_Type, e_Dir, u8_Request, wValue, wIndex, ref u8_Buffer);

        if (mi_WinUSB.Interface.Number != FIRMW_UPDATE_INTERFACE)
        {

            Byte[] u8_Feedback = new Byte[10];
            int s32_FbkError = mi_WinUSB.CtrlTansfer(eSetupRecip.Interface, eSetupType.Vendor, eDirection.In,
                                                     (Byte)eUsbRequest.GetLastError, wValue, wIndex, ref u8_Feedback);

            if (s32_FbkError == 0)
            {
                eFeedback e_Feedback = (eFeedback)u8_Feedback[0];
                if (e_Feedback != eFeedback.Success)
                    throw new Exception("The device has returned error feedback: " + e_Feedback.ToString().Replace('_', ' '));
            }
        }

        if (s32_CmdError != 0)
        {
            if (s32_CmdError == (int)eApiError.GEN_FAILURE)
                throw new Exception("The device has refused to execute command " + s_Request);
            else
                Utils.ThrowApiError(s32_CmdError, "Error {0} executing Candlelight command: {1}");
        }

        if (e_Dir == eDirection.In)
        {

            if (u8_Request != (Byte)eUsbRequest.ReadFlash)
            {
                if (u8_Buffer.Length < s32_SizeIN)
                    throw new Exception(String.Format("Error executing command {0}. The device has returned {1} instead of {2} bytes",
                                        s_Request, u8_Buffer.Length, s32_SizeIN));
            }

            return Utils.BytesToStructureFix<T>(u8_Buffer);
        }

        return default(T);
    }

    // Sends packet blob.
    public void SendPacketBlob(CanPacket[] i_Packets, out Int64 s64_WinTimestamp)
    {
        if (!mb_InitDone || !mb_Started)
            throw new Exception("The device must be open and started.");

        if ((mk_Info.mk_Capability.me_Feature & eDeviceFlags.SendUsbBlobs) == 0)
            throw new Exception("Blobs are not supported by the firmware");

        if (i_Packets.Length > CAN_QUEUE_SIZE)
            throw new Exception("Too many Tx packets.");

        List<Byte> i_Transmit = new List<Byte>(MAX_BLOB_SIZE);

        cBlob i_Blob = new cBlob();
        i_Blob.mu8_FrameCount = (Byte)i_Packets.Length;
        i_Blob.me_MesgType    = eMessageType.TxBlob;
        i_Transmit.AddRange(Utils.StructureToBytesFix(i_Blob));

        foreach (CanPacket i_TxPack in i_Packets)
        {
            i_Transmit.AddRange(TxPacketToTxBytes(i_TxPack));
        }

        if (i_Transmit.Count > MAX_BLOB_SIZE)
            throw new Exception("Blob data exceeds MAX_BLOB_SIZE");

        s64_WinTimestamp = Utils.GetWinTimestamp();

        mi_PipeOut.Send(i_Transmit.ToArray());
    }

    // Sends packet.
    public void SendPacket(CanPacket i_Packet, out Int64 s64_WinTimestamp)
    {
        if (!mb_InitDone || !mb_Started)
            throw new Exception("The device must be open and started.");

        Byte[] u8_Transmit = TxPacketToTxBytes(i_Packet);

        s64_WinTimestamp = Utils.GetWinTimestamp();

        mi_PipeOut.Send(u8_Transmit);
    }

    // Encodes a CAN packet.
    private Byte[] TxPacketToTxBytes(CanPacket i_Packet)
    {

        const Byte PAD_BYTE = 0;

        if (mi_PipeIn.PipeErrors > 30 || mi_PipeOut.PipeErrors > 30)
            throw new IOException("Too many errors. The CANable has a problem or has been disconnected.");

        int s32_MaxData = mb_BaudFDSet ? 64 : 8;
        if (i_Packet.mi_Data.Count > s32_MaxData)
            throw new Exception("The CAN data must not be longer than " + s32_MaxData + " bytes");

        if (mb_BaudFDSet && i_Packet.mb_RTR)
            throw new Exception("A remote frame cannot be sent in CAN FD mode.");

        if (!mb_BaudFDSet && (i_Packet.mb_FDF || i_Packet.mb_BRS))
            throw new Exception("CAN FD frames can only be sent when a data baudrate has been set.");

        if (mi_TxOverflow.IsRunning && mi_TxOverflow.ElapsedMilliseconds < 4000)
        {
            mi_TxOverflow.Stop();
            throw new Exception("Sending is not possible because the Tx buffer is full.");
        }

        eCanIdFlags e_MaxID = i_Packet.mb_29bit ? eCanIdFlags.MASK_29 : eCanIdFlags.MASK_11;
        if (i_Packet.ms32_ID > (int)e_MaxID)
            throw new Exception("The CAN ID is invalid.");

        if (i_Packet.mb_RTR && i_Packet.mi_Data.Count > 1)
            throw new Exception("Remote frames contain no data or only one byte that defines the DLC value.");

        int s32_PadLen = i_Packet.mi_Data.Count;
             if (s32_PadLen > 48) s32_PadLen = 64;
        else if (s32_PadLen > 32) s32_PadLen = 48;
        else if (s32_PadLen > 24) s32_PadLen = 32;
        else if (s32_PadLen > 20) s32_PadLen = 24;
        else if (s32_PadLen > 16) s32_PadLen = 20;
        else if (s32_PadLen > 12) s32_PadLen = 16;
        else if (s32_PadLen >  8) s32_PadLen = 12;

        while (i_Packet.mi_Data.Count < s32_PadLen)
        {
            i_Packet.mi_Data.Add(PAD_BYTE);
        }

        i_Packet = i_Packet.Clone();

        cTxFrameElmue i_TxFrame = new cTxFrameElmue(i_Packet);

        if (mb_EnableTxEcho)
        {
            mu8_EchoMarker ++;
            if (mu8_EchoMarker == 0)
                mu8_EchoMarker = 1;

            i_TxFrame.mu8_Marker = mu8_EchoMarker;
        }

        mi_TxEcho[i_TxFrame.mu8_Marker] = i_Packet;

        return Utils.StructureToBytesVar(i_TxFrame, i_TxFrame.mu8_Size);
    }

    // Receives data.
    public cHeader ReceiveData(int s32_Timeout, out Int64 s64_RxTimestamp, out bool b_Blob)
    {
        b_Blob = false;

        s64_RxTimestamp = Utils.GetWinTimestamp();

        if (!mb_InitDone || !mb_Started)
            throw new Exception("The device must be open and started.");

        if (mi_PipeIn.PipeErrors > 30 || mi_PipeOut.PipeErrors > 30)
            throw new IOException("Too many errors. The CANable has a problem or has been disconnected.");

        if (ms32_BlobFrames <= 0)
        {
            ms32_BlobFrames = 0;
            ms32_BlobOffset = 0;

            mi_UsbInPacket = mi_PipeIn.ReadPipeIn(s32_Timeout);
            if (mi_UsbInPacket == null)
                return null;

            cBlob i_Blob = Utils.BytesToStructureVar<cBlob>(mi_UsbInPacket.mu8_Buffer, 0, Marshal.SizeOf(typeof(cBlob)));
            if (i_Blob.me_MesgType == eMessageType.RxBlob)
            {
                ms32_BlobFrames = i_Blob.mu8_FrameCount;
                ms32_BlobOffset = Marshal.SizeOf(typeof(cBlob));
            }
        }

        cHeader i_Header = Utils.BytesToStructureVar<cHeader>(mi_UsbInPacket.mu8_Buffer, ms32_BlobOffset, Marshal.SizeOf(typeof(cHeader)));

        if (ms32_BlobOffset + i_Header.mu8_Size > mi_UsbInPacket.ms32_BytesRead)
        {
            ms32_BlobFrames = 0;
            throw new Exception("Corrupt USB IN data received");
        }

        Byte[] u8_Frame = Utils.ExtractByteArr(mi_UsbInPacket.mu8_Buffer, ms32_BlobOffset, i_Header.mu8_Size);

        cHeader i_Struct;
        switch (i_Header.me_MesgType)
        {
            case eMessageType.TxEcho:  i_Struct = Utils.BytesToStructureVar<cTxEchoElmue> (u8_Frame, 0); break;
            case eMessageType.RxFrame: i_Struct = Utils.BytesToStructureVar<cRxFrameElmue>(u8_Frame, 0); break;
            case eMessageType.Error:   i_Struct = Utils.BytesToStructureVar<cErrorElmue>  (u8_Frame, 0); break;
            case eMessageType.String:  i_Struct = Utils.BytesToStructureVar<cStringElmue> (u8_Frame, 0); break;
            case eMessageType.Busload: i_Struct = Utils.BytesToStructureVar<cBusloadElmue>(u8_Frame, 0); break;
            default:
                throw new Exception("Received invalid USB message device (MessageType = " + u8_Frame[1] + ")");
        }

        if (u8_Frame.Length < i_Struct.GetMinSize(mb_McuTimestamp))
            throw new Exception("Received incomplete USB data from device");

        s64_RxTimestamp = mi_UsbInPacket.ms64_WinTimestamp;
        b_Blob          = ms32_BlobFrames > 0;

        ms32_BlobFrames --;
        ms32_BlobOffset += i_Header.mu8_Size;
        return i_Struct;
    }

    // Decodes a CAN packet.
    public CanPacket RxFrameToCanPacket(cRxFrameElmue i_Frame)
    {
        if (i_Frame == null)
            return null;

        CanPacket i_Packet = new CanPacket();
        i_Packet.ms32_ID   = (int)(i_Frame.mu32_CanID & (UInt32)eCanIdFlags.MASK_29);
        i_Packet.mb_29bit  = (i_Frame.mu32_CanID & (uint)eCanIdFlags.Extended) != 0;
        i_Packet.mb_RTR    = (i_Frame.mu32_CanID & (uint)eCanIdFlags.RTR) != 0;
        i_Packet.mb_FDF    = (i_Frame.me_Flags   & eFrameFlags.FDF) != 0;
        i_Packet.mb_BRS    = i_Packet.mb_FDF && (i_Frame.me_Flags & eFrameFlags.BRS) != 0;
        i_Packet.mb_ESI    = i_Packet.mb_FDF && (i_Frame.me_Flags & eFrameFlags.ESI) != 0;

        int s32_Offset  = mb_McuTimestamp ? 4 : 0;
        int s32_DataLen = i_Frame.mu8_Size - i_Frame.GetMinSize(mb_McuTimestamp);
        Byte[] u8_Data  = Utils.ExtractByteArr(i_Frame.mu8_TimeStampAndData, s32_Offset, s32_DataLen);

        i_Packet.mi_Data.AddRange(u8_Data);
        return i_Packet;
    }

    // Gets tx echo packet.
    public CanPacket GetTxEchoPacket(cTxEchoElmue i_Echo)
    {
        if (!mb_InitDone || !mb_Started)
            throw new Exception("The device must be open and started.");

        return mi_TxEcho[i_Echo.mu8_Marker];
    }

    // Formats timestamp.
    public String FormatTimestamp(cHeader i_Header, Int64 s64_WinTimestamp)
    {

        if (!mb_Started)
            throw new Exception("The device must be open and started.");

        Int64 s64_Stamp = -1;
        if (mb_McuTimestamp)
        {
            if (i_Header != null)
            {
                switch (i_Header.me_MesgType)
                {

                    case eMessageType.TxEcho:  s64_Stamp = ((cTxEchoElmue) i_Header).mu32_Timestamp; break;
                    case eMessageType.RxFrame: s64_Stamp = ((cRxFrameElmue)i_Header).Timestamp;      break;
                    case eMessageType.Error:   s64_Stamp = ((cErrorElmue)  i_Header).mu32_Timestamp; break;
                }
            }

            if (s64_Stamp >= 0)
            {

                if (s64_Stamp         <  0x10000000 &&
                    ms64_LastMcuStamp >  0xF0000000)
                    ms64_McuRollOver += 0x100000000;

                ms64_LastMcuStamp = s64_Stamp;

                s64_Stamp += ms64_McuRollOver;
            }
        }
        else
        {
            s64_Stamp = s64_WinTimestamp;
        }

        if (s64_Stamp < 0)
            return "No Timestamp    ";

        int s32_Micro = (int)(s64_Stamp % 1000);
        s64_Stamp    /= 1000;
        int s32_Milli = (int)(s64_Stamp % 1000);
        s64_Stamp    /= 1000;
        int s32_Sec   = (int)(s64_Stamp % 60);
        s64_Stamp    /= 60;
        int s32_Min   = (int)(s64_Stamp % 60);
        s64_Stamp    /= 60;
        int s32_Hour  = (int)(s64_Stamp % 24);

        return String.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}.{4:D3}", s32_Hour, s32_Min, s32_Sec, s32_Milli, s32_Micro);
    }

    // Formats can errors.
    public String FormatCanErrors(cErrorElmue i_Error, out eBusStatus e_BusStatus, out eErrorLevel e_Level)
    {
        eErrFlagsCanID e_ID    = (eErrFlagsCanID)i_Error.me_ErrID;
        eErrFlagsByte1 e_Byte1 = (eErrFlagsByte1)i_Error.mu8_ErrData[1];
        eErrFlagsByte2 e_Byte2 = (eErrFlagsByte2)i_Error.mu8_ErrData[2];
        eErrorAppFlags e_App   = (eErrorAppFlags)i_Error.mu8_ErrData[5];

        if ((e_App & eErrorAppFlags.CAN_Tx_overflow) > 0)
            mi_TxOverflow.Restart();
        else
            mi_TxOverflow.Stop();

        e_BusStatus = eBusStatus.Active;
        e_Level     = eErrorLevel.Low;

        String s_Mesg = "";
        if ((e_ID & eErrFlagsCanID.Bus_Off) > 0)
        {
            e_BusStatus = eBusStatus.Off;
            e_Level     = eErrorLevel.High;
            s_Mesg     += "Bus Off, ";
        }
        else if ((e_Byte1 & (eErrFlagsByte1.Rx_Bus_Passive | eErrFlagsByte1.Tx_Bus_Passive)) > 0)
        {
            e_BusStatus = eBusStatus.Passive;
            e_Level     = eErrorLevel.High;
            s_Mesg     += "Bus Passive, ";
        }
        else if ((e_Byte1 & (eErrFlagsByte1.Rx_Warning_Level | eErrFlagsByte1.Tx_Warning_Level)) > 0)
        {
            e_BusStatus = eBusStatus.Warning;
            e_Level     = eErrorLevel.Medium;
            s_Mesg     += "Bus Warning, ";
        }
        else
        {
            if ((e_Byte1 & eErrFlagsByte1.Bus_is_back_active) > 0) s_Mesg += "Back to Active, ";
            else                                                   s_Mesg += "Bus Active, ";
        }

        if (e_App > 0) e_Level = eErrorLevel.High;
        if ((e_App & eErrorAppFlags.Rx_Failed)       > 0) s_Mesg += "Rx Failed, ";
        if ((e_App & eErrorAppFlags.Tx_Failed)       > 0) s_Mesg += "Tx Failed, ";
        if ((e_App & eErrorAppFlags.Tx_Timeout)      > 0) s_Mesg += "Tx Timeout, ";
        if ((e_App & eErrorAppFlags.CAN_Tx_overflow) > 0) s_Mesg += "CAN Tx Overflow, ";
        if ((e_App & eErrorAppFlags.USB_IN_overflow) > 0) s_Mesg += "USB IN Overflow, ";

        if ((e_ID    & eErrFlagsCanID.No_ACK_received)     > 0) s_Mesg += "No ACK received, ";
        if ((e_ID    & eErrFlagsCanID.CRC_Error)           > 0) s_Mesg += "CRC Error, ";
        if ((e_Byte2 & eErrFlagsByte2.Bit_stuffing_error)  > 0) s_Mesg += "Bit Stuffing Error, ";
        if ((e_Byte2 & eErrFlagsByte2.Frame_format_error)  > 0) s_Mesg += "Frame Format Error, ";
        if ((e_Byte2 & eErrFlagsByte2.Dominant_bit_error)  > 0) s_Mesg += "Dominant Bit Error, ";
        if ((e_Byte2 & eErrFlagsByte2.Recessive_bit_error) > 0) s_Mesg += "Recessive Bit Error, ";

        if (i_Error.mu8_ErrData[6] > 0)
            s_Mesg += String.Format("Tx Errors: {0}, ", i_Error.mu8_ErrData[6]);

        if (i_Error.mu8_ErrData[7] > 0)
            s_Mesg += String.Format("Rx Errors: {0}, ", i_Error.mu8_ErrData[7]);

        return s_Mesg.TrimEnd(',', ' ');
    }

    // Enters firmware update mode.
    public void EnterDfuMode()
    {
        if (!mb_InitDone || mi_WinUSB.Interface.Number != FIRMW_UPDATE_INTERFACE)
            throw new Exception("The device must be opened for the DFU interface.");

        CtrlTransfer((Byte)eDfuRequest.Detach, eDirection.Out, 0, new Byte[0]);

        kDfuStatus k_Status = new kDfuStatus();
        try
        {
            k_Status = CtrlTransfer<kDfuStatus>((Byte)eDfuRequest.GetStatus, eDirection.In, 0);
        }
        catch
        {

        }

        Dispose();

        if (k_Status.me_State == eDfuState.AppDetach)
            throw new Exception("Please reconnect the USB cable");

        if (k_Status.me_State == eDfuState.Error && k_Status.mu8_StringIdx > 0 && k_Status.mu8_StringIdx < 255)
            throw new Exception(((eFeedback)k_Status.mu8_StringIdx).ToString().Replace('_', ' '));
    }
}
}
