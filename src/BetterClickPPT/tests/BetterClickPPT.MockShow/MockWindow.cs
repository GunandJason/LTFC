using System.Runtime.InteropServices;
using BetterClickPPT.Core.Interop;

namespace BetterClickPPT.MockShow;

/// <summary>模拟放映窗口（原生 Win32 窗口，类名可指定为 screenClass）。</summary>
internal sealed class MockWindow : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly MockShowOptions _options;
    private readonly LogWriter _log;
    private readonly WndProcDelegate _wndProc;
    private readonly IntPtr _hInstance;
    private readonly DateTime _startedAt = DateTime.Now;

    private IntPtr _hwnd;
    private IntPtr _font;
    private int _slide = 1;
    private int _nextCount;
    private int _clickDownCount;
    private int _clickUpCount;
    private bool _fullScreen;

    public MockWindow(MockShowOptions options, LogWriter log)
    {
        _options = options;
        _log = log;
        _wndProc = WindowProc;
        _hInstance = NativeMethods.GetModuleHandle(null);
    }

    public IntPtr Handle => _hwnd;

    public System.Drawing.Rectangle Rect { get; private set; }

    /// <summary>注册窗口类并创建（隐藏的）窗口。</summary>
    public bool Create()
    {
        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = _options.ClassName
        };

        var atom = NativeMethods.RegisterClass(ref wndClass);
        if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            _log.WriteLine($"ERROR 注册窗口类失败：{Marshal.GetLastWin32Error()}");
            return false;
        }

        _hwnd = NativeMethods.CreateWindowEx(
            WsExTopmost,
            _options.ClassName,
            _options.Title,
            WsPopup,
            _options.X,
            _options.Y,
            _options.Width,
            _options.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            _log.WriteLine($"ERROR 创建窗口失败：{Marshal.GetLastWin32Error()}");
            return false;
        }

        _font = CreateFont(-28, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 0, 0, "Microsoft YaHei UI");
        UpdateRect();
        return true;
    }

    /// <summary>切换为铺满主显示器的无边框全屏窗口。</summary>
    public void MakeFullScreen()
    {
        var screenWidth = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN);
        var screenHeight = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN);

        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, screenWidth, screenHeight, SwpNoZOrder | SwpNoActivate);
        _fullScreen = true;
        UpdateRect();
    }

    /// <summary>显示窗口并尝试置于前台。</summary>
    public void Show()
    {
        ShowWindow(_hwnd, 5);
        NativeMethods.SetForegroundWindow(_hwnd);
        InvalidateRect(_hwnd, IntPtr.Zero, true);
        UpdateWindow(_hwnd);
    }

    /// <summary>运行消息循环，最多运行 options.Seconds 秒。</summary>
    public void RunMessageLoop()
    {
        SetTimer(_hwnd, new IntPtr(1), 200, IntPtr.Zero);

        var message = default(MSG);
        while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_font != IntPtr.Zero)
        {
            DeleteObject(_font);
            _font = IntPtr.Zero;
        }
    }

    private void UpdateRect()
    {
        if (NativeMethods.GetWindowRect(_hwnd, out var rect))
        {
            Rect = rect.ToRectangle();
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case 0x000F: // WM_PAINT
                Paint(hWnd);
                return IntPtr.Zero;

            case NativeConstants.WM_LBUTTONDOWN:
                _clickDownCount++;
                _log.WriteLine($"CLICK_DOWN x={LowWord(lParam)} y={HighWord(lParam)} count={_clickDownCount}");
                return IntPtr.Zero;

            case NativeConstants.WM_LBUTTONUP:
                _clickUpCount++;
                _log.WriteLine($"CLICK_UP x={LowWord(lParam)} y={HighWord(lParam)} count={_clickUpCount}");
                return IntPtr.Zero;

            case NativeConstants.WM_KEYDOWN:
            {
                var virtualKey = wParam.ToInt32();
                if (virtualKey == NativeConstants.VK_NEXT)
                {
                    _nextCount++;
                    _slide++;
                    _log.WriteLine($"NEXT count={_nextCount} slide={_slide}");
                    InvalidateRect(hWnd, IntPtr.Zero, true);
                }
                else
                {
                    _log.WriteLine($"KEY vk=0x{virtualKey:X2}");
                }

                return IntPtr.Zero;
            }

            case NativeConstants.WM_KEYUP:
                return IntPtr.Zero;

            case 0x0113: // WM_TIMER
                if ((DateTime.Now - _startedAt).TotalSeconds >= _options.Seconds)
                {
                    _log.WriteLine("EXIT timeout");
                    PostQuitMessage(0);
                }

                return IntPtr.Zero;

            case NativeConstants.WM_DESTROY:
                _log.WriteLine("DESTROY");
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Paint(IntPtr hWnd)
    {
        var paint = default(PAINTSTRUCT);
        var dc = BeginPaint(hWnd, ref paint);

        try
        {
            var client = new RECT { Left = 0, Top = 0, Right = Rect.Width, Bottom = Rect.Height };
            var brush = CreateSolidBrush(0x00202020);
            FillRect(dc, ref client, brush);
            DeleteObject(brush);

            SetTextColor(dc, 0x00FFFFFF);
            SetBkMode(dc, 1);
            var previousFont = SelectObject(dc, _font);
            var text = $"模拟放映窗口  slide={_slide}  NEXT={_nextCount}  CLICK={_clickDownCount}"
                       + (_fullScreen ? "  [全屏]" : string.Empty);
            TextOut(dc, 40, 40, text, text.Length);
            SelectObject(dc, previousFont);
        }
        finally
        {
            EndPaint(hWnd, ref paint);
        }
    }

    private static int LowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    private static int HighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        public long rgb1;
        public long rgb2;
        public long rgb3;
        public long rgb4;
    }

    [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")] private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIdEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOut(IntPtr hdc, int x, int y, string lpString, int cchString);

    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);
}
