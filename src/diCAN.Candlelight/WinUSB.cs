

#if DEBUG

#endif

using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

using eApiError           = CANable.Utils.eApiError;
using eWaitObject         = CANable.Utils.eWaitObject;
using eFileAccess         = CANable.Utils.eFileAccess;
using eFileShare          = CANable.Utils.eFileShare;
using eFileCreate         = CANable.Utils.eFileCreate;
using eFileFlags          = CANable.Utils.eFileFlags;

namespace CANable
{

// Manages win usb.
public class WinUSB : IDisposable
{
    #region enums

    // Defines e descriptor values.
    public enum eDescriptor : byte
    {
        Device               = 0x01,
        Configuration        = 0x02,
        String               = 0x03,
        Interface            = 0x04,
        Endpoint             = 0x05,
        DeviceQualifier      = 0x06,
        OtherSpeed           = 0x07,
        InterfacePower       = 0x08,
        OnTheGo              = 0x09,
        Debug                = 0x0A,
        InterfaceAssociation = 0x0B,
        BOS                  = 0x0F,
        DeviceCapability     = 0x10,
        HID                  = 0x21,
        Report               = 0x22,
        Physical             = 0x23,
        CS_Interface         = 0x24,
        CS_Endpoint          = 0x25,
        HUB                  = 0x29,
        SuperHUB             = 0x2A,
        EndpointCompanion    = 0x30,
    }

    // Defines e device class values.
    public enum eDeviceClass : byte
    {
        Undefined        = 0x00,
        Audio            = 0x01,
        CdcControl       = 0x02,
        HID              = 0x03,
        Physical         = 0x05,
        StillImaging     = 0x06,
        Printer          = 0x07,
        MassStorage      = 0x08,
        HUB              = 0x09,
        CdcData          = 0x0A,
        Smartcard        = 0x0B,
        Security         = 0x0D,
        Video            = 0x0E,
        HealthCare       = 0x0F,
        DiagnosticDevice = 0xDC,
        Bluetooth        = 0xE0,
        Miscellaneous    = 0xEF,
        FwUpgrade        = 0xFE,
        Vendor           = 0xFF,
    }

    // Defines e pipe type values.
    public enum ePipeType : int
    {
        Control,
        Isochronous,
        Bulk,
        Interrupt,
    }

    // Defines e pipe policy values.
    public enum ePipePolicy : int
    {
        ShortPacketTerminate = 1,
        AutoClearStall,
        PipeTransferTimeout,
        IgnoreShortPackets,
        AllowPartialReads,
        AutoFlush,
        RawIO,
        MaximumTransferSize,
        ResetPipeOnResume,
    }

    // Manages e setup.
    enum eSetupRequest : byte
    {
        GetStatus        =  0,
        ClearFeature     =  1,
        SetFeature       =  3,
        GetMsDescriptor  =  4,
        SetAddress       =  5,
        GetDescriptor    =  6,
        SetDescriptor    =  7,
        GetConfiguration =  8,
        SetConfiguration =  9,
        GetInterface     = 10,
        SetInterface     = 11,
        SynchFrame       = 12,
    };

    // Defines e setup recip values.
    public enum eSetupRecip : byte
    {
        Device    = 0x00,
        Interface = 0x01,
        Endpoint  = 0x02,
        Other     = 0x03,

    };

    // Defines e setup type values.
    public enum eSetupType : byte
    {
        Standard = 0x00,
        Class    = 0x20,
        Vendor   = 0x40,
    };

    // Defines e direction values.
    public enum eDirection : byte
    {
        Out = 0x00,
        In  = 0x80,
    };

    // Defines e dfu request values.
    public enum eDfuRequest : byte
    {
        Detach      = 0,
        Download    = 1,
        Upload      = 2,
        GetStatus   = 3,
        ClearStatus = 4,
        GetState    = 5,
        Abort       = 6,
    }

    // Defines e dfu attribs values.
    [FlagsAttribute]
    public enum eDfuAttribs : byte
    {
        CanDownload           = 0x01,
        CanUpload             = 0x02,
        ManifestationTolerant = 0x04,
        WillDetach            = 0x08,
    }

    #endregion

