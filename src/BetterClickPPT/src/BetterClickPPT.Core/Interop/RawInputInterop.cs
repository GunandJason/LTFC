using System.Runtime.InteropServices;

namespace BetterClickPPT.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public IntPtr hwndTarget;

    public RAWINPUTDEVICE(ushort usagePage, ushort usage, uint flags, IntPtr target)
    {
        usUsagePage = usagePage;
        usUsage = usage;
        dwFlags = flags;
        hwndTarget = target;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTHEADER
{
    public uint dwType;
    public uint dwSize;
    public IntPtr hDevice;
    public IntPtr wParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct RAWHID
{
    public uint dwSizeHid;
    public uint dwCount;
}

/// <summary>HID 用途页常量。</summary>
public static class HidUsagePages
{
    public const ushort GenericDesktop = 0x01;
    public const ushort Digitizer = 0x0D;
}

/// <summary>Digitizer 用途页下的常用用途。</summary>
public static class HidDigitizerUsages
{
    /// <summary>数字化仪（通用）。</summary>
    public const ushort Digitizer = 0x01;

    /// <summary>笔。</summary>
    public const ushort Pen = 0x02;

    /// <summary>触摸屏。</summary>
    public const ushort TouchScreen = 0x04;

    /// <summary>触摸板（不监听，避免误触）。</summary>
    public const ushort TouchPad = 0x05;

    /// <summary>In Range（接触检测）。</summary>
    public const ushort InRange = 0x32;

    /// <summary>Tip Switch（笔尖 / 手指接触开关）。</summary>
    public const ushort TipSwitch = 0x42;

    /// <summary>Contact Count（同时接触点数）。</summary>
    public const ushort ContactCount = 0x55;
}

/// <summary>Generic Desktop 用途页下的常用用途。</summary>
public static class HidDesktopUsages
{
    public const ushort X = 0x30;
    public const ushort Y = 0x31;
}

/// <summary>Raw Input 相关导入。</summary>
public static class RawInputInterop
{
    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;

    public const uint RID_INPUT = 0x10000003;

    public const uint RIDI_DEVICEINFO = 0x2000000b;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_PREPARSEDDATA = 0x20000005;

    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    /// <summary>读取设备名（诊断用）。</summary>
    public static string? GetDeviceName(IntPtr hDevice)
    {
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return null;
        }

        // 文档中 RIDI_DEVICENAME 的长度单位是“字符数”，这里按字符数 + 结束符分配，
        // 并整段清零，避免任何尾部残留数据被误读为字符串。
        var buffer = Marshal.AllocHGlobal((int)size * 2 + 2);
        try
        {
            for (var i = 0; i < (int)size * 2 + 2; i++)
            {
                Marshal.WriteByte(buffer, i, 0);
            }

            var requested = size;
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref requested) == uint.MaxValue || requested == 0)
            {
                return null;
            }

            var name = Marshal.PtrToStringUni(buffer, (int)requested) ?? string.Empty;
            var terminator = name.IndexOf('\0');
            if (terminator >= 0)
            {
                name = name[..terminator];
            }

            return name.Length == 0 ? null : name;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>注册 HID 数字化仪（触摸屏 / 笔）输入监听，后台也能接收。</summary>
    public static bool RegisterDigitizer(IntPtr hwnd, out int errorCode)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE(HidUsagePages.Digitizer, HidDigitizerUsages.TouchScreen, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwnd),
            new RAWINPUTDEVICE(HidUsagePages.Digitizer, HidDigitizerUsages.Pen, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwnd),
            new RAWINPUTDEVICE(HidUsagePages.Digitizer, HidDigitizerUsages.Digitizer, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwnd)
        };

        var result = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        errorCode = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    /// <summary>注销 Raw Input 监听。</summary>
    public static bool UnregisterDigitizer(IntPtr hwnd, out int errorCode)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE(HidUsagePages.Digitizer, HidDigitizerUsages.TouchScreen, RIDEV_REMOVE, IntPtr.Zero),
            new RAWINPUTDEVICE(HidUsagePages.Digitizer, HidDigitizerUsages.Pen, RIDEV_REMOVE, IntPtr.Zero),
            new RAWINPUTDEVICE(HidUsagePages.Digitizer, HidDigitizerUsages.Digitizer, RIDEV_REMOVE, IntPtr.Zero)
        };

        var result = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        errorCode = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }
}
