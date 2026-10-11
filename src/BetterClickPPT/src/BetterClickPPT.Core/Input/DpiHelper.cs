using System.Runtime.InteropServices;

namespace BetterClickPPT.Core.Input;

/// <summary>
/// DPI 辅助：部分环境下“低层鼠标钩子的坐标是物理像素”，而窗口 API 返回的却是逻辑像素
/// （进程 DPI 感知级别不同）。这里提供缩放比例并做双向换算，保证坐标一定能对上。
/// </summary>
public static class DpiHelper
{
    private static readonly object Sync = new();
    private static double _cachedScale = 1.0;
    private static long _cachedAt = long.MinValue / 4;

    /// <summary>系统 DPI 缩放比例（96 DPI = 1.0），5 秒缓存。</summary>
    public static double GetScale()
    {
        lock (Sync)
        {
            if (Environment.TickCount64 - _cachedAt < 5000)
            {
                return _cachedScale;
            }

            _cachedAt = Environment.TickCount64;
            _cachedScale = 1.0;

            try
            {
                var dpi = GetDpiForSystem();
                if (dpi > 0)
                {
                    _cachedScale = dpi / 96.0;
                }
            }
            catch
            {
                _cachedScale = 1.0;
            }

            return _cachedScale;
        }
    }

    /// <summary>返回“原坐标 + 按 DPI 缩放换算后的坐标”（去重，最多 3 个）。</summary>
    public static IEnumerable<System.Drawing.Point> ExpandCandidates(System.Drawing.Point point)
    {
        yield return point;

        var scale = GetScale();
        if (Math.Abs(scale - 1.0) < 0.001)
        {
            yield break;
        }

        var divided = new System.Drawing.Point((int)Math.Round(point.X / scale), (int)Math.Round(point.Y / scale));
        if (divided != point)
        {
            yield return divided;
        }

        var multiplied = new System.Drawing.Point((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale));
        if (multiplied != point)
        {
            yield return multiplied;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
