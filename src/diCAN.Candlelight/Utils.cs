

using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CANable
{

// Manages utils.
public class Utils
{
    #region enums Kernel32

    public  static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    // Defines e api error values.
    public enum eApiError : uint
    {
        ACCESS_DENIED        =          5,
        GEN_FAILURE          =         31,
        INVALID_PARAMETER    =         87,
        SEM_TIMEOUT          =        121,
        NO_MORE_ITEMS        =        259,
        NO_SUCH_DEVICE       =        433,
        OPERATION_ABORTED    =        995,
        ERROR_IO_INCOMPLETE  =        996,
        ERROR_IO_PENDING     =        997,
        ERROR_TIMEOUT        =       1460,
        WSAEHOSTUNREACH      =      10065,
        ReflectionTypeLoadEx = 0x80131602,
    }

    // Defines e wait object values.
    public enum eWaitObject : int
    {
        Failed    = -1,
        Object0   = 0,
        Object1   = 1,
        Object2   = 2,
        Object3   = 3,
        Abandoned = 0x80,
        Timeout   = 0x102,
    }

    // Defines e file access values.
    [FlagsAttribute]
    public enum eFileAccess : uint
    {
        GenericRead  = 0x80000000,
        GenericWrite = 0x40000000,
    }

    // Defines e file share values.
    [FlagsAttribute]
    public enum eFileShare
    {
        None  = 0,
        Read  = 1,
        Write = 2,
    }

    // Defines e file create values.
    public enum eFileCreate
    {
        OpenExisting = 3,
    }

    // Defines e file flags values.
    [FlagsAttribute]
    public enum eFileFlags
    {
        AttributeNormal = 0x00000080,
        FlagOverlapped  = 0x40000000,
    }

    #endregion

    #region DLL Imports

    // Creates file w.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(String s_FileName, eFileAccess e_DesiredAccess, eFileShare e_ShareMode, IntPtr p_SecurityAttributes, eFileCreate e_CreationDisposition, eFileFlags e_FlagsAndAttributes, IntPtr h_TemplateFile);

    // Cancels io.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIo(IntPtr h_File);

    // Creates event w.
    [DllImport("kernel32.dll", SetLastError = true, CharSet=CharSet.Unicode)]
    public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, String lpName);

    // Closes handle.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hHandle);

    // Sets event.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetEvent(IntPtr hHandle);

    // Resets event.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ResetEvent(IntPtr hHandle);

    // Waits for pending work.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern eWaitObject WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    // Fills a memory region.
    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr memset(IntPtr dest, int c, IntPtr count);

    #endregion

    #region Console Stuff

    // Defines a callback contract.
    public delegate bool ConsoleCtrlDelegate(int ctrlType);

    // Stores input key record data.
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT_KEY_RECORD
    {
        public UInt16 EventType;
        public bool   bKeyDown;
        public UInt16 wRepeatCount;
        public UInt16 wVirtualKeyCode;
        public UInt16 wVirtualScanCode;
        public UInt16 UnicodeChar;
        public UInt32 dwControlKeyState;
    }

    // Gets std handle.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    // Sets console ctrl handler.
    [DllImport("Kernel32")]
    public static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    // Gets peek console input.
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PeekConsoleInput(IntPtr hConsoleInput, out INPUT_KEY_RECORD lpBuffer, int nLength, out int lpNumberOfEventsRead);

    // Reads console input w.
    [DllImport("kernel32.dll", SetLastError = true, CharSet=CharSet.Unicode)]
    public static extern bool ReadConsoleInputW(IntPtr hConsoleInput, out INPUT_KEY_RECORD lpBuffer, int nLength, out int lpNumberOfEventsRead);

    #endregion

    private static Stopwatch mi_Timestamp = new Stopwatch();

    // Gets win timestamp.
    public static Int64 GetWinTimestamp()
    {
        mi_Timestamp.Start();

        double d_Time = mi_Timestamp.ElapsedTicks;
        d_Time *= 1000000.0;

        d_Time /= Stopwatch.Frequency;
        return (Int64)d_Time;
    }

    // Reports an operation error.
    public static void ThrowApiError(int s32_Error, String s_Format)
    {

        Win32Exception i_WinEx = new Win32Exception(s32_Error);
        throw new Exception(String.Format(s_Format, s32_Error, i_WinEx.Message));
    }

    // Extracts the requested bytes.
    public static Byte[] ExtractByteArr(Byte[] u8_Data, int s32_Start, int s32_Count = -1)
    {
        if (s32_Count < 0)
            s32_Count = u8_Data.Length - s32_Start;

        if (s32_Start < 0 || s32_Count < 0 || s32_Start + s32_Count > u8_Data.Length)
            throw new Exception("Invalid parameters");

        Byte[] u8_Copy = new Byte[s32_Count];
        Array.Copy(u8_Data, s32_Start, u8_Copy, 0, s32_Count);
        return u8_Copy;
    }

    // Joins byte arrays.
    public static Byte[] ByteArrayConcat(params Byte[][] u8_Arrays)
    {
        List<Byte> i_List = new List<Byte>();
        foreach (Byte[] u8_Array in u8_Arrays)
        {
            i_List.AddRange(u8_Array);
        }
        return i_List.ToArray();
    }

    // Compares byte arrays.
    public static bool ByteArraysEqual(Byte[] u8_Array1, Byte[] u8_Array2)
    {
        if (u8_Array1.Length != u8_Array2.Length)
            return false;

        for (int i=0; i<u8_Array1.Length; i++)
        {
            if (u8_Array1[i] != u8_Array2[i])
                return false;
        }
        return true;
    }

    // Gets bytes to structure fix.
    public static T BytesToStructureFix<T>(Byte[] u8_Bytes)
    {
        if (typeof(T) == typeof(Byte[]))
            return (T)(Object)u8_Bytes;

        int s32_Size = Marshal.SizeOf(typeof(T));
        if (s32_Size != u8_Bytes.Length)
            throw new Exception("Invalid data for structure " + typeof(T).Name);

        return BytesToStructureVar<T>(u8_Bytes, 0, s32_Size);
    }

    // Gets bytes to structure var.
    public static T BytesToStructureVar<T>(Byte[] u8_Bytes, int s32_Offset, int s32_ByteCount = int.MaxValue)
    {
        int s32_Size = Marshal.SizeOf(typeof(T));
        IntPtr p_Mem = Marshal.AllocHGlobal(s32_Size);
        memset(p_Mem, 0, (IntPtr)s32_Size);

        s32_ByteCount = Math.Min(s32_ByteCount, s32_Size);
        s32_ByteCount = Math.Min(s32_ByteCount, u8_Bytes.Length - s32_Offset);
        try
        {
            Marshal.Copy(u8_Bytes, s32_Offset, p_Mem, s32_ByteCount);
            return (T)Marshal.PtrToStructure(p_Mem, typeof(T));
        }
        finally
        {
            Marshal.FreeHGlobal(p_Mem);
        }
    }

    // Gets structure to bytes fix.
    public static Byte[] StructureToBytesFix(Object o_Structure)
    {
        if (o_Structure is Byte[])
            return (Byte[])o_Structure;

        return StructureToBytesVar(o_Structure, Marshal.SizeOf(o_Structure.GetType()));
    }

    // Gets structure to bytes var.
    public static Byte[] StructureToBytesVar(Object o_Structure, int s32_ByteCount)
    {
        int s32_Size = Marshal.SizeOf(o_Structure.GetType());
        if (s32_ByteCount > s32_Size)
            throw new Exception("Invalid size for struct conversion");

        IntPtr p_Mem = Marshal.AllocHGlobal(s32_Size);
        try
        {
            Marshal.StructureToPtr(o_Structure, p_Mem, false);
            Byte[] u8_Bytes = new Byte[s32_ByteCount];
            Marshal.Copy(p_Mem, u8_Bytes, 0, s32_ByteCount);
            return u8_Bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(p_Mem);
        }
    }

    // Gets bytes to hex.
    public static String BytesToHex(Byte[] u8_Data, int s32_First=0, int s32_MaxCount=0, String s_Delimiter=" ")
    {
        Debug.Assert(u8_Data != null);

        int s32_Last = u8_Data.Length;
        if (s32_MaxCount > 0)
            s32_Last = Math.Min(s32_Last, s32_First + s32_MaxCount);

        StringBuilder i_Hex = new StringBuilder();
        for (int i=s32_First; i<s32_Last; i++)
        {
            if (i > s32_First) i_Hex.Append(s_Delimiter);
            i_Hex.Append(u8_Data[i].ToString("X2"));
        }
        return i_Hex.ToString();
    }

    // Formats bcd version.
    public static String FormatBcdVersion(UInt32 u32_Version)
    {
        if (u32_Version == 0)
            return "0";

        if (u32_Version > 0x250101 && u32_Version < 0x991231)
        {
            Byte u8_Day   = (Byte)(u32_Version);
            Byte u8_Month = (Byte)(u32_Version >> 8);
            Byte u8_Year  = (Byte)(u32_Version >> 16);

            String s_MonthName = null;
            switch (u8_Month)
            {
                case 0x01: s_MonthName = "Jan"; break;
                case 0x02: s_MonthName = "Feb"; break;
                case 0x03: s_MonthName = "Mar"; break;
                case 0x04: s_MonthName = "Apr"; break;
                case 0x05: s_MonthName = "May"; break;
                case 0x06: s_MonthName = "Jun"; break;
                case 0x07: s_MonthName = "Jul"; break;
                case 0x08: s_MonthName = "Aug"; break;
                case 0x09: s_MonthName = "Sep"; break;
                case 0x10: s_MonthName = "Oct"; break;
                case 0x11: s_MonthName = "Nov"; break;
                case 0x12: s_MonthName = "Dec"; break;
            }
            if (s_MonthName != null && u8_Day >= 1 && u8_Day <= 31)
                return String.Format("{0:X}.{1}.{2:X2}", u8_Day, s_MonthName, u8_Year);
        }

        String s_Version = "";
        for (int s32_Shift = 24; s32_Shift >= 0; s32_Shift -= 8)
        {
            Byte u8_Part = (Byte)(u32_Version >> s32_Shift);
            if (s_Version.Length > 0)
            {
                s_Version += "." + u8_Part.ToString("X");
            }
            else if (u8_Part > 0)
            {
                s_Version += u8_Part.ToString("X");
            }
        }
        return s_Version;
    }
}
}
