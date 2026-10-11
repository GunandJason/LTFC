namespace BetterClickPPT.Core.Input;

/// <summary>输入来源。</summary>
public enum InputSource
{
    /// <summary>无法判断。</summary>
    Unknown = 0,

    /// <summary>真实鼠标（含触摸板）。</summary>
    Mouse = 1,

    /// <summary>触摸屏手指或手写笔（系统由触摸/笔“提升”而来的鼠标消息）。</summary>
    TouchOrPen = 2,

    /// <summary>由程序注入的鼠标事件。</summary>
    Injected = 3
}

/// <summary>翻页按键名称常量。</summary>
public static class NextKeyNames
{
    public const string PageDown = "PageDown";
    public const string Right = "Right";
    public const string Down = "Down";
    public const string Space = "Space";
    public const string Enter = "Enter";

    public static IReadOnlyList<string> All { get; } = new[] { PageDown, Right, Down, Space, Enter };

    /// <summary>转换为中文说明，供界面显示。</summary>
    public static string ToDisplayName(string name) => name switch
    {
        PageDown => "PageDown（下一页）",
        Right => "→ 右方向键",
        Down => "↓ 下方向键",
        Space => "空格键",
        Enter => "回车键",
        _ => name
    };
}

/// <summary>
/// 触摸 / 笔“轻触”的处理方式。
/// </summary>
public static class TouchActions
{
    /// <summary>
    /// 还原成一次真实鼠标点击（默认，推荐）：
    /// 放映时的分步动画、超链接、屏幕上的“上一页 / 下一页”按钮等原有点击行为全部保留，
    /// 同时触摸模式下被忽略的“点一下”也会正常翻页——相当于把手指换成了鼠标左键。
    /// </summary>
    public const string MouseClick = "MouseClick";

    /// <summary>发送“下一页”按键（PageDown）：只翻页，不触发页内动画与超链接。</summary>
    public const string NextKey = "NextKey";

    public static IReadOnlyList<string> All { get; } = new[] { MouseClick, NextKey };

    /// <summary>下拉框显示文本。</summary>
    public static string ToDisplayName(string name) => name switch
    {
        MouseClick => "模拟鼠标点击（推荐：动画 / 超链接 / 上一页按钮都能用）",
        NextKey => "发送下一页按键（只翻页，不触发动画与超链接）",
        _ => name
    };

    /// <summary>日志用的短名称。</summary>
    public static string ToShortName(string name) => name switch
    {
        MouseClick => "鼠标点击",
        NextKey => "下一页按键",
        _ => name
    };
}

/// <summary>按键发送方式。</summary>
public static class KeySendModes
{
    /// <summary>自动：放映窗口在前台时用 SendInput，否则向放映窗口 PostMessage。</summary>
    public const string Auto = "Auto";

    /// <summary>SendInput：模拟真实按键（发送给当前前台窗口）。</summary>
    public const string SendInput = "SendInput";

    /// <summary>PostMessage：直接向放映窗口投递按键消息。</summary>
    public const string PostMessage = "PostMessage";

    public static IReadOnlyList<string> All { get; } = new[] { Auto, SendInput, PostMessage };

    public static string ToDisplayName(string name) => name switch
    {
        Auto => "自动（推荐）",
        SendInput => "模拟按键（SendInput）",
        PostMessage => "窗口消息（PostMessage）",
        _ => name
    };
}

/// <summary>鼠标钩子捕获到的按键事件。</summary>
public readonly record struct MouseHookEvent(
    System.Drawing.Point ScreenPoint,
    InputSource Source,
    uint ExtraInfo,
    bool Injected,
    uint MessageTime)
{
    /// <summary>是否为触摸 / 笔引发的点击。</summary>
    public bool IsTouchLike => Source == InputSource.TouchOrPen;

    /// <summary>是否为真实鼠标点击（非触摸、非笔、非注入）。</summary>
    public bool IsRealMouse => Source == InputSource.Mouse;
}

/// <summary>原始输入（HID 数字化仪）识别出的一次“轻触”。</summary>
public readonly record struct TouchTapEvent(
    long DurationMs,
    int MaxContacts,
    double MovementRatio,
    string DeviceName)
{
    /// <summary>接触点归一化 X 坐标（0~1，未知为 null）。</summary>
    public double? NormalizedX { get; init; }

    /// <summary>接触点归一化 Y 坐标（0~1，未知为 null）。</summary>
    public double? NormalizedY { get; init; }
}
