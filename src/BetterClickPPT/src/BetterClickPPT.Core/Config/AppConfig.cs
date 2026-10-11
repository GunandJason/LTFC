using BetterClickPPT.Core.Input;

namespace BetterClickPPT.Core.Config;

/// <summary>
/// 应用配置。保存于 %LOCALAPPDATA%\BetterClickPPT\config.json。
/// </summary>
public sealed class AppConfig
{
    /// <summary>当前配置结构版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>触摸 / 笔“提升”为鼠标消息时使用的默认特征值（MI_WP_SIGNATURE）。</summary>
    public const uint DefaultTouchSignatureValue = 0xFF515700;

    /// <summary>触摸特征值的默认掩码。</summary>
    public const uint DefaultTouchSignatureMask = 0xFFFFFF00;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>总开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>触摸 / 笔点击 → 下一页。</summary>
    public bool AdvanceOnTouch { get; set; } = true;

    /// <summary>鼠标左键点击 → 下一页（默认关闭：鼠标点击原本就能翻页）。</summary>
    public bool AdvanceOnMouse { get; set; }

    /// <summary>
    /// 触摸 / 笔轻触的处理方式：
    /// MouseClick（默认）= 还原成一次真实鼠标点击（动画、超链接、上一页按钮都正常）；
    /// NextKey = 发送下一页按键。
    /// </summary>
    public string TouchAction { get; set; } = TouchActions.MouseClick;

    /// <summary>吞掉触摸 / 笔的原始点击（防止“点一下翻两页”）。</summary>
    public bool SwallowTouchClick { get; set; } = true;

    /// <summary>鼠标点击被增强时吞掉原始点击（防止“点一下翻两页”）。</summary>
    public bool SwallowMouseClick { get; set; } = true;

    /// <summary>深度触控模式：使用原始输入（Raw Input）直接监听触摸屏 HID 设备。</summary>
    public bool RawTouchEnabled { get; set; } = true;

    /// <summary>
    /// 放映开始时自动把 Office 的「触摸模式」切回「鼠标模式」。
    /// 触摸屏电脑上 PowerPoint / WPS 进入放映时会切到触摸模式（按钮变大、出现触摸工具栏），容易误触；
    /// 开启后本程序会在检测到全屏放映时自动切回鼠标模式（不改动其它任何设置）。
    /// </summary>
    public bool AutoSwitchToMouseMode { get; set; } = true;

    /// <summary>忽略距屏幕边缘多少像素内的点击（0 表示不忽略）。</summary>
    public int EdgeMarginPx { get; set; }

    /// <summary>两次翻页的最小间隔（毫秒），用于防抖与去重。</summary>
    public int DebounceMs { get; set; } = 400;

    /// <summary>翻页按键。</summary>
    public string NextKey { get; set; } = NextKeyNames.PageDown;

    /// <summary>按键发送方式。</summary>
    public string KeySendMode { get; set; } = KeySendModes.Auto;

    /// <summary>轻触判定：按下到抬起的最大时长（毫秒）。</summary>
    public int TapMaxDurationMs { get; set; } = 400;

    /// <summary>轻触判定：最大移动量（占屏幕比例，0.02 = 2%）。</summary>
    public double TapMaxMovementRatio { get; set; } = 0.02;

    /// <summary>触摸特征值（一般无需修改；用于兼容特殊触摸屏驱动）。</summary>
    public uint TouchSignatureValue { get; set; } = DefaultTouchSignatureValue;

    /// <summary>触摸特征值掩码。</summary>
    public uint TouchSignatureMask { get; set; } = DefaultTouchSignatureMask;

    /// <summary>进入放映时显示气泡提示（退出放映不再弹窗）。</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>记录详细日志。</summary>
    public bool VerboseLog { get; set; }

    /// <summary>日志文件大小上限（KB）。</summary>
    public int LogMaxKb { get; set; } = 512;