    #region structs

    // Manages k descriptor head.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class kDescriptorHead
    {
        public byte         bLength;
        public eDescriptor  eDescrType;
    }

    // Manages k device descriptor.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class kDeviceDescriptor : kDescriptorHead
    {
        public ushort       bcdUSB;
        public eDeviceClass bDeviceClass;
        public byte         bDeviceSubClass;
        public byte         bDeviceProtocol;
        public byte         bMaxPacketSize0;
        public ushort       idVendor;
        public ushort       idProduct;
        public ushort       bcdDevice;
        public byte         iManufacturer;
        public byte         iProduct;
        public byte         iSerialNumber;
        public byte         bNumConfigurations;
    }

    // Manages k config descriptor.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    protected class kConfigDescriptor : kDescriptorHead
    {
        public ushort      wTotalLength;
        public byte        bNumInterfaces;
        public byte        bConfigurationValue;
        public byte        iConfiguration;
        public byte        bmAttributes;
        public byte        MaxPower;
    }

    // Manages k interface.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class kInterfaceDescriptor : kDescriptorHead
    {
        public byte         bInterfaceNumber;
        public byte         bAlternateSetting;
        public byte         bNumEndpoints;
        public eDeviceClass bInterfaceClass;
        public byte         bInterfaceSubClass;
        public byte         bInterfaceProtocol;
        public byte         iInterface;
    }

    // Manages k endpoint descriptor.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class kEndpointDescriptor : kDescriptorHead
    {
        public Byte        u8_EndpointAddress;
        public Byte        u8_Attributes;
        public UInt16      u16_MaxPacketSize;
        public Byte        u8_Interval;
    }

    // Manages k hid descriptor.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class kHidDescriptor : kDescriptorHead
    {
        public Byte        u8_VersionLo;
        public Byte        u8_VersionHi;
        public Byte        u8_CountryCode;
        public Byte        u8_NumDescriptors;
        public Byte        u8_DescrType;
        public UInt16      u16_DescriptorLength;
    }

    // Manages k dfu descriptor.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class kDfuDescriptor : kDescriptorHead
    {
        public eDfuAttribs e_Attributes;
        public UInt16      u16_DetachTimeout;
        public UInt16      u16_TransferSize;
        public Byte        u8_DfuVersionLo;
        public Byte        u8_DfuVersionHi;
    }

    // Stores k setup data.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct kSetup
    {
        public byte   mu8_RequType;
        public byte   mu8_Request;
        public ushort mu16_Value;
        public ushort mu16_Index;
        public ushort mu16_Length;

        // Formats the value as text.
        public override string ToString()
        {
            Byte[] u8_Data = Utils.StructureToBytesFix(this);
            return Utils.BytesToHex(u8_Data);
        }
    }

    // Manages k pipe.
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct kPipeInformation
    {
        public ePipeType me_PipeType;
        public Byte      mu8_PipeId;
        public ushort    mu16_MaxPacketSize;
        public Byte      mu8_Interval;

        public eDirection Direction
        {
            get { return (eDirection)(mu8_PipeId & 0x80); }
        }
    }

    #endregion

    #region DLL Imports WinUSB

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_Initialize(SafeFileHandle DeviceHandle, out IntPtr InterfaceHandle);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_Free(IntPtr InterfaceHandle);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_GetAssociatedInterface(IntPtr h_WinUSB, Byte u8_InterfaceIndex, out IntPtr h_AssociatedInterfaceHandle);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_GetDescriptor(IntPtr h_WinUSB, eDescriptor DescriptorType, Byte Index, UInt16 LanguageID, [MarshalAs(UnmanagedType.LPArray)] Byte[] u8_Data, int BufferLength, out int LengthTransfered);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_QueryInterfaceSettings(IntPtr h_Interface, Byte u8_AlternateInterfaceNumber, [MarshalAs(UnmanagedType.LPArray)] Byte[] u8_Data);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_QueryPipe(IntPtr h_Interface, Byte u8_AlternateInterfaceNumber, Byte u8_PipeIndex, out kPipeInformation k_PipeInformation);

