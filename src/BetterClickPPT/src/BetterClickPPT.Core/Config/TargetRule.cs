namespace BetterClickPPT.Core.Config;

/// <summary>
/// 一个“放映目标”规则：什么样的窗口算是正在全屏放映的演示文稿窗口。
/// 内置 PowerPoint 与 WPS 演示两条规则，用户也可自行添加（自定义进程名）。
/// </summary>
public sealed class TargetRule
{
    public string Id { get; set; } = "custom";

    /// <summary>界面显示名称，例如 “Microsoft PowerPoint”。</summary>
    public string DisplayName { get; set; } = "自定义";

    /// <summary>是否启用该规则。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>进程名（不含路径），支持通配符，例如 POWERPNT.EXE、wpp*.exe。</summary>
    public List<string> ProcessNames { get; set; } = new();

    /// <summary>窗口类名（可选），支持通配符；留空表示不限制类名。</summary>
    public List<string> ClassNames { get; set; } = new();

    /// <summary>是否要求窗口必须铺满整个显示器。</summary>
    public bool RequireFullScreen { get; set; } = true;

    /// <summary>是否要求窗口无标题栏 / 边框（避免把“最大化的编辑窗口”误判为放映窗口）。</summary>
    public bool RequireBorderless { get; set; } = true;

    /// <summary>程序路径（仅用于界面显示与诊断，可为空）。</summary>
    public string? ExePath { get; set; }

    /// <summary>是否内置规则（内置规则不可删除，只能禁用）。</summary>
    public bool IsBuiltIn { get; set; }

    public TargetRule Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Enabled = Enabled,
        ProcessNames = new List<string>(ProcessNames),
        ClassNames = new List<string>(ClassNames),
        RequireFullScreen = RequireFullScreen,
        RequireBorderless = RequireBorderless,
        ExePath = ExePath,
        IsBuiltIn = IsBuiltIn
    };
}
