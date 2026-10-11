using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Input;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Logging;
using BetterClickPPT.Core.Office;
using BetterClickPPT.Core.Slideshow;

namespace BetterClickPPT.Core.Engine;

/// <summary>增强器产生的一条活动记录（界面 / 诊断用）。</summary>
public sealed record EnhanceActivity(DateTime Timestamp, string Message, bool IsAdvance)
{
    /// <inheritdoc />
    public override string ToString() => $"{Timestamp:HH:mm:ss} {Message}";
}

/// <summary>放映状态变化事件参数。</summary>
public sealed class ShowStateChangedEventArgs : EventArgs
{
    public ShowStateChangedEventArgs(bool isShowing, string description)
    {
        IsShowing = isShowing;
        Description = description;
    }

    /// <summary>当前是否处于全屏放映状态。</summary>
    public bool IsShowing { get; }

    /// <summary>放映窗口描述。</summary>
    public string Description { get; }
}

/// <summary>
/// 核心增强器：识别放映窗口，并把“触摸 / 笔点击”转换成“下一页”。
/// 注意：Start / Stop 必须在拥有消息循环的线程（通常是界面线程）上调用。
/// </summary>
public sealed class SlideshowEnhancer : IDisposable
{
    private const int MaxActivities = 200;

    private readonly AppLog _log;
    private readonly KeySender _keySender;
    private readonly MouseClickSender _clickSender;
    private readonly HiddenMessageWindow _actionWindow;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly RawTouchTapSource _rawTouch;
    private readonly TouchModeGuard _touchModeGuard;
    private readonly Queue<EnhanceActivity> _activities = new();
    private readonly object _activitySync = new();

    private volatile AppConfig _config;
    private volatile TargetRule[] _rules;
    private long _lastAdvanceTicks = long.MinValue / 4;
    private volatile string? _lastShowKey;
    private System.Threading.Timer? _stateTimer;
    private bool _disposed;

    public SlideshowEnhancer(AppConfig config, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _config = config.Clone().Normalize();
        _rules = _config.Targets.Where(t => t.Enabled).Select(t => t.Clone()).ToArray();

        _keySender = new KeySender(log);
        _clickSender = new MouseClickSender(log);
        _actionWindow = new HiddenMessageWindow("BetterClickPPTActionWindow", log);
        _mouseHook = new LowLevelMouseHook();
        _mouseHook.LeftButtonDown += OnLeftButtonDown;

        _rawTouch = new RawTouchTapSource(config.TapMaxDurationMs, config.TapMaxMovementRatio, log);
        _rawTouch.Tap += OnRawTouchTap;

        _touchModeGuard = new TouchModeGuard(log) { Enabled = IsTouchModeGuardEnabled(_config) };
        _touchModeGuard.Completed += OnTouchModeGuardCompleted;

        Detector = new SlideshowDetector(() => _rules);
    }

    /// <summary>产生活动记录（界面实时显示）。</summary>
    public event EventHandler<EnhanceActivity>? Activity;

    /// <summary>放映状态变化（用于托盘气泡提示）。</summary>
    public event EventHandler<ShowStateChangedEventArgs>? ShowStateChanged;

    /// <summary>窗口识别器（诊断用）。</summary>
    public SlideshowDetector Detector { get; }

    /// <summary>「放映时自动切回鼠标模式」守护（设置界面 / 托盘提示订阅其结果）。</summary>
    public TouchModeGuard TouchMode => _touchModeGuard;

    /// <summary>是否已启动。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>全局鼠标钩子是否安装成功。</summary>
    public bool IsMouseHookActive => _mouseHook.IsHooked;

    /// <summary>深度触控（原始输入）监听是否已启动。</summary>
    public bool IsRawTouchActive => _rawTouch.IsRunning;

    /// <summary>累计翻页次数。</summary>
    public int AdvanceCount { get; private set; }

    /// <summary>其中“把触摸还原成鼠标点击”的次数。</summary>
    public int ClickReplayCount { get; private set; }

    /// <summary>累计捕获的触摸 / 笔点击次数。</summary>
    public int TouchClickCount { get; private set; }

    /// <summary>累计鼠标钩子回调次数。</summary>
    public int MouseEventCount { get; private set; }

    /// <summary>最近的活动记录（最新在后）。</summary>
    public IReadOnlyList<EnhanceActivity> RecentActivities
    {
        get
        {
            lock (_activitySync)
            {
                return _activities.ToArray();
            }
        }
    }

