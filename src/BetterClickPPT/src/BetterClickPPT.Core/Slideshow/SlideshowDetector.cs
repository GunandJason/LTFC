using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Util;

namespace BetterClickPPT.Core.Slideshow;

/// <summary>匹配结果：命中的规则与被测窗口。</summary>
public sealed record WindowMatch(TargetRule Rule, WindowProbe Window)
{
    /// <summary>用于日志的简短描述。</summary>
    public string Describe() => $"{Rule.DisplayName} / {Window.ProcessName} / {Window.ClassName}";
}

/// <summary>
/// 放映窗口识别器：判断“当前点击或当前前台窗口”是否属于正在全屏放映的演示文稿。
/// </summary>
public sealed class SlideshowDetector
{
    private readonly Func<IReadOnlyList<TargetRule>> _rulesProvider;
    private readonly uint _selfProcessId;

    public SlideshowDetector(Func<IReadOnlyList<TargetRule>> rulesProvider, uint? selfProcessId = null)
    {
        _rulesProvider = rulesProvider ?? throw new ArgumentNullException(nameof(rulesProvider));
        _selfProcessId = selfProcessId ?? (uint)Environment.ProcessId;
    }

    /// <summary>规则是否命中窗口（纯函数，便于单元测试）。</summary>
    public static bool Matches(TargetRule rule, WindowProbe probe)
    {
        if (rule is null || probe is null || !rule.Enabled)
        {
            return false;
        }

        if (probe.ProcessName.Length == 0)
        {
            return false;
        }

        if (rule.ProcessNames.Count == 0 || !Wildcard.IsMatchAny(probe.ProcessName, rule.ProcessNames))
        {
            return false;
        }

        if (rule.ClassNames.Count > 0 && !Wildcard.IsMatchAny(probe.ClassName, rule.ClassNames))
        {
            return false;
        }

        if (rule.RequireFullScreen && !probe.IsFullScreen)
        {
            return false;
        }

        if (rule.RequireBorderless && probe.HasCaption)
        {
            return false;
        }

        if (probe.IsMinimized || !probe.IsVisible)
        {
            return false;
        }

        return true;
    }

    /// <summary>按坐标（屏幕坐标）检测：屏幕该点处的窗口是否为放映窗口。</summary>
    public WindowMatch? MatchPoint(System.Drawing.Point screenPoint)
    {
        var hWnd = NativeMethods.WindowFromPoint(new POINT(screenPoint.X, screenPoint.Y));
        return MatchHandle(hWnd);
    }

    /// <summary>检测前台窗口是否为放映窗口。</summary>
    public WindowMatch? MatchForeground() => MatchHandle(NativeMethods.GetForegroundWindow());

    /// <summary>检测指定窗口是否为放映窗口。</summary>
    public WindowMatch? MatchHandle(IntPtr hWnd)
    {
        var probe = WindowProbeFactory.FromHandle(hWnd);
        if (probe is null || probe.ProcessId == 0 || probe.ProcessId == _selfProcessId)
        {
            return null;
        }

        foreach (var rule in _rulesProvider())
        {
            if (Matches(rule, probe))
            {
                return new WindowMatch(rule, probe);
            }
        }

        return null;
    }

    /// <summary>
    /// 遍历所有顶层窗口，寻找任意一个正在全屏放映的窗口。
    /// 用于“触控原始输入”场景（无法得到触点坐标时，只要桌面上存在放映窗口即可翻页）。
    /// </summary>
    public WindowMatch? MatchAnyShowWindow()
    {
        WindowMatch? result = null;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            var probe = WindowProbeFactory.FromHandle(hWnd);
            if (probe is null || probe.ProcessId == 0 || probe.ProcessId == _selfProcessId)
            {
                return true;
            }

            foreach (var rule in _rulesProvider())
            {
                if (Matches(rule, probe))
                {
                    result = new WindowMatch(rule, probe);
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>
    /// 遍历所有顶层窗口，返回“属于规则关注进程”的窗口及其匹配结果（用于诊断）。
    /// </summary>
    public IReadOnlyList<(WindowProbe Probe, WindowMatch? Match)> EnumerateRuleWindows()
    {
        var results = new List<(WindowProbe, WindowMatch?)>();
        var rules = _rulesProvider();
        if (rules.Count == 0)
        {
            return results;
        }

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            var probe = WindowProbeFactory.FromHandle(hWnd);
            if (probe is null || probe.ProcessId == 0 || probe.ProcessId == _selfProcessId)
            {
                return true;
            }

            var relevant = rules.Any(rule => rule.ProcessNames.Count > 0
                                             && Util.Wildcard.IsMatchAny(probe.ProcessName, rule.ProcessNames));
            if (!relevant)
            {
                return true;
            }

            WindowMatch? match = null;
            foreach (var rule in rules)
            {
                if (Matches(rule, probe))
                {
                    match = new WindowMatch(rule, probe);
                    break;
                }
            }

            results.Add((probe, match));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>对当前前台窗口做完整诊断（包含“是否命中规则”）。</summary>
    public (WindowProbe? Probe, WindowMatch? Match) DiagnoseForeground()
        => Diagnose(NativeMethods.GetForegroundWindow());

    /// <summary>对指定窗口做完整诊断（包含“是否命中规则”）。</summary>
    public (WindowProbe? Probe, WindowMatch? Match) Diagnose(IntPtr hWnd)
    {
        var probe = WindowProbeFactory.FromHandle(hWnd);
        if (probe is null)
        {
            return (null, null);
        }

        foreach (var rule in _rulesProvider())
        {
            if (Matches(rule, probe))
            {
                return (probe, new WindowMatch(rule, probe));
            }
        }

        return (probe, null);
    }
}
