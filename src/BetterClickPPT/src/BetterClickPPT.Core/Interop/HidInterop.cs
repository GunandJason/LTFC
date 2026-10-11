using System.Runtime.InteropServices;

namespace BetterClickPPT.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct HIDP_CAPS
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
    public ushort[] Reserved;

    public ushort NumberLinkCollectionNodes;
    public ushort NumberInputButtonCaps;
    public ushort NumberInputValueCaps;
    public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps;
    public ushort NumberOutputValueCaps;
    public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps;
    public ushort NumberFeatureValueCaps;
    public ushort NumberFeatureDataIndices;
}

[StructLayout(LayoutKind.Sequential)]
public struct HIDP_RANGE
{
    public ushort UsageMin;
    public ushort UsageMax;
    public ushort StringMin;
    public ushort StringMax;
    public ushort DesignatorMin;
    public ushort DesignatorMax;
    public ushort DataIndexMin;
    public ushort DataIndexMax;
}

[StructLayout(LayoutKind.Sequential)]
public struct HIDP_VALUE_CAPS
{
    public ushort UsagePage;
    public byte ReportID;

    [MarshalAs(UnmanagedType.U1)] public bool IsAlias;

    public ushort BitField;
    public ushort LinkCollection;
    public ushort LinkUsage;
    public ushort LinkUsagePage;

    [MarshalAs(UnmanagedType.U1)] public bool IsRange;
    [MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
    [MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
    [MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
    [MarshalAs(UnmanagedType.U1)] public bool HasNull;

    public byte Reserved;
    public ushort BitSize;
    public ushort ReportCount;
    public ushort Reserved2a;
    public ushort Reserved2b;
    public ushort Reserved2c;
    public ushort Reserved2d;
    public ushort Reserved2e;
    public uint UnitsExp;
    public uint Units;
    public int LogicalMin;
    public int LogicalMax;
    public int PhysicalMin;
    public int PhysicalMax;
    public HIDP_RANGE Range;
    public HIDP_RANGE StringRange;
    public HIDP_RANGE DesignatorRange;
}

/// <summary>hid.dll 导入：解析 HID 报告描述符并按用途读取报告数据。</summary>
public static class HidInterop
{
    public const uint HIDP_STATUS_SUCCESS = 0x00110000;
    public const uint HIDP_INPUT = 0;

    public const int HIDP_STATUS_USAGE_NOT_FOUND = unchecked((int)0xC0110004);
    public const int HIDP_STATUS_INCOMPATIBLE_REPORT_ID = unchecked((int)0xC011000A);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetValueCaps(uint reportType, [Out] HIDP_VALUE_CAPS[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetUsageValue(
        uint reportType,
        ushort usagePage,
        ushort linkCollection,
        ushort usage,
        out uint usageValue,
        IntPtr preparsedData,
        byte[] report,
        uint reportLength);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetUsages(
        uint reportType,
        ushort usagePage,
        ushort linkCollection,
        [Out] ushort[] usageList,
        ref uint usageLength,
        IntPtr preparsedData,
        byte[] report,
        uint reportLength);

    /// <summary>读取指定用途的数值；读不到时返回 false（不抛异常）。</summary>
    public static bool TryGetUsageValue(
        IntPtr preparsedData,
        byte[] report,
        ushort usagePage,
        ushort usage,
        ushort linkCollection,
        out uint value)
    {
        value = 0;
        try
        {
            var status = HidP_GetUsageValue(HIDP_INPUT, usagePage, linkCollection, usage, out value, preparsedData, report, (uint)report.Length);
            return status == HIDP_STATUS_SUCCESS;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>读取指定用途在该链接集合中的全部取值（多触点数组用法）。</summary>
    public static bool TryGetUsages(
        IntPtr preparsedData,
        byte[] report,
        ushort usagePage,
        ushort linkCollection,
        ushort[] usageList,
        out uint count)
    {
        count = 0;
        try
        {
            count = (uint)usageList.Length;
            var status = HidP_GetUsages(HIDP_INPUT, usagePage, linkCollection, usageList, ref count, preparsedData, report, (uint)report.Length);
            return status == HIDP_STATUS_SUCCESS;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
