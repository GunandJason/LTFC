namespace BetterClickPPT.MockShow;

/// <summary>模拟放映窗口程序入口。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // 与主程序保持一致：按显示器 DPI 感知，保证窗口坐标与钩子坐标在同一坐标系
        TrySetPerMonitorDpiAware();

        var options = MockShowOptions.Parse(args);
        using var log = new LogWriter(options.LogPath);
        log.WriteLine($"ARGS {string.Join(" ", args)} logpath={options.LogPath ?? "(空)"} cwd={Directory.GetCurrentDirectory()}");

        using var window = new MockWindow(options, log);
        if (!window.Create())
        {
            return 1;
        }

        if (options.FullScreen)
        {
            window.MakeFullScreen();
        }

        window.Show();
        log.WriteLine($"READY hwnd=0x{window.Handle.ToInt64():X} class={options.ClassName} "
                      + $"rect={window.Rect.Left},{window.Rect.Top},{window.Rect.Width}x{window.Rect.Height} fullscreen={options.FullScreen}");

        window.RunMessageLoop();
        return 0;
    }

    /// <summary>设置进程为按显示器 DPI 感知（PerMonitorV2），保证坐标与鼠标钩子一致。</summary>
    private static void TrySetPerMonitorDpiAware()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
            SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch
        {
            // 老系统不支持时忽略，退回系统 DPI 感知。
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