    // Transfers USB control data.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_ControlTransfer(IntPtr h_Interface, kSetup k_Setup, Byte[] Buffer, int s32_BufSize, out int s32_Transferred, IntPtr pk_Overlapped);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_SetPipePolicy(IntPtr h_Interface, Byte u8_PipeID, ePipePolicy PolicyType, int ValueLength, ref Byte u8_BoolValue);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_SetPipePolicy(IntPtr h_Interface, Byte u8_PipeID, ePipePolicy PolicyType, int ValueLength, ref int s32_IntValue);

    // Aborts a USB pipe transfer.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_AbortPipe(IntPtr h_Interface, Byte u8_PipeID);

    // Writes USB pipe data.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_WritePipe(IntPtr h_Interface, Byte u8_PipeID, Byte[] u8_Data, int s32_DataLength, out int s32_Transferred, IntPtr pk_Overlapped);

    // Reads USB pipe data.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_ReadPipe(IntPtr h_Interface, Byte u8_PipeID, IntPtr h_RxBuffer, int s32_BufSize, IntPtr p_Transferred, ref NativeOverlapped k_Overlapped);

    // Accesses the USB driver.
    [DllImport("winusb.dll", SetLastError = true)]
    static extern bool WinUsb_GetOverlappedResult(IntPtr h_Interface, ref NativeOverlapped k_Overlapped, out int s32_Transferred, bool b_Wait);

    #endregion

    #region cInterface

    // Manages c interface.
    public class cInterface : IDisposable
    {
        private WinUSB               mi_WinUSB;
        private IntPtr               mh_Handle;
        private kInterfaceDescriptor mk_Descriptor;
        private String               ms_InterfaceStr;
        private List<cPipeIn>        mi_InPipes  = new List<cPipeIn>();
        private List<cPipeOut>       mi_OutPipes = new List<cPipeOut>();

        // Initializes this instance.
        public cInterface(WinUSB i_WinUSB, IntPtr h_Handle)
        {
            mi_WinUSB = i_WinUSB;
            mh_Handle = h_Handle;

            Byte[] u8_Descr = new Byte[Marshal.SizeOf(typeof(kInterfaceDescriptor))];
            if (!WinUsb_QueryInterfaceSettings(h_Handle, 0, u8_Descr))
                i_WinUSB.ThrowLastError("Error reading interface descriptor");

            mk_Descriptor = Utils.BytesToStructureFix<kInterfaceDescriptor>(u8_Descr);

            for (Byte u8_EP=0; u8_EP < mk_Descriptor.bNumEndpoints; u8_EP++)
            {
                kPipeInformation k_Pipe;
                if (!WinUsb_QueryPipe(h_Handle, 0, u8_EP, out k_Pipe))
                    i_WinUSB.ThrowLastError("Error getting pipe information");

                if (k_Pipe.Direction == eDirection.In)  mi_InPipes .Add(new cPipeIn (i_WinUSB, h_Handle, this, k_Pipe));
                if (k_Pipe.Direction == eDirection.Out) mi_OutPipes.Add(new cPipeOut(i_WinUSB, h_Handle, this, k_Pipe));
            }

            ms_InterfaceStr = mi_WinUSB.ReadStringDescriptor(mk_Descriptor.iInterface, LANGUAGE_ENGLISH_USA);
        }

        // Formats the value as text.
        public override string ToString()
        {
            return ms_InterfaceStr;
        }

        // Releases held resources.
        public void Dispose()
        {
            foreach (cPipeIn i_PipeIn in mi_InPipes)
            {
                i_PipeIn.Dispose();
            }

            WinUsb_Free(mh_Handle);
            mh_Handle = IntPtr.Zero;
        }

        public Byte Number
        {
            get { return mk_Descriptor.bInterfaceNumber; }
        }
        public String String
        {
            get { return ms_InterfaceStr; }
        }
        public Byte EndpointCount
        {
            get { return mk_Descriptor.bNumEndpoints; }
        }

        // Gets pipe out.
        public cPipeOut GetPipeOut(ePipeType e_Type)
        {
            foreach (cPipeOut i_Pipe in mi_OutPipes)
            {
                if (i_Pipe.PipeType == e_Type)
                    return i_Pipe;
            }
            return null;
        }

