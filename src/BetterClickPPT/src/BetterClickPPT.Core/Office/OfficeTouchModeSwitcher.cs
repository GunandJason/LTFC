using System.Windows.Automation;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Office;

/// <summary>
/// Office（PowerPoint / WPS 演示）「触摸 / 鼠标模式」切换器：用 UI Automation 找到快速访问工具栏上的
/// 「触摸/鼠标模式」下拉按钮（NetUIAnchor），展开后在图库（NetUIToolWindow）里选择「鼠标 / 触摸」。
/// </summary>
/// <remarks>
/// 实测要点（PowerPoint 16 / Microsoft 365 中文版）：
/// <list type="bullet">
///   <item>只有 InvokePattern.Invoke() 会真正提交模式；SelectionItemPattern.Select() 只高亮、不生效。</item>
///   <item>全程不注入键鼠消息、不激活窗口，因此放映（全屏）过程中也能安全切换，不会打断放映、不会让放映窗口失去响应。</item>
///   <item>UIA 需要跨进程通信，单次调用可能耗时数百毫秒，所以只能由后台线程调用（本类不切换线程，由调用方负责）。</item>
/// </list>
/// 注意：本类使用托管 UIA 客户端（<c>System.Windows.Automation</c>），
/// 因此 BetterClickPPT.Core 需要 <c>&lt;UseWPF&gt;true&lt;/UseWPF&gt;</c>。
/// </remarks>
public sealed class OfficeTouchModeSwitcher : ITouchModeSwitcher
{
    /// <summary>「触摸/鼠标模式」下拉按钮的类名（Office 快速访问工具栏）。</summary>
    public const string ToggleClassName = "NetUIAnchor";

    /// <summary>展开后的下拉面板（图库）类名。</summary>
    public const string GalleryClassName = "NetUIToolWindow";

    /// <summary>下拉面板里每一项的类名。</summary>
    public const string GalleryItemClassName = "NetUIGalleryButton";

    /// <summary>PowerPoint 主窗口类名。</summary>
    public const string PowerPointFrameClassName = "PPTFrameClass";

    /// <summary>
    /// 按钮名称。中文版 Office 是「触摸/鼠标模式」，英文版是「Touch/Mouse Mode」；
    /// 两种都试一遍，找不到就认为该 Office 版本不支持本功能（不会报错，只写日志）。
    /// </summary>
    private static readonly string[] ToggleNames = { "触摸/鼠标模式", "Touch/Mouse Mode" };

    /// <summary>「鼠标」项名称。</summary>
    private static readonly string[] MouseItemNames = { "鼠标", "Mouse" };

    /// <summary>「触摸」项名称。</summary>
    private static readonly string[] TouchItemNames = { "触摸", "Touch" };

    /// <summary>Office 系主窗口的类名（PowerPoint 已实测；WPS 演示的类名未验证，列为候选）。</summary>
    private static readonly string[] FrameClassNames = { PowerPointFrameClassName, "WPPFrameClass", "KWPPFrameClass" };

    private const int GalleryTimeoutMs = 2500;
    private const int PollIntervalMs = 100;
    private const int SettleAfterInvokeMs = 800;
    private const int CollapseWaitMs = 300;

    private readonly AppLog? _log;

    /// <summary>创建切换器（<paramref name="log"/> 可为 null，此时静默工作）。</summary>
    public OfficeTouchModeSwitcher(AppLog? log = null) => _log = log;

