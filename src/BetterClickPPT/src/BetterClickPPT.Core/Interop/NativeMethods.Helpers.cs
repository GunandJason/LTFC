using System.Runtime.InteropServices;
using System.Text;

namespace BetterClickPPT.Core.Interop;

/// <summary>NativeMethods 的便捷辅助方法。</summary>
public static partial class NativeMethods
{
    /// <summary>读取窗口样式（GWL_STYLE）。</summary>
    public static long GetStyle(IntPtr hWnd) => GetWindowLongPtr(hWnd, NativeConstants.GWL_STYLE).ToInt64();

    /// <summary>读取窗口扩展样式（GWL_EXSTYLE）。</summary>
    public static long GetExStyle(IntPtr hWnd) => GetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE).ToInt64();

    /// <summary>读取窗口类名（失败时返回空字符串）。</summary>
    public static string GetClassNameSafe(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    /// <summary>读取窗口标题（失败时返回空字符串）。</summary>
    public static string GetWindowTitleSafe(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        var copied = GetWindowText(hWnd, buffer, buffer.Capacity);
        return copied > 0 ? buffer.ToString(0, copied) : string.Empty;
    }

    /// <summary>判断窗口是否带标题栏（用于排除“最大化的编辑窗口”）。</summary>
    public static bool HasCaption(IntPtr hWnd) => (GetStyle(hWnd) & NativeConstants.WS_CAPTION) == NativeConstants.WS_CAPTION;

    /// <summary>获取窗口所在显示器的矩形。</summary>
    public static System.Drawing.Rectangle GetMonitorBounds(IntPtr hWnd)
    {
        var monitor = MonitorFromWindow(hWnd, NativeConstants.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            return info.rcMonitor.ToRectangle();
        }

        return System.Drawing.Rectangle.Empty;
    }

    /// <summary>获取虚拟屏幕（所有显示器合并）的矩形。</summary>
    public static System.Drawing.Rectangle GetVirtualScreenBounds()
    {
        var x = GetSystemMetrics(NativeConstants.SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(NativeConstants.SM_YVIRTUALSCREEN);
        return System.Drawing.Rectangle.FromLTRB(
            x,
            y,
            x + GetSystemMetrics(NativeConstants.SM_CXVIRTUALSCREEN),
            y + GetSystemMetrics(NativeConstants.SM_CYVIRTUALSCREEN));
    }
}