        // Gets pipe in.
        public cPipeIn GetPipeIn(ePipeType e_Type)
        {
            foreach (cPipeIn i_Pipe in mi_InPipes)
            {
                if (i_Pipe.PipeType == e_Type)
                    return i_Pipe;
            }
            return null;
        }
    }

    #endregion

    #region cPipe

    // Manages c pipe.
    public class cPipe
    {
        protected WinUSB           mi_WinUSB;
        protected IntPtr           mh_Handle;
        protected cInterface       mi_Interface;
        protected kPipeInformation mk_PipeInfo;
        protected int              ms32_PipeErrors;

        public ePipeType PipeType
        {
            get { return mk_PipeInfo.me_PipeType; }
        }

        // Formats the value as text.
        public override string ToString()
        {
            return String.Format("EP {0:X2}", Endpoint);
        }

        public Byte Endpoint
        {
            get { return mk_PipeInfo.mu8_PipeId; }
        }
        public UInt16 MaxPacketSize
        {
            get { return mk_PipeInfo.mu16_MaxPacketSize; }
        }
        public int PipeErrors
        {
            get { return ms32_PipeErrors; }
        }

        // Initializes this instance.
        public cPipe(WinUSB i_WinUSB, IntPtr h_Handle, cInterface i_Interface, kPipeInformation k_PipeInfo)
        {
            mi_WinUSB    = i_WinUSB;
            mh_Handle    = h_Handle;
            mi_Interface = i_Interface;
            mk_PipeInfo  = k_PipeInfo;
        }

        // Sets policy.
        public void SetPolicy(ePipePolicy e_Policy, bool b_Value)
        {
            Debug.Assert(e_Policy != ePipePolicy.PipeTransferTimeout, "Invalid parameter");

            Byte u8_Bool = b_Value ? (Byte)1 : (Byte)0;
            if (!WinUsb_SetPipePolicy(mh_Handle, Endpoint, e_Policy, 1, ref u8_Bool))
                mi_WinUSB.ThrowLastError("Error setting pipe policy");
        }

        // Sets transfer timeout.
        public void SetTransferTimeout(int s32_Timeout)
        {
            if (!WinUsb_SetPipePolicy(mh_Handle, Endpoint, ePipePolicy.PipeTransferTimeout, 4, ref s32_Timeout))
                mi_WinUSB.ThrowLastError("Error setting pipe timeout");
        }

        // Aborts the active operation.
        public bool Abort()
        {
            return WinUsb_AbortPipe(mh_Handle, Endpoint);
        }
    }

    #endregion

    #region cPipeOut

    // Manages c pipe out.
    public class cPipeOut : cPipe
    {

        // Initializes this instance.
        public cPipeOut(WinUSB i_WinUSB, IntPtr h_Handle, cInterface i_Interface, kPipeInformation k_Pipe)
            : base(i_WinUSB, h_Handle, i_Interface, k_Pipe)
        {
        }

        // Sends the requested data.
        public void Send(Byte[] u8_TxData)
        {
            #if TRACE_PIPES
                Debug.Print(String.Format("Tx: [{0}] {1}", u8_TxData.Length, Utils.BytesToHex(u8_TxData)));
            #endif

            int s32_Transferred = 0;
            if (!WinUsb_WritePipe(mh_Handle, Endpoint, u8_TxData, u8_TxData.Length, out s32_Transferred, IntPtr.Zero))
            {
                ms32_PipeErrors ++;
                mi_WinUSB.ThrowLastError("Error writing to pipe");
            }

            if (s32_Transferred != u8_TxData.Length)
                throw new Exception("Error sending data to pipe.");

            ms32_PipeErrors = 0;
        }
    }

    #endregion

    #region cPipeIn

    // Manages c pipe in.
    public class cPipeIn : cPipe, IDisposable
    {

        const int FIFO_SLOTS = 1024;

        cUsbInPacket[] mi_RxFifo = new cUsbInPacket[FIFO_SLOTS];
        int            ms32_FifoReadIdx;
        int            ms32_FifoCount;
        bool           mb_AbortThread;
        bool           mb_FifoOverflow;

