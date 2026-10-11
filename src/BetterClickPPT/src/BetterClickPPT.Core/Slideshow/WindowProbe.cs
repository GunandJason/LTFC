using BetterClickPPT.Core.Interop;

namespace BetterClickPPT.Core.Slideshow;

/// <summary>被探测窗口的信息快照。</summary>
public sealed record WindowProbe(
    IntPtr Handle,
    string ClassName,
    string Title,
    uint ProcessId,
    string ProcessName,
    System.Drawing.Rectangle Bounds,
    System.Drawing.Rectangle MonitorBounds,
    bool IsFullScreen,
    bool HasCaption,
    bool IsVisible,
    bool IsMinimized)
{
    /// <summary>多行诊断信息（供界面显示）。</summary>
    public string ToDiagnosticText()
    {
        var lines = new List<string>
        {
            $"窗口句柄   : 0x{Handle.ToInt64():X}",
            $"标题       : {Title}",
            $"类名       : {ClassName}",
            $"进程       : {ProcessName} (PID {ProcessId})",
            $"窗口矩形   : {Bounds.Left},{Bounds.Top} {Bounds.Width}×{Bounds.Height}",
            $"显示器矩形 : {MonitorBounds.Left},{MonitorBounds.Top} {MonitorBounds.Width}×{MonitorBounds.Height}",
            $"全屏       : {(IsFullScreen ? "是" : "否")}",
            $"标题栏     : {(HasCaption ? "有" : "无")}",
            $"可见 / 最小化 : {(IsVisible ? "可见" : "不可见")} / {(IsMinimized ? "是" : "否")}"
        };

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>根据窗口句柄生成 <see cref="WindowProbe"/>。</summary>
public static class WindowProbeFactory
{
    private const int TolerancePx = 2;

    /// <summary>由窗口句柄生成信息快照（会自动取顶层祖先窗口）；无效窗口返回 null。</summary>
    public static WindowProbe? FromHandle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return null;
        }

        var root = NativeMethods.GetAncestor(hWnd, NativeConstants.GA_ROOT);
        if (root == IntPtr.Zero)
        {
            root = hWnd;
        }

        var className = NativeMethods.GetClassNameSafe(root);
        var title = NativeMethods.GetWindowTitleSafe(root);
        NativeMethods.GetWindowThreadProcessId(root, out var pid);

        var hasRect = NativeMethods.GetWindowRect(root, out var rect);
        var bounds = hasRect ? rect.ToRectangle() : System.Drawing.Rectangle.Empty;
        var monitorBounds = NativeMethods.GetMonitorBounds(root);

        var isVisible = NativeMethods.IsWindowVisible(root);
        var isMinimized = NativeMethods.IsIconic(root);
        var hasCaption = NativeMethods.HasCaption(root);
        var isFullScreen = !isMinimized
            && (IsCoveringTarget(bounds, monitorBounds) || IsCoveringTarget(bounds, NativeMethods.GetVirtualScreenBounds()));

        return new WindowProbe(
            root,
            className,
            title,
            pid,
            ProcessNameCache.GetProcessName(pid),
            bounds,
            monitorBounds,
            isFullScreen,
            hasCaption,
            isVisible,
            isMinimized);
    }

    /// <summary>窗口矩形是否完整覆盖目标矩形（允许 2 像素误差，也允许超出，例如跨屏放映）。</summary>
    public static bool IsCoveringTarget(System.Drawing.Rectangle window, System.Drawing.Rectangle target)
    {
        if (target.Width <= 0 || target.Height <= 0 || window.Width <= 0 || window.Height <= 0)
        {
            return false;
        }

        return window.Left <= target.Left + TolerancePx
            && window.Top <= target.Top + TolerancePx
            && window.Right >= target.Right - TolerancePx
            && window.Bottom >= target.Bottom - TolerancePx;
    }
}
