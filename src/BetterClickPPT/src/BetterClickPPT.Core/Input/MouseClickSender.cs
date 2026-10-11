using System.Runtime.InteropServices;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Input;

/// <summary>
/// 在指定屏幕坐标合成一次真实的鼠标左键点击。
/// 用途：把“触摸轻触”还原成鼠标点击，从而让放映程序按原有的点击逻辑处理
/// （分步动画、超链接、屏幕上的“上一页 / 下一页”按钮等）。
/// </summary>
public sealed class MouseClickSender
{
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventAbsolute = 0x8000;

    /// <summary>坐标按整个虚拟桌面（多显示器）计算，而不是仅主显示器。</summary>
    private const uint MouseEventVirtualDesk = 0x4000;

    private readonly AppLog? _log;

    public MouseClickSender(AppLog? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// 在屏幕坐标处点击一次（注入事件不带触摸特征值，因此不会被本程序再次识别为触摸，
    /// 也不会被自己的钩子拦下）。
    /// </summary>
    public bool ClickAt(System.Drawing.Point screenPoint, out string detail)
    {
        var virtualScreen = NativeMethods.GetVirtualScreenBounds();
        if (virtualScreen.Width <= 0 || virtualScreen.Height <= 0)
        {
            detail = "无法获取桌面尺寸";
            return false;
        }

        var normalizedX = NormalizeCoordinate(screenPoint.X, virtualScreen.Left, virtualScreen.Width);
        var normalizedY = NormalizeCoordinate(screenPoint.Y, virtualScreen.Top, virtualScreen.Height);

        var move = Send(MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk, normalizedX, normalizedY);
        var down = Send(MouseEventLeftDown, 0, 0);
        var up = Send(MouseEventLeftUp, 0, 0);

        detail = $"在 {screenPoint.X},{screenPoint.Y} 模拟鼠标点击（{virtualScreen.Width}×{virtualScreen.Height}）";
        if (move && down && up)
        {
            return true;
        }

        _log?.Warn($"模拟鼠标点击失败（错误码 {Marshal.GetLastWin32Error()}）：{detail}");
        return false;
    }

    /// <summary>把屏幕坐标换算为 0~65535 的绝对坐标。</summary>
    public static int NormalizeCoordinate(int value, int origin, int size)
    {
        var span = Math.Max(1, size - 1);
        var normalized = (value - origin) * 65535.0 / span;
        return (int)Math.Round(Math.Clamp(normalized, 0, 65535));
    }

    private static bool Send(uint flags, int dx, int dy)
    {
        var inputs = new INPUT[1];
        inputs[0].type = 0; // INPUT_MOUSE
        inputs[0].u.mi = new MOUSEINPUT
        {
            dx = dx,
            dy = dy,
            mouseData = 0,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        return NativeMethods.SendInput(1, inputs, Marshal.SizeOf<INPUT>()) == 1;
    }
}
