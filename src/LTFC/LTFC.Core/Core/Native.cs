using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LTFC
{
    /// <summary>少量原生调用（DPI 解析、圆角等）。</summary>
    internal static class Native
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        private const int LogPixelsX = 88;

        /// <summary>
        /// 解析当前窗口所在显示器的 DPI 缩放系数。
        /// 顺序：GetDpiForWindow（Win10 1607+）→ 屏幕 DC 的系统 DPI（Win7/8 可用）→ 传入的兜底值。
        /// </summary>
        public static float ResolveDpiScale(IntPtr hwnd, int fallbackDeviceDpi)
        {
            int dpi = 0;

            try
            {
                if (hwnd != IntPtr.Zero) dpi = (int)GetDpiForWindow(hwnd);
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 7 / 8 没有该 API，走系统 DPI
            }
            catch (Exception)
            {
            }

            if (dpi <= 0) dpi = GetSystemDpi();
            if (dpi <= 0) dpi = fallbackDeviceDpi;
            if (dpi <= 0) dpi = 96;

            return dpi / 96f;
        }

        /// <summary>系统 DPI（屏幕 DC 的每逻辑英寸像素数）。</summary>
        public static int GetSystemDpi()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    int dpi = GetDeviceCaps(dc, LogPixelsX);
                    ReleaseDC(IntPtr.Zero, dc);
                    if (dpi > 0) return dpi;
                }
            }
            catch (Exception)
            {
            }

            return 0;
        }

        // ---------- 窗口激活（重复点击模块时“显示已有窗口”，不开新的实例） ----------

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

        private const int SwShowNormal = 1;
        private const int SwRestore = 9;

        /// <summary>
        /// 把某个进程的主窗口显示出来并置于前台。返回是否成功找到并激活了窗口。
        /// 说明：只做“显示已有窗口”，不会创建新窗口。
        /// </summary>
        public static bool ActivateWindowOfProcess(int processId, IntPtr preferred)
        {
            IntPtr hwnd = preferred;
            if (hwnd == IntPtr.Zero) hwnd = FindTopLevelWindowForProcess(processId);
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                else ShowWindow(hwnd, SwShowNormal);

                if (SetForegroundWindow(hwnd)) return true;

                // 前台锁定（例如主程序刚接收到输入）时，附加到前台线程再试一次
                IntPtr foreground = GetForegroundWindow();
                uint otherThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
                uint thisThread = GetCurrentThreadId();
                if (otherThread != 0 && otherThread != thisThread)
                {
                    if (AttachThreadInput(otherThread, thisThread, true))
                    {
                        bool ok = SetForegroundWindow(hwnd);
                        AttachThreadInput(otherThread, thisThread, false);
                        if (ok) return true;
                    }
                }

                AllowSetForegroundWindow(processId);
                return SetForegroundWindow(hwnd);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>在进程的所有顶层窗口中挑一个“像主窗口”的（可见、标题最长）。</summary>
        public static IntPtr FindTopLevelWindowForProcess(int processId)
        {
            IntPtr best = IntPtr.Zero;
            int bestLength = -1;

            try
            {
                EnumWindows((hwnd, _) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid != (uint)processId) return true;
                    if (!IsWindowVisible(hwnd)) return true;

                    int length = GetWindowTextLength(hwnd);
                    if (length <= 0) return true;

                    if (length > bestLength)
                    {
                        best = hwnd;
                        bestLength = length;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception)
            {
            }

            return best;
        }

        /// <summary>窗口标题（诊断用）。</summary>
        public static string WindowTitle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            try
            {
                int length = GetWindowTextLength(hwnd);
                if (length <= 0) return string.Empty;
                var buffer = new System.Text.StringBuilder(length + 2);
                GetWindowText(hwnd, buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
