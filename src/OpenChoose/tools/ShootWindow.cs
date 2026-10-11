// ShootWindow.cs —— 测试辅助程序：把指定进程的可见窗口画面保存成 PNG
//   ShootWindow.exe <pid> <输出 png>
// 用 PrintWindow 抓取，按窗口真实像素渲染，不受调用方 DPI 感知影响。
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

internal static class ShootWindow
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

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
            Console.WriteLine("usage: ShootWindow.exe <pid> <output png>");
            return 1;
        }

        uint pid = uint.Parse(args[0]);
        IntPtr target = IntPtr.Zero;

        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint owner;
            GetWindowThreadProcessId(hWnd, out owner);
            if (owner != pid || !IsWindowVisible(hWnd))
            {
                return true;
            }

            StringBuilder title = new StringBuilder(512);
            GetWindowText(hWnd, title, title.Capacity);
            if (title.Length > 0)
            {
                target = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero)
        {
            Console.WriteLine("no visible window found");
            return 2;
        }

        RECT rect;
        GetWindowRect(target, out rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            Console.WriteLine("bad window rect");
            return 3;
        }

        using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    // 2 = PW_RENDERFULLCONTENT（Win8.1+，可抓到 DirectComposition 内容）
                    PrintWindow(target, hdc, 2);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            bitmap.Save(args[1], ImageFormat.Png);
        }

        Console.WriteLine("saved " + args[1] + " (" + width + "x" + height + ")");
        return 0;
    }
}
