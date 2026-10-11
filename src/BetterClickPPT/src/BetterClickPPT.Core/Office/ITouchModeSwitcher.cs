namespace BetterClickPPT.Core.Office;

/// <summary>
/// Office「触摸 / 鼠标模式」开关的抽象。真实实现是 <see cref="OfficeTouchModeSwitcher"/>（UI Automation），
/// 自测里用假实现验证“放映开始时自动切回鼠标模式”的守护逻辑。
/// </summary>
public interface ITouchModeSwitcher
{
    /// <summary>
    /// 读取指定进程当前的模式。读取时可能会短暂展开「触摸/鼠标模式」下拉面板，随后自动收起。
    /// </summary>
    /// <param name="processId">Office 进程号。</param>
    TouchModeProbeResult Probe(uint processId);

    /// <summary>把指定进程切到 <paramref name="target"/> 模式（已经是目标模式时不做任何改动）。</summary>
    /// <param name="processId">Office 进程号。</param>
    /// <param name="target">目标模式（鼠标 / 触摸）。</param>
    TouchModeSwitchResult Switch(uint processId, TouchModeState target);
}
