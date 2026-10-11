namespace BetterClickPPT.Core.Config;

using BetterClickPPT.Core.Util;

/// <summary>
/// 内置放映目标规则定义（PowerPoint / WPS 演示）。
/// 独立成静态类，便于安装向导、自测程序复用同一份定义。
/// </summary>
public static class BuiltInTargets
{
    /// <summary>Microsoft PowerPoint 规则。</summary>
    public static TargetRule CreatePowerPoint() => new()
    {
        Id = "powerpoint",
        DisplayName = "Microsoft PowerPoint",
        Enabled = true,
        IsBuiltIn = true,
        ProcessNames = new List<string> { "POWERPNT.EXE" },
        // PowerPoint 全屏放映窗口的类名为 screenClass（已实测）。
        // 同时保留“全屏 + 无边框”判定，即使个别版本类名不同也能正确识别，
        // 而“最大化的编辑窗口”带标题栏，不会被误判。
        ClassNames = new List<string> { "screenClass" },
        RequireFullScreen = true,
        RequireBorderless = true
    };

    /// <summary>WPS 演示规则。</summary>
    public static TargetRule CreateWps() => new()
    {
        Id = "wps",
        DisplayName = "WPS 演示",
        Enabled = true,
        IsBuiltIn = true,
        // WPS 演示主程序为 wpp.exe，部分版本使用 kwpp.exe / wpspresent.exe；
        // 不限制窗口类名，只要求“全屏 + 无边框”，以兼容各版本 WPS。
        ProcessNames = new List<string> { "wpp.exe", "wpp64.exe", "kwpp.exe", "wpspresent.exe", "wps.exe" },
        ClassNames = new List<string>(),
        RequireFullScreen = true,
        RequireBorderless = true
    };

    /// <summary>全部内置规则（每次调用返回新实例）。</summary>
    public static IEnumerable<TargetRule> CreateAll()
    {
        yield return CreatePowerPoint();
        yield return CreateWps();
    }

    /// <summary>内置规则 Id（Office 演示软件）。</summary>
    private static readonly string[] OfficeRuleIds = { "powerpoint", "wps" };

    /// <summary>Office 演示软件的进程名（含 WPS 各版本）。</summary>
    private static readonly string[] OfficeProcessNames =
    {
        "POWERPNT.EXE", "wpp.exe", "wpp64.exe", "kwpp.exe", "wpspresent.exe", "wps.exe"
    };

    /// <summary>
    /// 该放映窗口是否属于 Office 演示软件（PowerPoint / WPS 演示）。
    /// 只有它们才有「触摸/鼠标模式」按钮；其他放映软件（用户自定义的目标）不需要做这个切换，
    /// 否则每次放映都会白试几次并留下“未能切换”的日志。
    /// </summary>
    public static bool IsOfficeShow(TargetRule? rule, string? processName)
    {
        if (rule is not null && OfficeRuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return Wildcard.IsMatchAny(processName, OfficeProcessNames);
    }
}
