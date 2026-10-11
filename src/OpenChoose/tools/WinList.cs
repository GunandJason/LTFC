// WinList.cs —— 测试辅助程序：列出指定进程的所有顶层窗口
//   WinList.exe <pid> <输出文件>
// 用于在无人工点击的情况下验证界面窗口标题。
using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class WinList
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: WinList.exe <pid> <output file>");
            return 1;
        }

        uint pid = uint.Parse(args[0]);
        StringBuilder report = new StringBuilder();

        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint owner;
            GetWindowThreadProcessId(hWnd, out owner);
            if (owner != pid)
            {
                return true;
            }

            StringBuilder title = new StringBuilder(512);
            GetWindowText(hWnd, title, title.Capacity);
            StringBuilder className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);

            RECT window;
            GetWindowRect(hWnd, out window);
            RECT client;
            GetClientRect(hWnd, out client);

            report.AppendLine("hwnd=" + hWnd.ToInt64() +
                " visible=" + IsWindowVisible(hWnd) +
                " rect=" + window.Left + "," + window.Top + "," + window.Right + "," + window.Bottom +
                " client=" + client.Right + "x" + client.Bottom +
                " class=" + className +
                " title=[" + title + "]");
            return true;
        }, IntPtr.Zero);

        System.IO.File.WriteAllText(args[1], report.ToString(), new UTF8Encoding(true));
        return 0;
    }
}