    /// <summary>启动增强器。</summary>
    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        var config = _config;
        _log.Info($"启动增强器：触摸翻页={config.AdvanceOnTouch}（{TouchActions.ToShortName(config.TouchAction)}）, 鼠标翻页={config.AdvanceOnMouse}, 深度触控={config.RawTouchEnabled}, 自动切回鼠标模式={IsTouchModeGuardEnabled(config)}, 按键={config.NextKey}, 发送方式={config.KeySendMode}");

        // 先创建“动作窗口”：钩子回调只做决策，真正的注入/按键动作都通过它投递到消息循环执行，
        // 避免在钩子回调里调用 SendInput 造成自我死锁。
        if (!_actionWindow.Create())
        {
            _log.Warn("创建动作窗口失败：动作将改为在钩子回调内直接执行（存在阻塞风险）");
        }

        if (config.AdvanceOnTouch || config.AdvanceOnMouse)
        {
            if (!_mouseHook.Start())
            {
                _log.Warn($"安装鼠标钩子失败（错误码 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）");
            }
        }

        ApplyRawTouchState(config);

        _stateTimer = new System.Threading.Timer(_ => SafePollShowState(), null, 1000, 1500);
        IsRunning = true;
        Report("增强器已启动", false);
        return true;
    }

    /// <summary>停止增强器。</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _stateTimer?.Dispose();
        _stateTimer = null;
        _mouseHook.Stop();
        _rawTouch.Stop();
        _touchModeGuard.NotifyShowEnded();
        _actionWindow.Dispose();
        IsRunning = false;
        _lastShowKey = null;
        Report("增强器已停止", false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _mouseHook.LeftButtonDown -= OnLeftButtonDown;
        _rawTouch.Tap -= OnRawTouchTap;
        _touchModeGuard.Completed -= OnTouchModeGuardCompleted;
        _mouseHook.Dispose();
        _rawTouch.Dispose();
        _touchModeGuard.Dispose();
        _actionWindow.Dispose();
    }

    /// <summary>应用新的配置（设置界面保存后调用）。</summary>
    public void ApplyConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var snapshot = config.Clone().Normalize();
        _rules = snapshot.Targets.Where(t => t.Enabled).Select(t => t.Clone()).ToArray();
        _config = snapshot;
        _log.Verbose = snapshot.VerboseLog;

        if (IsRunning)
        {
            if (snapshot.AdvanceOnTouch || snapshot.AdvanceOnMouse)
            {
                _mouseHook.Start();
            }
            else
            {
                _mouseHook.Stop();
            }

            ApplyRawTouchState(snapshot);
        }

        _touchModeGuard.Target = TouchModeState.Mouse;
        _touchModeGuard.Enabled = IsTouchModeGuardEnabled(snapshot);

        Report($"配置已更新：触摸翻页={snapshot.AdvanceOnTouch}, 鼠标翻页={snapshot.AdvanceOnMouse}, 深度触控={snapshot.RawTouchEnabled}, 自动切回鼠标模式={_touchModeGuard.Enabled}", false);
    }

    /// <summary>鼠标左键按下（低层钩子回调）。返回 true 表示吞掉该点击。</summary>
    private bool OnLeftButtonDown(MouseHookEvent hookEvent)
    {
        var config = _config;
        MouseEventCount++;

        if (!config.Enabled)
        {
            return false;
        }

        var isTouchLike = hookEvent.Source == InputSource.TouchOrPen;
        if (hookEvent.Source == InputSource.Injected)
        {
            _log.Trace($"忽略注入的鼠标事件（source=Injected, extra=0x{hookEvent.ExtraInfo:X8}, 位置 {hookEvent.ScreenPoint.X},{hookEvent.ScreenPoint.Y}）");
            return false;
        }

        if (isTouchLike && !config.AdvanceOnTouch)
        {
            return false;
        }

        if (!isTouchLike && !config.AdvanceOnMouse)
        {
            _log.Trace("忽略真实鼠标点击（未开启鼠标增强）");
            return false;
        }

        var match = MatchPointWithDpiFallback(hookEvent.ScreenPoint, out var effectivePoint);
        if (match is null)
        {
            _log.Trace($"点击位置 {hookEvent.ScreenPoint.X},{hookEvent.ScreenPoint.Y} 不是放映窗口，放行（source={hookEvent.Source}）");
            return false;
        }

        if (config.EdgeMarginPx > 0 && IsNearEdge(effectivePoint, config.EdgeMarginPx, match))
        {
            Report($"忽略放映窗口边缘 {config.EdgeMarginPx}px 内的点击", false);
            return false;
        }

        if (isTouchLike)
        {
            TouchClickCount++;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastAdvanceTicks) >= config.DebounceMs)
        {
            // 注意：这里仍在钩子回调内，只做决策并把动作投递出去（绝不能在钩子回调里 SendInput）。
            Interlocked.Exchange(ref _lastAdvanceTicks, now);
            var point = effectivePoint;
            var isTouch = isTouchLike;
            var reason = isTouchLike ? "触摸点击" : "鼠标点击";
            _actionWindow.Post(() => PerformAction(reason, match, point, isTouch));
        }
        else
        {
            Report($"忽略重复{(isTouchLike ? "触摸" : "鼠标")}点击（防抖 {config.DebounceMs}ms）", false);
        }

        // 吞掉原始点击：保证“点一下只触发一次”，避免与程序自身的点击行为重复。
        return isTouchLike ? config.SwallowTouchClick : config.SwallowMouseClick;
    }

    /// <summary>原始输入识别到的一次轻触（触摸屏自行处理触摸、系统未提升为鼠标消息的场景）。</summary>
    private void OnRawTouchTap(object? sender, TouchTapEvent touchTap)
    {
        var config = _config;
        if (!config.Enabled || !config.AdvanceOnTouch)
        {
            return;
        }

        if (Environment.TickCount64 - Interlocked.Read(ref _lastAdvanceTicks) < config.DebounceMs)
        {
            _log.Trace("原始输入轻触与已处理的点击重复，忽略");
            return;
        }

        var match = Detector.MatchForeground() ?? Detector.MatchAnyShowWindow();
        if (match is null)
        {
            return;
        }

        TouchClickCount++;

        var point = ResolveTapPoint(match, touchTap);
        var reason = $"触屏轻触（原始输入，{touchTap.DurationMs}ms/{touchTap.MaxContacts}指）";
        Interlocked.Exchange(ref _lastAdvanceTicks, Environment.TickCount64);

        // 与鼠标钩子通道一致：动作统一在消息循环上执行。
        _actionWindow.Post(() => PerformAction(reason, match, point, isTouch: true));
    }

    /// <summary>
    /// 把原始输入触摸的归一化坐标换算成屏幕坐标（按放映窗口所在显示器换算；
    /// 拿不到坐标时退化为窗口中心）。
    /// </summary>
    private static System.Drawing.Point ResolveTapPoint(WindowMatch match, TouchTapEvent touchTap)
    {
        var monitor = match.Window.MonitorBounds;
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            monitor = match.Window.Bounds;
        }

        if (touchTap.NormalizedX is { } nx && touchTap.NormalizedY is { } ny
            && monitor.Width > 0 && monitor.Height > 0)
        {
            var x = monitor.Left + (int)Math.Round(Math.Clamp(nx, 0d, 1d) * (monitor.Width - 1));
            var y = monitor.Top + (int)Math.Round(Math.Clamp(ny, 0d, 1d) * (monitor.Height - 1));
            return new System.Drawing.Point(x, y);
        }

        return new System.Drawing.Point(
            monitor.Left + monitor.Width / 2,
            monitor.Top + monitor.Height / 2);
    }

    /// <summary>
    /// 执行“轻触应做的动作”：
    /// 默认（MouseClick）把轻触还原成一次真实鼠标点击——动画、超链接、屏幕上的上一页按钮都保持可用；
    /// 也可切换为直接发送下一页按键。
    /// </summary>
    private bool PerformAction(string reason, WindowMatch match, System.Drawing.Point point, bool isTouch)
    {
        var config = _config;
        var useMouseClick = isTouch
            && string.Equals(config.TouchAction, TouchActions.MouseClick, StringComparison.OrdinalIgnoreCase);

        if (useMouseClick)
        {
            var ok = _clickSender.ClickAt(point, out var clickDetail);
            if (ok)
            {
                ClickReplayCount++;
                AdvanceCount++;
                Report($"{reason} → 还原为鼠标点击（{clickDetail}），动画 / 超链接 / 上一页按钮均正常（{match.Describe()}）", true);
            }
            else
            {
                Report($"{reason} → 模拟鼠标点击失败：{clickDetail}", false);
            }

            return ok;
        }

        return TrySendNextKey(reason, match);
    }

    private bool TrySendNextKey(string reason, WindowMatch match)
    {
        var config = _config;
        var ok = _keySender.SendNext(match.Window.Handle, config.NextKey, config.KeySendMode, out var detail);

        if (ok)
        {
            AdvanceCount++;
            Report($"{reason} → 发送下一页按键（{detail}）（{match.Describe()}）", true);
        }
        else
        {
            Report($"{reason} → 翻页失败（{detail}）（{match.Describe()}）", false);
        }

        return ok;
    }

    private void ApplyRawTouchState(AppConfig config)
    {
        if (config.RawTouchEnabled)
        {
            _rawTouch.Start();
        }
        else
        {
            _rawTouch.Stop();
        }
    }

    /// <summary>
    /// 按坐标匹配放映窗口；如果直接匹配失败，会再用 DPI 缩放换算后的坐标重试
    /// （某些环境下钩子坐标是物理像素、窗口 API 是逻辑像素，或相反）。
    /// </summary>
    private WindowMatch? MatchPointWithDpiFallback(System.Drawing.Point point, out System.Drawing.Point effectivePoint)
    {
        foreach (var candidate in DpiHelper.ExpandCandidates(point))
        {
            var match = Detector.MatchPoint(candidate);
            if (match is not null)
            {
                effectivePoint = candidate;
                return match;
            }
        }

        effectivePoint = point;
        return null;
    }

    private void SafePollShowState()
    {
        try
        {
            PollShowState();
        }
        catch (Exception ex)
        {
            _log.Warn($"检测放映状态失败：{ex.Message}");
        }
    }

    private void PollShowState()
    {
        var match = Detector.MatchForeground() ?? Detector.MatchAnyShowWindow();
        var key = match?.Describe();
        if (string.Equals(key, _lastShowKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastShowKey = key;
        if (match is not null)
        {
            Report($"检测到全屏放映：{key}", false);

            if (BuiltInTargets.IsOfficeShow(match.Rule, match.Window.ProcessName))
            {
                // 进入放映时 Office 可能已经切到“触摸模式”（按钮变大、出现触摸工具栏）：
                // 交给 TouchModeGuard 在后台线程自动切回“鼠标模式”，完全不注入键鼠消息、不激活窗口，
                // 因此不会打断放映，也不会让放映窗口失去响应。
                _touchModeGuard.NotifyShowStarted(match.Window.ProcessId, match.Describe());
            }
            else
            {
                _log.Trace($"非 Office 放映窗口（{match.Window.ProcessName}）：跳过「自动切回鼠标模式」");
            }
        }
        else
        {
            Report("放映已结束", false);
            _touchModeGuard.NotifyShowEnded();
        }

        try
        {
            ShowStateChanged?.Invoke(this, new ShowStateChangedEventArgs(match is not null, key ?? string.Empty));
        }
        catch (Exception ex)
        {
            _log.Warn($"通知放映状态失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 「放映时自动切回鼠标模式」是否生效。
    /// 总开关关闭时本程序不再改动 Office 的任何设置，因此这里也跟随总开关。
    /// </summary>
    private static bool IsTouchModeGuardEnabled(AppConfig config)
        => config.Enabled && config.AutoSwitchToMouseMode;

    /// <summary>后台线程回调：把切换结果写入活动记录（界面实时显示 / 日志）。</summary>
    private void OnTouchModeGuardCompleted(object? sender, TouchModeGuardResult result)
        => Report(result.Describe(), false);

    private static bool IsNearEdge(System.Drawing.Point point, int margin, WindowMatch match)
    {
        // 用“放映窗口所在显示器”的范围判断，避免多显示器 / 高 DPI 下用虚拟屏幕判断出错。
        var bounds = match.Window.MonitorBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            bounds = Interop.NativeMethods.GetVirtualScreenBounds();
        }

        return point.X <= bounds.Left + margin
            || point.X >= bounds.Right - margin
            || point.Y <= bounds.Top + margin
            || point.Y >= bounds.Bottom - margin;
    }

    private void Report(string message, bool isAdvance)
    {
        var activity = new EnhanceActivity(DateTime.Now, message, isAdvance);
        lock (_activitySync)
        {
            _activities.Enqueue(activity);
            while (_activities.Count > MaxActivities)
            {
                _activities.Dequeue();
            }
        }

        if (isAdvance)
        {
            _log.Info(message);
        }
        else
        {
            _log.Trace(message);
        }

        try
        {
            Activity?.Invoke(this, activity);
        }
        catch
        {
            // 忽略界面订阅者异常。
        }
    }
}