        readonly cUsbInPacket mi_DiscardPacket = new cUsbInPacket();
        long                  ms64_Discarded;
        IntPtr         mh_ThreadEvent;
        IntPtr         mh_ReceiveEvent;
        int            ms32_BufSize;

        // Initializes this instance.
        public cPipeIn(WinUSB i_WinUSB, IntPtr h_Handle, cInterface i_Interface, kPipeInformation k_Pipe)
            : base(i_WinUSB, h_Handle, i_Interface, k_Pipe)
        {
            mh_ReceiveEvent = Utils.CreateEventW(IntPtr.Zero, false, false, null);

            for (int i=0; i<mi_RxFifo.Length; i++)
            {
                mi_RxFifo[i] = new cUsbInPacket();
            }
        }

        public long DiscardedPackets
        {
            get { lock (mi_RxFifo) { return ms64_Discarded; } }
        }

        // Releases held resources.
        public void Dispose()
        {

            for (int i=0; mh_ThreadEvent != IntPtr.Zero && i < 100; i++)
            {
                mb_AbortThread = true;
                Utils.SetEvent(mh_ThreadEvent);
                Thread.Sleep(10);
            }
            Utils.CloseHandle(mh_ReceiveEvent);
        }

        // Starts thread.
        public void StartThread(int s32_BufSize)
        {
            ms32_BufSize = s32_BufSize;

            Thread i_Thread = new Thread(new ThreadStart(ReadPipeThread));
            i_Thread.IsBackground = true;
            i_Thread.Priority     = ThreadPriority.Highest;
            i_Thread.Name         = "WinUSB Pipe Thread";
            i_Thread.Start();
        }

        // Reads pipe thread.
        private void ReadPipeThread()
        {
            mb_AbortThread = false;
            mh_ThreadEvent = Utils.CreateEventW(IntPtr.Zero, false, false, null);

            NativeOverlapped k_Overlapped = new NativeOverlapped();
            k_Overlapped.EventHandle = mh_ThreadEvent;

            Byte[]   u8_RxBuffer = new Byte[ms32_BufSize];
            GCHandle i_GcHandle  = GCHandle.Alloc(u8_RxBuffer, GCHandleType.Pinned);
            IntPtr   h_RxBuffer  = i_GcHandle.AddrOfPinnedObject();

            while (!mb_AbortThread)
            {

                bool b_Discard;
                lock (mi_RxFifo)
                {
                    b_Discard = ms32_FifoCount >= mi_RxFifo.Length;
                    if (b_Discard)
                        mb_FifoOverflow = true;
                }

                int s32_FifoWriteIdx;
                cUsbInPacket i_FifoWrite;
                lock (mi_RxFifo)
                {
                    if (b_Discard)
                    {
                        i_FifoWrite = mi_DiscardPacket;
                    }
                    else
                    {
                        s32_FifoWriteIdx = (ms32_FifoReadIdx + ms32_FifoCount) % mi_RxFifo.Length;
                        i_FifoWrite      = mi_RxFifo[s32_FifoWriteIdx];
                    }
                }

                int s32_Read  = 0;
                int s32_Error = 0;
                if (!WinUsb_ReadPipe(mh_Handle, Endpoint, h_RxBuffer, u8_RxBuffer.Length, IntPtr.Zero, ref k_Overlapped))
                {
                    s32_Error = Marshal.GetLastWin32Error();
                    if (s32_Error == (int)eApiError.ERROR_IO_PENDING)
                    {
                        s32_Error = 0;

                        eWaitObject e_Result = Utils.WaitForSingleObject(mh_ThreadEvent, Timeout.Infinite);
                        if (mb_AbortThread)
                            break;

                        switch (e_Result)
                        {
                            case eWaitObject.Timeout:
                                s32_Error = (int)eApiError.ERROR_TIMEOUT;
                                break;

                            case eWaitObject.Object0:
                                if (WinUsb_GetOverlappedResult(mh_Handle, ref k_Overlapped, out s32_Read, false))
                                    ms32_PipeErrors = 0;
                                else
                                    s32_Error = Marshal.GetLastWin32Error();
                                break;

                            default:
                                s32_Error = Marshal.GetLastWin32Error();
                                break;
                        }
                    }
                }

                i_FifoWrite.ms64_WinTimestamp = Utils.GetWinTimestamp();
                i_FifoWrite.ms32_BytesRead    = s32_Read;
                i_FifoWrite.ms32_Error        = s32_Error;
                i_FifoWrite.mu8_Buffer        = null;
                if (s32_Error == 0)
                    i_FifoWrite.mu8_Buffer = Utils.ExtractByteArr(u8_RxBuffer, 0, s32_Read);

                #if TRACE_PIPES
                    Debug.Print(String.Format("Rx: [{0}] {1}", s32_Read, Utils.BytesToHex(i_FifoWrite.mu8_Buffer)));
                #endif

                lock (mi_RxFifo)
                {

                    if (b_Discard)
                    {
                        ms64_Discarded ++;
                    }
                    else
                    {
                        ms32_FifoCount ++;
                        Utils.SetEvent(mh_ReceiveEvent);
                    }
                }

                if (s32_Error > 0)
                {

                    Thread.Sleep(50);
                    ms32_PipeErrors ++;
                }
            }

            i_GcHandle.Free();
            Utils.CloseHandle(mh_ThreadEvent);
            mh_ThreadEvent = IntPtr.Zero;
        }