    /// <inheritdoc />
    public TouchModeProbeResult Probe(uint processId)
    {
        if (processId == 0)
        {
            return new TouchModeProbeResult(TouchModeState.Unknown, "未指定 Office 进程");
        }

        AutomationElement? toggle = null;
        var expandedByUs = false;
        try
        {
            var frame = FindFrame(processId);
            if (frame is null)
            {
                return new TouchModeProbeResult(TouchModeState.Unknown, "未找到 Office 主窗口（进程可能已退出或仍在启动）");
            }

            toggle = FindToggle(frame);
            if (toggle is null)
            {
                return new TouchModeProbeResult(TouchModeState.Unknown, ToggleMissingDetail);
            }

            expandedByUs = EnsureExpanded(toggle);
            var gallery = WaitForGallery(frame, GalleryTimeoutMs);
            if (gallery is null)
            {
                return new TouchModeProbeResult(TouchModeState.Unknown, "「触摸/鼠标模式」下拉面板没有打开");
            }

            var result = ReadSelection(gallery);
            return new TouchModeProbeResult(result.State, $"{result.Detail}；按钮宽 {WidthOf(toggle)}px");
        }
        catch (Exception ex)
        {
            Report($"读取 Office 触摸模式失败（pid={processId}）：{ex.Message}");
            return new TouchModeProbeResult(TouchModeState.Unknown, "读取失败：" + ex.Message);
        }
        finally
        {
            if (expandedByUs && toggle is not null)
            {
                Collapse(toggle);
            }
        }
    }

    /// <inheritdoc />
    public TouchModeSwitchResult Switch(uint processId, TouchModeState target)
    {
        if (!TouchModes.IsSwitchable(target))
        {
            return new TouchModeSwitchResult(target, TouchModeState.Unknown, TouchModeState.Unknown, false, "目标模式无效");
        }

        if (processId == 0)
        {
            return new TouchModeSwitchResult(target, TouchModeState.Unknown, TouchModeState.Unknown, false, "未指定 Office 进程");
        }

        AutomationElement? toggle = null;
        var expandedByUs = false;
        try
        {
            var frame = FindFrame(processId);
            if (frame is null)
            {
                return new TouchModeSwitchResult(target, TouchModeState.Unknown, TouchModeState.Unknown, false, "未找到 Office 主窗口（进程可能已退出或仍在启动）");
            }

            toggle = FindToggle(frame);
            if (toggle is null)
            {
                return new TouchModeSwitchResult(target, TouchModeState.Unknown, TouchModeState.Unknown, false, ToggleMissingDetail);
            }

            expandedByUs = EnsureExpanded(toggle);
            var gallery = WaitForGallery(frame, GalleryTimeoutMs);
            if (gallery is null)
            {
                return new TouchModeSwitchResult(target, TouchModeState.Unknown, TouchModeState.Unknown, false, "「触摸/鼠标模式」下拉面板没有打开");
            }

            var before = ReadSelection(gallery).State;
            if (before == target)
            {
                return new TouchModeSwitchResult(target, before, before, true, "无需切换");
            }

            var item = FindItem(gallery, ItemNames(target));
            if (item is null)
            {
                return new TouchModeSwitchResult(target, before, TouchModeState.Unknown, false, $"下拉面板里没有「{TouchModes.ToDisplayName(target)}」项");
            }

            InvokeItem(item);
            Thread.Sleep(SettleAfterInvokeMs);

            // 切换后下拉面板可能被 Office 重建或收起：重新展开一次再确认最终状态。
            var after = TouchModeState.Unknown;
            for (var attempt = 0; attempt < 3 && after == TouchModeState.Unknown; attempt++)
            {
                var current = FindGallery(frame) ?? (EnsureExpanded(toggle) ? WaitForGallery(frame, GalleryTimeoutMs) : null);
                if (current is not null)
                {
                    after = ReadSelection(current).State;
                }

                if (after == TouchModeState.Unknown)
                {
                    Thread.Sleep(300);
                }
            }

            var success = after == target;
            return new TouchModeSwitchResult(
                target,
                before,
                after,
                success,
                success ? "已切换" : $"切换后未能确认状态（读到 {TouchModes.ToDisplayName(after)}）");
        }
        catch (Exception ex)
        {
            Report($"切换 Office 触摸模式失败（pid={processId}）：{ex.Message}");
            return new TouchModeSwitchResult(target, TouchModeState.Unknown, TouchModeState.Unknown, false, "切换失败：" + ex.Message);
        }
        finally
        {
            if (expandedByUs && toggle is not null)
            {
                Collapse(toggle);
            }
        }
    }

