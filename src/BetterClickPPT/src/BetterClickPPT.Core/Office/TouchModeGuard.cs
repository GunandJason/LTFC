using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Office;

/// <summary>一次「放映开始时自动切回鼠标模式」的结果。</summary>
public sealed record TouchModeGuardResult(
    uint ProcessId,
    TouchModeState Target,
    TouchModeState Before,
    TouchModeState After,
    int Attempts,
    bool Success,
    string Detail)
{
    /// <summary>是否真的改变了模式。</summary>
    public bool Changed => Success && Before != After;

    /// <summary>供日志 / 界面显示的一句话。</summary>
    public string Describe()
    {
        if (!Success)
        {
            return $"未能自动切换到「{TouchModes.ToDisplayName(Target)}」（尝试 {Attempts} 次）：{Detail}";
        }

        return Changed
            ? $"已自动把 Office 切回「{TouchModes.ToDisplayName(After)}」（{TouchModes.ToDisplayName(Before)} → {TouchModes.ToDisplayName(After)}，第 {Attempts} 次尝试成功）"
            : $"Office 已经是「{TouchModes.ToDisplayName(After)}」，无需切换";
    }
}

/// <summary>
/// 「触摸模式守护」：检测到进入全屏放映后，在后台线程把 Office 的「触摸模式」切回「鼠标模式」。
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
///   <item>全程不注入键鼠消息、不激活窗口，所以放映过程中切换不会打断放映，也不会让放映窗口失去响应。</item>
///   <item>每次放映只处理一次：失败会自动重试若干次后放弃并写日志，绝不反复改动用户的界面设置。</item>
///   <item>任何异常都被吞掉并记录：即使这个 Office 版本不支持该按钮，也不影响翻页增强功能。</item>
/// </list>
/// </remarks>
public sealed class TouchModeGuard : IDisposable
{
    private readonly AppLog _log;
    private readonly ITouchModeSwitcher _switcher;
    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private int _running;
    private bool _disposed;

    /// <summary>创建守护（<paramref name="switcher"/> 为 null 时使用真实实现 <see cref="OfficeTouchModeSwitcher"/>）。</summary>
    public TouchModeGuard(AppLog log, ITouchModeSwitcher? switcher = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _switcher = switcher ?? new OfficeTouchModeSwitcher(log);
    }

    /// <summary>是否启用（由「总开关」与「放映开始时自动切回鼠标模式」共同控制）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>目标模式（默认鼠标模式）。</summary>
    public TouchModeState Target { get; set; } = TouchModeState.Mouse;

    /// <summary>最多尝试次数。</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>两次尝试之间的间隔（毫秒）。</summary>
    public int RetryDelayMs { get; set; } = 900;

    /// <summary>完成（成功或放弃）时触发；在后台线程触发。</summary>
    public event EventHandler<TouchModeGuardResult>? Completed;

    /// <summary>是否有一次切换正在进行。</summary>
    public bool IsBusy => Volatile.Read(ref _running) > 0;

    /// <summary>最近一次结果（供界面显示）。</summary>
    public TouchModeGuardResult? LastResult { get; private set; }

    /// <summary>累计成功切换次数。</summary>
    public int SuccessCount { get; private set; }

    /// <summary>
    /// 放映开始时调用。非阻塞：真正的 UI Automation 操作在后台线程执行。
    /// </summary>
    /// <param name="processId">放映窗口所在进程。</param>
    /// <param name="showDescription">放映窗口描述（仅用于日志）。</param>
    public void NotifyShowStarted(uint processId, string? showDescription = null)
    {
        if (_disposed || !Enabled || processId == 0 || !TouchModes.IsSwitchable(Target))
        {
            return;
        }

        CancellationToken token;
        lock (_sync)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            var cts = new CancellationTokenSource();
            _cts = cts;
            token = cts.Token;
        }

        Interlocked.Increment(ref _running);
        _log.Info($"放映开始，准备把 Office 切回「{TouchModes.ToDisplayName(Target)}」"
                  + $"（pid={processId}{(string.IsNullOrEmpty(showDescription) ? string.Empty : "，" + showDescription)}）");

        _ = Task.Run(() => RunGuarded(processId, token), CancellationToken.None);
    }

    /// <summary>放映结束时调用：取消还没完成的尝试，避免放映结束后还去改动 Office 界面。</summary>
    public void NotifyShowEnded() => Cancel();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
    }

    /// <summary>取消正在进行的尝试。</summary>
    private void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _cts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (Exception)
        {
            // 忽略：取消失败说明已经在结束了。
        }
    }

    private void RunGuarded(uint processId, CancellationToken token)
    {
        TouchModeGuardResult result;
        try
        {
            result = Run(processId, token);
        }
        catch (Exception ex)
        {
            result = new TouchModeGuardResult(processId, Target, TouchModeState.Unknown, TouchModeState.Unknown, 0, false, "异常：" + ex.Message);
            _log.Warn($"自动切换 Office 触摸模式时出现异常：{ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }

        LastResult = result;
        if (result.Success)
        {
            SuccessCount++;
            _log.Info(result.Describe());
        }
        else if (!token.IsCancellationRequested)
        {
            _log.Warn(result.Describe());
        }

        if (token.IsCancellationRequested)
        {
            // 放映已结束（或又开始了一次新放映）：不再通知界面，避免过期提示。
            return;
        }

        try
        {
            Completed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _log.Warn($"通知触摸模式切换结果失败：{ex.Message}");
        }
    }

    private TouchModeGuardResult Run(uint processId, CancellationToken token)
    {
        var maxAttempts = Math.Max(1, MaxAttempts);
        var attempts = 0;
        var before = TouchModeState.Unknown;
        var after = TouchModeState.Unknown;
        var detail = "尚未执行";

        while (attempts < maxAttempts)
        {
            if (token.IsCancellationRequested)
            {
                return new TouchModeGuardResult(processId, Target, before, after, attempts, false, "放映已结束，已取消");
            }

            attempts++;
            try
            {
                var result = _switcher.Switch(processId, Target);
                before = result.Before != TouchModeState.Unknown ? result.Before : before;
                after = result.After;
                detail = result.Detail;

                if (result.Success)
                {
                    return new TouchModeGuardResult(processId, Target, result.Before, result.After, attempts, true, result.Detail);
                }

                _log.Trace($"第 {attempts} 次尝试未成功：{result.Detail}");
            }
            catch (Exception ex)
            {
                detail = "异常：" + ex.Message;
                _log.Trace($"第 {attempts} 次尝试出现异常：{ex.Message}");
            }

            if (attempts >= maxAttempts)
            {
                break;
            }

            if (token.WaitHandle.WaitOne(Math.Max(0, RetryDelayMs)))
            {
                return new TouchModeGuardResult(processId, Target, before, after, attempts, false, "放映已结束，已取消");
            }
        }

        return new TouchModeGuardResult(processId, Target, before, after, attempts, false, detail);
    }
}