        // Reads pipe in.
        public cUsbInPacket ReadPipeIn(int s32_Timeout)
        {
            int s32_Available;
            cUsbInPacket i_FifoRead;
            lock (mi_RxFifo)
            {
                i_FifoRead    = mi_RxFifo[ms32_FifoReadIdx];
                s32_Available = ms32_FifoCount;
                if (s32_Available > 0)
                    Utils.ResetEvent(mh_ReceiveEvent);
            }

            if (s32_Available == 0)
            {

                if (mb_FifoOverflow)
                {
                    lock (mi_RxFifo)
                    {
                        mb_FifoOverflow = false;
                    }

                    throw new Exception("Rx FIFO overflow");
                }

                eWaitObject e_Result = Utils.WaitForSingleObject(mh_ReceiveEvent, s32_Timeout);
                if (e_Result == eWaitObject.Timeout)
                    return null;

                lock (mi_RxFifo)
                {
                    s32_Available = ms32_FifoCount;
                }

                if (s32_Available == 0)
                    return null;
            }

            cUsbInPacket i_Copy;
            lock (mi_RxFifo)
            {
                i_Copy = new cUsbInPacket
                {
                    mu8_Buffer        = i_FifoRead.mu8_Buffer,
                    ms32_BytesRead    = i_FifoRead.ms32_BytesRead,
                    ms32_Error        = i_FifoRead.ms32_Error,
                    ms64_WinTimestamp = i_FifoRead.ms64_WinTimestamp,
                };

                ms32_FifoReadIdx = (ms32_FifoReadIdx + 1) % mi_RxFifo.Length;
                ms32_FifoCount --;
            }

            if (i_Copy.ms32_Error != 0)
                Utils.ThrowApiError(i_Copy.ms32_Error, "Error {0} reading WinUSB pipe: {1}");

            return i_Copy;
        }
    }

    #endregion

    #region cUsbInPacket

    // Manages c usb in packet.
    public class cUsbInPacket
    {
        public Byte[]  mu8_Buffer;
        public int     ms32_BytesRead;
        public int     ms32_Error;
        public Int64   ms64_WinTimestamp;
    };

    #endregion

    const UInt16 LANGUAGE_ENGLISH_USA = 0x409;

    SafeFileHandle        mi_FileHandle;
    IntPtr                mh_WinUSB;
    kDeviceDescriptor     mi_DeviceDescr;
    List<kDescriptorHead> mi_AllDescriptors = new List<kDescriptorHead>();
    cInterface            mi_Interface;
    String                ms_Vendor;
    String                ms_Product;
    String                ms_SerialNo;
    bool                  mb_IsOpen;

    public String Vendor
    {
        get { return ms_Vendor; }
    }
    public String Product
    {
        get { return ms_Product; }
    }
    public String SerialNo
    {
        get { return ms_SerialNo; }
    }
    public kDeviceDescriptor DeviceDescriptor
    {
        get { return mi_DeviceDescr; }
    }
    public kDescriptorHead[] AllDescriptors
    {
        get { return mi_AllDescriptors.ToArray(); }
    }
    public cInterface Interface
    {
        get { return mi_Interface; }
    }