    /// <summary>
    /// 只读检查：该进程是否存在「触摸/鼠标模式」下拉按钮（不会展开面板、不改变 Office 界面）。
    /// 用于「检测当前前台窗口」诊断输出，让用户一眼看出该 Office 是否支持自动切换。
    /// </summary>
    public static bool IsTogglePresent(uint processId, out string detail)
    {
        if (processId == 0)
        {
            detail = "未指定 Office 进程";
            return false;
        }

        try
        {
            var frame = FindFrame(processId);
            if (frame is null)
            {
                detail = "未找到 Office 主窗口";
                return false;
            }

            var toggle = FindToggle(frame);
            if (toggle is null)
            {
                detail = ToggleMissingDetail;
                return false;
            }

            detail = $"已找到「触摸/鼠标模式」按钮（按钮宽 {WidthOf(toggle)}px）";
            return true;
        }
        catch (Exception ex)
        {
            detail = "检测失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 由「鼠标 / 触摸」两项的选中状态推断当前模式。
    /// 两项都标记为选中时按「触摸模式」处理：多切一次「鼠标」没有副作用，漏切才会让放映界面变大。
    /// </summary>
    public static TouchModeState ClassifySelection(bool? mouseSelected, bool? touchSelected)
    {
        var mouse = mouseSelected ?? false;
        var touch = touchSelected ?? false;

        if (!mouse && !touch)
        {
            return TouchModeState.Unknown;
        }

        return touch ? TouchModeState.Touch : TouchModeState.Mouse;
    }

    /// <summary>找不到按钮时的一句话说明。</summary>
    private const string ToggleMissingDetail =
        "快速访问工具栏里没有「触摸/鼠标模式」按钮（可在快速访问工具栏下拉菜单里勾选它，或该 Office 版本不支持）";

    /// <summary>目标模式对应的图库项名称。</summary>
    private static string[] ItemNames(TouchModeState target)
        => target == TouchModeState.Touch ? TouchItemNames : MouseItemNames;

    // ---------- UI Automation 细节（下面这些方法都由后台线程调用） ----------

    /// <summary>找到 Office 主窗口（先按进程 + 类名；类名不认识时用“是否存在下拉按钮”兜底）。</summary>
    private static AutomationElement? FindFrame(uint processId)
    {
        var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, (int)processId);
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, condition);

        AutomationElement? fallback = null;
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            var className = ClassNameOf(window);
            if (FrameClassNames.Contains(className, StringComparer.OrdinalIgnoreCase))
            {
                return window;
            }

            // 兜底：类名不认识时，看它是不是“带下拉按钮的框架窗口”
            //（只对 Frame 类窗口做这一步，避免对无关窗口做代价很高的深度搜索）。
            if (fallback is null
                && className.Contains("Frame", StringComparison.OrdinalIgnoreCase)
                && FindToggle(window) is not null)
            {
                fallback = window;
            }
        }