    /// <summary>开机自动启动。</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>放映目标规则。</summary>
    public List<TargetRule> Targets { get; set; } = new();

    /// <summary>创建默认配置（同时包含 PowerPoint 与 WPS 演示两条内置规则）。</summary>
    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.Targets.AddRange(BuiltInTargets.CreateAll());
        return config;
    }

    /// <summary>补齐缺失的字段 / 内置规则，并把非法值收敛到合理范围。</summary>
    public AppConfig Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        Targets ??= new List<TargetRule>();

        foreach (var builtIn in BuiltInTargets.CreateAll())
        {
            var existing = Targets.FirstOrDefault(t => string.Equals(t.Id, builtIn.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Targets.Add(builtIn);
                continue;
            }

            // 内置规则必须保留：合并新增的进程名，但保留用户设置（启用状态、ExePath 等）。
            existing.IsBuiltIn = true;
            if (string.IsNullOrWhiteSpace(existing.DisplayName))
            {
                existing.DisplayName = builtIn.DisplayName;
            }

            foreach (var name in builtIn.ProcessNames)
            {
                if (!existing.ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    existing.ProcessNames.Add(name);
                }
            }
        }

        foreach (var target in Targets)
        {
            target.ProcessNames ??= new List<string>();
            target.ClassNames ??= new List<string>();
            if (string.IsNullOrWhiteSpace(target.Id))
            {
                target.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
            }

            if (string.IsNullOrWhiteSpace(target.DisplayName))
            {
                target.DisplayName = target.ProcessNames.FirstOrDefault() ?? target.Id;
            }
        }

        DebounceMs = Math.Clamp(DebounceMs, 0, 5000);
        EdgeMarginPx = Math.Clamp(EdgeMarginPx, 0, 2000);
        TapMaxDurationMs = Math.Clamp(TapMaxDurationMs, 50, 3000);
        TapMaxMovementRatio = Math.Clamp(TapMaxMovementRatio, 0.001, 0.5);
        LogMaxKb = Math.Clamp(LogMaxKb, 64, 16384);

        if (!NextKeyNames.All.Contains(NextKey))
        {
            NextKey = NextKeyNames.PageDown;
        }

        if (!KeySendModes.All.Contains(KeySendMode))
        {
            KeySendMode = KeySendModes.Auto;
        }

        if (!TouchActions.All.Contains(TouchAction))
        {
            TouchAction = TouchActions.MouseClick;
        }

        if (TouchSignatureMask == 0)
        {
            TouchSignatureMask = DefaultTouchSignatureMask;
            TouchSignatureValue = DefaultTouchSignatureValue;
        }

        return this;
    }

    /// <summary>深拷贝。</summary>
    public AppConfig Clone()
    {
        return new AppConfig
        {
            SchemaVersion = SchemaVersion,
            Enabled = Enabled,
            AdvanceOnTouch = AdvanceOnTouch,
            AdvanceOnMouse = AdvanceOnMouse,
            TouchAction = TouchAction,
            SwallowTouchClick = SwallowTouchClick,
            SwallowMouseClick = SwallowMouseClick,
            RawTouchEnabled = RawTouchEnabled,
            AutoSwitchToMouseMode = AutoSwitchToMouseMode,
            EdgeMarginPx = EdgeMarginPx,
            DebounceMs = DebounceMs,
            NextKey = NextKey,
            KeySendMode = KeySendMode,
            TapMaxDurationMs = TapMaxDurationMs,
            TapMaxMovementRatio = TapMaxMovementRatio,
            TouchSignatureValue = TouchSignatureValue,
            TouchSignatureMask = TouchSignatureMask,
            ShowNotifications = ShowNotifications,
            VerboseLog = VerboseLog,
            LogMaxKb = LogMaxKb,
            AutoStart = AutoStart,
            Targets = Targets.Select(t => t.Clone()).ToList()
        };
    }
}