    // Formats the value as text.
    public override string ToString()
    {
        return String.Format("{0} - {1} [{2}]", ms_Vendor, ms_Product, ms_SerialNo);
    }

    // Releases held resources.
    public void Dispose()
    {
        if (mi_Interface != null)
        {
            mi_Interface.Dispose();
            mi_Interface = null;
        }

        if (mi_FileHandle != null)
        {
            mi_FileHandle.Dispose();
            mi_FileHandle = null;
        }
    }

    // Opens the requested resource.
    public void Open(String s_NtPath, int s32_ControlTimeout)
    {
        Debug.Assert(!mb_IsOpen, "Never reuse this class. Always create a new instance.");

        mi_FileHandle = Utils.CreateFileW(s_NtPath, eFileAccess.GenericRead | eFileAccess.GenericWrite,
                                            eFileShare.None, IntPtr.Zero, eFileCreate.OpenExisting,
                                            eFileFlags.AttributeNormal | eFileFlags.FlagOverlapped, IntPtr.Zero);

        if (mi_FileHandle.IsInvalid)
        {
            int s32_Error = Marshal.GetLastWin32Error();
            if (s32_Error == (int)eApiError.ACCESS_DENIED)
                throw new Exception("Error opening the WinUSB device. It is probably already open elsewhere.");
            else
                ThrowLastError("Error opening the WinUSB device", s32_Error);
        }

        if (!WinUsb_Initialize(mi_FileHandle, out mh_WinUSB))
            ThrowLastError("Error initializing the WinUSB device");

        if (!WinUsb_SetPipePolicy(mh_WinUSB, 0, ePipePolicy.PipeTransferTimeout, 4, ref s32_ControlTimeout))
            ThrowLastError("Error setting control pipe timeout");

        ReadDescriptors(eDescriptor.Device);
        ReadDescriptors(eDescriptor.Configuration);

        if (mi_DeviceDescr.iManufacturer == 1 && mi_DeviceDescr.iSerialNumber == 3)
            mi_DeviceDescr.iProduct = 2;

        ms_Vendor   = ReadStringDescriptor(mi_DeviceDescr.iManufacturer, LANGUAGE_ENGLISH_USA);
        ms_Product  = ReadStringDescriptor(mi_DeviceDescr.iProduct,      LANGUAGE_ENGLISH_USA);
        ms_SerialNo = ReadStringDescriptor(mi_DeviceDescr.iSerialNumber, LANGUAGE_ENGLISH_USA);

        mi_Interface = new cInterface(this, mh_WinUSB);

        mb_IsOpen = true;
    }

    // Reads descriptors.
    void ReadDescriptors(eDescriptor e_Descriptor)
    {
        Byte[] u8_Data = new Byte[255];
        int s32_Read;
        if (!WinUsb_GetDescriptor(mh_WinUSB, e_Descriptor, 0, 0, u8_Data, u8_Data.Length, out s32_Read))
            ThrowLastError("Error reading the "+e_Descriptor+" descriptor");

        kInterfaceDescriptor i_LastInterface = new kInterfaceDescriptor();

        int s32_Offset = 0;
        while (s32_Offset < s32_Read)
        {

            kDescriptorHead i_Head = Utils.BytesToStructureVar<kDescriptorHead>(u8_Data, s32_Offset, Marshal.SizeOf(typeof(kDescriptorHead)));

            if (s32_Offset + i_Head.bLength > s32_Read)
                throw new Exception("Device has sent incomplete data for the "+e_Descriptor+" descriptor.");

            kDescriptorHead i_Descr = null;
            switch (i_Head.eDescrType)
            {
                case eDescriptor.Device:
                    mi_DeviceDescr = Utils.BytesToStructureVar<kDeviceDescriptor>(u8_Data, s32_Offset, i_Head.bLength);
                    i_Descr = mi_DeviceDescr;
                    break;
                case eDescriptor.Configuration:
                    i_Descr = Utils.BytesToStructureVar<kConfigDescriptor>(u8_Data, s32_Offset, i_Head.bLength);
                    break;
                case eDescriptor.Endpoint:
                    i_Descr = Utils.BytesToStructureVar<kEndpointDescriptor>(u8_Data, s32_Offset, i_Head.bLength);
                    break;
                case eDescriptor.Interface:
                    i_LastInterface = Utils.BytesToStructureVar<kInterfaceDescriptor>(u8_Data, s32_Offset, i_Head.bLength);
                    i_Descr = i_LastInterface;
                    break;
                case eDescriptor.HID:
                    if (i_LastInterface.bInterfaceClass == eDeviceClass.HID)
                        i_Descr = Utils.BytesToStructureVar<kHidDescriptor>(u8_Data, s32_Offset, i_Head.bLength);
                    if (i_LastInterface.bInterfaceClass == eDeviceClass.FwUpgrade)
                        i_Descr = Utils.BytesToStructureVar<kDfuDescriptor>(u8_Data, s32_Offset, i_Head.bLength);
                    break;
                default:
                    Debug.Assert(false, "Descriptor not implemented: " + i_Head.eDescrType);
                    break;
            }

            s32_Offset += i_Head.bLength;

            if (i_Descr != null)
                mi_AllDescriptors.Add(i_Descr);
        }
    }

