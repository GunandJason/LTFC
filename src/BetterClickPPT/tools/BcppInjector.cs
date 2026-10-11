// 供 tools/verify-powerpoint.ps1 使用的点击注入工具（Add-Type -Path 编译）。
// 说明：注入事件会带上触摸特征值 0xFF515700，用来模拟“触摸被系统提升为鼠标点击”。

using System;
using System.Runtime.InteropServices;

public static class BcppInjector
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>查找放映窗口（PowerPoint 的放映窗口类名固定为 screenClass）。</summary>
    public static IntPtr FindShowWindow(string className)
    {
        return FindWindow(className, null);
    }

    /// <summary>当前前台窗口句柄。</summary>
    public static IntPtr GetForeground()
    {
        return GetForegroundWindow();
    }

    /// <summary>把窗口切到前台（会临时附加输入线程，绕过前台锁定限制）。</summary>
    public static bool ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        uint ignoredProcessId;
        uint targetThread = GetWindowThreadProcessId(hWnd, out ignoredProcessId);
        uint currentThread = GetCurrentThreadId();
        bool attached = targetThread != currentThread && AttachThreadInput(currentThread, targetThread, true);

        try
        {
            ShowWindow(hWnd, 9); // SW_RESTORE
            bool result = SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);
            return result;
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
        }
    }

    /// <summary>把窗口设为置顶（用于验证“放映窗口不在前台”时的翻页）。</summary>
    public static bool MakeTopmost(IntPtr hWnd, bool topmost)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr insertAfter = topmost ? new IntPtr(-1) : new IntPtr(-2);
        return SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
    }

    /// <summary>取窗口矩形：返回 [left, top, width, height]。</summary>
    public static int[] GetWindowBounds(IntPtr hWnd)
    {
        RECT rect;
        if (!GetWindowRect(hWnd, out rect))
        {
            return new[] { 0, 0, 0, 0 };
        }

        return new[] { rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top };
    }

    /// <summary>窗口是否为当前前台窗口。</summary>
    public static bool IsForeground(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && GetForegroundWindow() == hWnd;
    }

    /// <summary>把当前 PowerShell 进程设为按显示器 DPI 感知，保证坐标与钩子一致。</summary>
    public static void MakeDpiAware()
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch
        {
        }
    }

    /// <summary>在指定屏幕坐标点击一次；asTouch=true 时携带触摸特征值。</summary>
    public static string Click(int x, int y, bool asTouch)
    {
        MakeDpiAware();

        int width = GetSystemMetrics(0);
        int height = GetSystemMetrics(1);
        int normalizedX = (int)Math.Round(x * 65535.0 / Math.Max(1, width - 1));
        int normalizedY = (int)Math.Round(y * 65535.0 / Math.Max(1, height - 1));
        IntPtr extra = asTouch ? new IntPtr(unchecked((int)0xFF515700)) : IntPtr.Zero;

        Move(normalizedX, normalizedY, extra);
        System.Threading.Thread.Sleep(40);
        SendFlags(0x0002, extra); // MOUSEEVENTF_LEFTDOWN
        System.Threading.Thread.Sleep(50);
        SendFlags(0x0004, extra); // MOUSEEVENTF_LEFTUP

        return string.Format("click({0},{1}) touch={2} screen={3}x{4}", x, y, asTouch, width, height);
    }

    private static void Move(int normalizedX, int normalizedY, IntPtr extra)
    {
        var inputs = new INPUT[1];
        inputs[0].type = 0;
        inputs[0].u.mi = new MOUSEINPUT
        {
            dx = normalizedX,
            dy = normalizedY,
            dwFlags = 0x8001, // MOVE | ABSOLUTE
            time = 0,
            dwExtraInfo = extra
        };

        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    private static void SendFlags(uint flags, IntPtr extra)
    {
        var inputs = new INPUT[1];
        inputs[0].type = 0;
        inputs[0].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = flags, time = 0, dwExtraInfo = extra };
        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>带特征值的按键注入（备用：可用于验证 SendInput 键盘通道）。</summary>
    public static void SendPageDown()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1;
        inputs[0].u.ki.wVk = 0x22;
        inputs[1].type = 1;
        inputs[1].u.ki.wVk = 0x22;
        inputs[1].u.ki.dwFlags = 0x0002;
        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