        return fallback;
    }

    /// <summary>找到「触摸/鼠标模式」下拉按钮。</summary>
    private static AutomationElement? FindToggle(AutomationElement frame)
    {
        // 先让 UIA 自己过滤（快）；某些 Office 版本条件查询不返回结果，再用托管过滤兜底（已实测可用）。
        foreach (var name in ToggleNames)
        {
            try
            {
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ClassNameProperty, ToggleClassName),
                    new PropertyCondition(AutomationElement.NameProperty, name));

                var found = frame.FindFirst(TreeScope.Descendants, condition);
                if (found is not null)
                {
                    return found;
                }
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        foreach (AutomationElement element in frame.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (ClassNameOf(element) == ToggleClassName
                && ToggleNames.Contains(NameOf(element), StringComparer.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>找到展开后的下拉面板（图库）。</summary>
    private static AutomationElement? FindGallery(AutomationElement frame)
    {
        foreach (AutomationElement element in frame.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (ClassNameOf(element) == GalleryClassName
                && ToggleNames.Contains(NameOf(element), StringComparer.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>面板出现需要一点时间，轮询等待。</summary>
    private static AutomationElement? WaitForGallery(AutomationElement frame, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var gallery = FindGallery(frame);
            if (gallery is not null)
            {
                return gallery;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return null;
            }

            Thread.Sleep(PollIntervalMs);
        }
    }
    /// <summary>在图库（找不到图库时退化为在整个主窗口内）查找指定名称的项。</summary>
    private static AutomationElement? FindItem(AutomationElement frame, string[] names)
    {
        var scope = FindGallery(frame) ?? frame;
        foreach (AutomationElement element in scope.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (ClassNameOf(element) == GalleryItemClassName
                && names.Contains(NameOf(element), StringComparer.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>确保下拉面板已展开；返回 true 表示“是本次调用展开的”（调用方负责收起）。</summary>
    private static bool EnsureExpanded(AutomationElement toggle)
    {
        try
        {
            if (toggle.GetCurrentPattern(ExpandCollapsePattern.Pattern) is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            if (pattern.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
            {
                return false;
            }

            pattern.Expand();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>收起下拉面板（失败无所谓：用户在别处点一下也会收起）。</summary>
    private static void Collapse(AutomationElement toggle)
    {
        try
        {
            if (toggle.GetCurrentPattern(ExpandCollapsePattern.Pattern) is ExpandCollapsePattern pattern
                && pattern.Current.ExpandCollapseState != ExpandCollapseState.Collapsed)
            {
                pattern.Collapse();
                Thread.Sleep(CollapseWaitMs);
            }
        }
        catch (Exception)
        {
            // 忽略：面板状态由用户点击控制，收起失败不影响功能。
        }
    }

    /// <summary>
    /// 提交图库项：实测只有 InvokePattern.Invoke() 会真正切换模式；
    /// 老版本 Office 若只提供 SelectionItemPattern，才退化为 Select()。
    /// </summary>
    private static void InvokeItem(AutomationElement item)
    {
        if (item.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern invoke)
        {
            invoke.Invoke();
            return;
        }

        if (item.GetCurrentPattern(SelectionItemPattern.Pattern) is SelectionItemPattern selection)
        {
            selection.Select();
            return;
        }

        throw new InvalidOperationException("图库项既不支持 Invoke 也不支持 SelectionItem，无法切换模式");
    }

    /// <summary>读取「鼠标 / 触摸」的选中状态。</summary>
    private static TouchModeProbeResult ReadSelection(AutomationElement gallery)
    {
        var mouse = TryIsSelected(gallery, MouseItemNames);
        var touch = TryIsSelected(gallery, TouchItemNames);
        var state = ClassifySelection(mouse, touch);
        var mouseText = mouse is null ? "读不到" : (mouse.Value ? "选中" : "未选中");
        var touchText = touch is null ? "读不到" : (touch.Value ? "选中" : "未选中");

        return new TouchModeProbeResult(state, $"「鼠标」{mouseText}、「触摸」{touchText}");
    }

    private static bool? TryIsSelected(AutomationElement gallery, string[] names)
    {
        foreach (AutomationElement element in gallery.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (ClassNameOf(element) != GalleryItemClassName
                || !names.Contains(NameOf(element), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (element.GetCurrentPattern(SelectionItemPattern.Pattern) is SelectionItemPattern pattern)
                {
                    return pattern.Current.IsSelected;
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        return null;
    }

    private static string ClassNameOf(AutomationElement element)
    {
        try
        {
            return element.Current.ClassName ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string? NameOf(AutomationElement element)
    {
        try
        {
            return element.Current.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int WidthOf(AutomationElement element)
    {
        try
        {
            return (int)Math.Round(element.Current.BoundingRectangle.Width);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void Report(string message) => _log?.Warn(message);



}