    // Reads string descriptor.
    String ReadStringDescriptor(byte u8_Index, ushort u16_LanguageID)
    {

        if (u8_Index == 0)
            return "";

        Byte[] u8_Buffer = new Byte[256];
        int s32_Read;
        if (!WinUsb_GetDescriptor(mh_WinUSB, eDescriptor.String, u8_Index, u16_LanguageID, u8_Buffer, u8_Buffer.Length, out s32_Read))
            ThrowLastError("Error reading string descriptor " + u8_Index);

        Byte      u8_Length = u8_Buffer[0];
        eDescriptor e_Descr = (eDescriptor)u8_Buffer[1];

        if (e_Descr != eDescriptor.String || u8_Length < 2 || u8_Length != s32_Read || (s32_Read & 1) > 0)
            throw new Exception("The device returned crippled data for string descriptor " + u8_Index + ".");

        return Encoding.Unicode.GetString(u8_Buffer, 2, s32_Read - 2);
    }

    // Transfers USB control data.
    public int CtrlTansfer(eSetupRecip e_Recip, eSetupType e_Type, eDirection e_Dir, Byte u8_Request,
                            UInt16 u16_Value, UInt16 u16_Index, ref Byte[] u8_Buffer)
    {
        kSetup k_Setup;
        k_Setup.mu8_RequType = (Byte)((int)e_Recip | (int)e_Type | (int)e_Dir);
        k_Setup.mu8_Request  = u8_Request;
        k_Setup.mu16_Value   = u16_Value;
        k_Setup.mu16_Index   = u16_Index;
        k_Setup.mu16_Length  = 0;

        int s32_Transferred;
        if (!WinUsb_ControlTransfer(mh_WinUSB, k_Setup, u8_Buffer, u8_Buffer.Length, out s32_Transferred, IntPtr.Zero))
            return Marshal.GetLastWin32Error();

        if (e_Dir == eDirection.In && s32_Transferred < u8_Buffer.Length)
            u8_Buffer = Utils.ExtractByteArr(u8_Buffer, 0, s32_Transferred);

        return 0;
    }

    // Reports an operation error.
    void ThrowLastError(String s_Mesg, int s32_Error = 0)
    {
        if (s32_Error <= 0)
            s32_Error = Marshal.GetLastWin32Error();

        if (mb_IsOpen && s32_Error == (int)eApiError.GEN_FAILURE)
            throw new Exception(s_Mesg + ".  No response from the WinUSB device");

        Win32Exception i_WinEx = new Win32Exception(s32_Error);
        s_Mesg += ".  Error " + s32_Error + ": " + i_WinEx.Message;

        if (!mb_IsOpen) s_Mesg += "\nThe reason may be that the device has crashed or is defective. Try to reconnect the USB cable.";

        throw new Exception(s_Mesg);
    }
}
}
