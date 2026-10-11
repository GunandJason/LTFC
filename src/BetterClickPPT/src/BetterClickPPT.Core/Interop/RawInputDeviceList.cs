using System.Runtime.InteropServices;

namespace BetterClickPPT.Core.Interop;

/// <summary>RAWINPUTDEVICELIST 结构。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTDEVICELIST
{
    public IntPtr hDevice;
    public uint dwType;
}

/// <summary>
/// RID_DEVICE_INFO 结构（这里只使用 HID 部分，其余用填充字段占位，保证 32 / 64 位下均为 32 字节）。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RID_DEVICE_INFO
{
    public uint cbSize;
    public uint dwType;
    public uint dwVendorId;
    public uint dwProductId;
    public uint dwVersionNumber;
    public ushort usUsagePage;
    public ushort usUsage;
    public uint Padding1;
    public uint Padding2;
}

/// <summary>系统输入设备信息（用于环境检测报告）。</summary>
public sealed record RawInputDeviceInfo(IntPtr Handle, uint Type, ushort UsagePage, ushort Usage, string? Name)
{
    /// <summary>是否是触摸屏数字化仪。</summary>
    public bool IsTouchScreen => Type == RawInputInterop.RIM_TYPEHID
        && UsagePage == HidUsagePages.Digitizer
        && Usage == HidDigitizerUsages.TouchScreen;

    /// <summary>是否是手写笔。</summary>
    public bool IsPen => Type == RawInputInterop.RIM_TYPEHID
        && UsagePage == HidUsagePages.Digitizer
        && Usage == HidDigitizerUsages.Pen;

    /// <summary>是否是触摸板。</summary>
    public bool IsTouchPad => Type == RawInputInterop.RIM_TYPEHID
        && UsagePage == HidUsagePages.Digitizer
        && Usage == HidDigitizerUsages.TouchPad;

    /// <summary>设备类型描述。</summary>
    public string KindDescription => Type switch
    {
        RawInputInterop.RIM_TYPEHID when IsTouchScreen => "触摸屏（数字化仪）",
        RawInputInterop.RIM_TYPEHID when IsPen => "手写笔",
        RawInputInterop.RIM_TYPEHID when IsTouchPad => "触摸板",
        RawInputInterop.RIM_TYPEHID => $"HID 设备（0x{UsagePage:X2}/0x{Usage:X2}）",
        RawInputInterop.RIM_TYPEMOUSE => "鼠标",
        RawInputInterop.RIM_TYPEKEYBOARD => "键盘",
        _ => "未知"
    };
}

/// <summary>Raw Input 设备枚举。</summary>
public static class RawInputDeviceEnumerator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    /// <summary>枚举当前系统的输入设备（触摸屏 / 手写笔 / 鼠标 / 键盘）。</summary>
    public static IReadOnlyList<RawInputDeviceInfo> Enumerate()
    {
        var result = new List<RawInputDeviceInfo>();

        try
        {
            uint deviceCount = 0;
            var size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            if (GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, size) != 0 || deviceCount == 0)
            {
                return result;
            }

            var buffer = Marshal.AllocHGlobal((int)(size * deviceCount));
            try
            {
                var count = GetRawInputDeviceList(buffer, ref deviceCount, size);
                if (count == uint.MaxValue)
                {
                    return result;
                }

                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(buffer + (int)(size * i));
                    ushort usagePage = 0;
                    ushort usage = 0;

                    if (item.dwType == RawInputInterop.RIM_TYPEHID)
                    {
                        var info = new RID_DEVICE_INFO { cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>() };
                        var infoSize = info.cbSize;
                        var infoBuffer = Marshal.AllocHGlobal((int)infoSize);
                        try
                        {
                            Marshal.StructureToPtr(info, infoBuffer, fDeleteOld: false);
                            if (RawInputInterop.GetRawInputDeviceInfo(item.hDevice, RawInputInterop.RIDI_DEVICEINFO, infoBuffer, ref infoSize) != uint.MaxValue)
                            {
                                var parsed = Marshal.PtrToStructure<RID_DEVICE_INFO>(infoBuffer);
                                usagePage = parsed.usUsagePage;
                                usage = parsed.usUsage;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(infoBuffer);
                        }
                    }

                    result.Add(new RawInputDeviceInfo(
                        item.hDevice,
                        item.dwType,
                        usagePage,
                        usage,
                        RawInputInterop.GetDeviceName(item.hDevice)));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // 枚举失败时返回已收集到的内容。
        }

        return result;
    }
}
