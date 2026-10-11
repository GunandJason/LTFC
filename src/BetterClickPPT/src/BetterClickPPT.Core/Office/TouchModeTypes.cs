namespace BetterClickPPT.Core.Office;

/// <summary>Office（PowerPoint / WPS 演示）界面的「触摸 / 鼠标模式」。</summary>
public enum TouchModeState
{
    /// <summary>无法确定（Office 未运行、界面语言不匹配，或快速访问工具栏里没有该按钮）。</summary>
    Unknown = 0,

    /// <summary>鼠标模式：界面元素为常规尺寸（本程序希望放映时处于这个状态）。</summary>
    Mouse = 1,

    /// <summary>触摸模式：按钮 / 间距变大，放映时会出现触摸工具栏。</summary>
    Touch = 2
}

/// <summary><see cref="TouchModeState"/> 的显示名与取值集合。</summary>
public static class TouchModes
{
    /// <summary>可以作为切换目标的模式（不含「未知」）。</summary>
    public static readonly TouchModeState[] Switchable = { TouchModeState.Mouse, TouchModeState.Touch };

    /// <summary>中文显示名。</summary>
    public static string ToDisplayName(TouchModeState state) => state switch
    {
        TouchModeState.Mouse => "鼠标模式",
        TouchModeState.Touch => "触摸模式",
        _ => "未知"
    };

    /// <summary>该模式能否作为切换目标。</summary>
    public static bool IsSwitchable(TouchModeState state)
        => state is TouchModeState.Mouse or TouchModeState.Touch;
}

/// <summary>读取「触摸 / 鼠标模式」的结果。</summary>
public sealed record TouchModeProbeResult(TouchModeState State, string Detail)
{
    /// <summary>是否成功读到了确定的状态。</summary>
    public bool IsKnown => State != TouchModeState.Unknown;

    /// <summary>当前是否鼠标模式。</summary>
    public bool IsMouse => State == TouchModeState.Mouse;

    /// <summary>当前是否触摸模式。</summary>
    public bool IsTouch => State == TouchModeState.Touch;

    /// <inheritdoc />
    public override string ToString() => $"{TouchModes.ToDisplayName(State)}（{Detail}）";
}

/// <summary>一次「触摸 / 鼠标模式」切换的结果。</summary>
public sealed record TouchModeSwitchResult(
    TouchModeState Target,
    TouchModeState Before,
    TouchModeState After,
    bool Success,
    string Detail)
{
    /// <summary>是否真的改变了模式（原本已经是目标模式时为 false）。</summary>
    public bool Changed => Success && Before != After;

    /// <inheritdoc />
    public override string ToString()
    {
        if (!Success)
        {
            return $"切换失败：{Detail}";
        }

        return Changed
            ? $"{TouchModes.ToDisplayName(Before)} → {TouchModes.ToDisplayName(After)}（{Detail}）"
            : $"已是{TouchModes.ToDisplayName(After)}（{Detail}）";
    }
}
