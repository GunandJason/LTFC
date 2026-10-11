using System.Runtime.InteropServices;
using BetterClickPPT.Core.Interop;

namespace BetterClickPPT.SelfTest;

/// <summary>模拟触摸 / 鼠标点击（注入事件，并带上触摸特征值以模拟被系统“提升”的触摸点击）。</summary>
internal static class InputSimulator
{
    /// <summary>Windows 为触摸 / 笔“提升”出的鼠标消息设置的特征值（MI_WP_SIGNATURE）。</summary>
    public const uint TouchSignature = 0xFF515700;

    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventAbsolute = 0x8000;

    /// <summary>
    /// 在屏幕坐标处模拟一次点击。
    /// 返回 false 表示注入超时（例如桌面输入队列被其它程序占用，SendInput 被阻塞），
    /// 这样测试可以“跳过”而不是卡死。
    /// </summary>
    public static bool Click(int screenX, int screenY, bool asTouch, Action<string>? trace = null, int timeoutMs = 4000)
    {
        var finished = new ManualResetEventSlim(false);

        var worker = new Thread(() =>
        {
            try
            {
                ClickCore(screenX, screenY, asTouch, trace);
            }
            catch
            {
                // 忽略（超时判定由外层负责）。
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
            Name = "BetterClickPPT.TestInput"
        };

        worker.Start();
        return finished.Wait(timeoutMs);
    }

    private static void ClickCore(int screenX, int screenY, bool asTouch, Action<string>? trace)
    {
        var extra = asTouch ? new IntPtr(TouchSignature) : IntPtr.Zero;
        var virtualScreen = NativeMethods.GetVirtualScreenBounds();

        var normalizedX = (int)Math.Round((screenX - virtualScreen.Left) * 65535.0 / Math.Max(1, virtualScreen.Width - 1));
        var normalizedY = (int)Math.Round((screenY - virtualScreen.Top) * 65535.0 / Math.Max(1, virtualScreen.Height - 1));

        trace?.Invoke($"目标 {screenX},{screenY} → 归一化 {normalizedX},{normalizedY}（虚拟屏 {virtualScreen}）");

        SendAbsolute(MouseEventMove | MouseEventAbsolute, normalizedX, normalizedY, extra);
        trace?.Invoke($"移动后光标：{Cursor.Position.X},{Cursor.Position.Y}");
        Pump.Wait(30);

        SendAbsolute(MouseEventLeftDown | MouseEventAbsolute, normalizedX, normalizedY, extra);
        trace?.Invoke($"按下后光标：{Cursor.Position.X},{Cursor.Position.Y}");
        Pump.Wait(40);

        SendAbsolute(MouseEventLeftUp | MouseEventAbsolute, normalizedX, normalizedY, extra);
        trace?.Invoke($"抬起后光标：{Cursor.Position.X},{Cursor.Position.Y}");
    }

    private static void SendAbsolute(uint flags, int dx, int dy, IntPtr extraInfo)
    {
        var input = new INPUT[1];
        input[0].type = 0; // INPUT_MOUSE
        input[0].u.mi = new MOUSEINPUT
        {
            dx = dx,
            dy = dy,
            mouseData = 0,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = extraInfo
        };

        NativeMethods.SendInput(1, input, Marshal.SizeOf<INPUT>());
    }
}
