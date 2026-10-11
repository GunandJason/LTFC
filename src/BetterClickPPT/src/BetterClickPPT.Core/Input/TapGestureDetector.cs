namespace BetterClickPPT.Core.Input;

/// <summary>一次触摸手势的判定结果。</summary>
public readonly record struct TapGesture(
    long DurationMs,
    int MaxContacts,
    double MovementRatio,
    double? NormalizedX = null,
    double? NormalizedY = null)
{
    /// <summary>手势描述（日志 / 诊断）。</summary>
    public string Describe() => $"时长 {DurationMs}ms / 触点数 {MaxContacts} / 移动 {MovementRatio:P1}";
}

/// <summary>一次触摸采样的判定结果。</summary>
public enum TapSampleResult
{
    /// <summary>无结论（手势仍在进行中）。</summary>
    None,

    /// <summary>识别为“轻触”（应当翻页）。</summary>
    Tap,

    /// <summary>手势结束，但不是轻触（滑动 / 长按 / 多指）。</summary>
    NotATap
}

/// <summary>
/// 触摸手势状态机（纯逻辑，可单元测试）：
/// 只有“单指 + 时长不超过阈值 + 位移不超过阈值”才算轻触，
/// 因此滑动翻页、双指缩放、长按等原有操作不会受影响。
/// </summary>
public sealed class TapGestureDetector
{
    private readonly int _maxDurationMs;
    private readonly double _maxMovementRatio;

    private bool _active;
    private long _startMs;
    private int _maxContacts;
    private double? _startX;
    private double? _startY;
    private double _maxMovement;

    public TapGestureDetector(int maxDurationMs = 400, double maxMovementRatio = 0.02)
    {
        _maxDurationMs = Math.Max(50, maxDurationMs);
        _maxMovementRatio = Math.Max(0.001, maxMovementRatio);
    }

    /// <summary>当前是否有触点。</summary>
    public bool IsInContact => _active;

    /// <summary>
    /// 输入一次采样。
    /// </summary>
    /// <param name="touching">是否处于接触状态（Tip Switch / 触点数 &gt; 0）。</param>
    /// <param name="contacts">触点数量（未知时传 1）。</param>
    /// <param name="normalizedX">归一化 X 坐标（0~1，未知传 null）。</param>
    /// <param name="normalizedY">归一化 Y 坐标（0~1，未知传 null）。</param>
    /// <param name="timestampMs">采样时间戳（毫秒，可用 Environment.TickCount64）。</param>
    /// <param name="gesture">手势详情。</param>
    public TapSampleResult Feed(
        bool touching,
        int contacts,
        double? normalizedX,
        double? normalizedY,
        long timestampMs,
        out TapGesture gesture)
    {
        gesture = default;

        if (touching)
        {
            var contactCount = Math.Max(1, contacts);

            // 上一个手势没有正常结束（丢了抬起事件）时，超过阈值后重新开始一个手势。
            if (_active && timestampMs - _startMs > _maxDurationMs + 1000)
            {
                _active = false;
            }

            if (!_active)
            {
                _active = true;
                _startMs = timestampMs;
                _maxContacts = contactCount;
                _maxMovement = 0;
                _startX = normalizedX;
                _startY = normalizedY;
            }
            else
            {
                _maxContacts = Math.Max(_maxContacts, contactCount);
                if (_startX is null && normalizedX.HasValue)
                {
                    _startX = normalizedX;
                }

                if (_startY is null && normalizedY.HasValue)
                {
                    _startY = normalizedY;
                }
            }

            if (_startX.HasValue && _startY.HasValue && normalizedX.HasValue && normalizedY.HasValue)
            {
                var dx = Math.Abs(normalizedX.Value - _startX.Value);
                var dy = Math.Abs(normalizedY.Value - _startY.Value);
                _maxMovement = Math.Max(_maxMovement, Math.Max(dx, dy));
            }

            return TapSampleResult.None;
        }

        if (!_active)
        {
            return TapSampleResult.None;
        }

        _active = false;
        var duration = Math.Max(0, timestampMs - _startMs);
        gesture = new TapGesture(duration, _maxContacts, _maxMovement, _startX, _startY);

        var isTap = _maxContacts <= 1 && duration <= _maxDurationMs && _maxMovement <= _maxMovementRatio;
        return isTap ? TapSampleResult.Tap : TapSampleResult.NotATap;
    }

    /// <summary>重置状态（例如设备被拔出）。</summary>
    public void Reset()
    {
        _active = false;
        _maxContacts = 0;
        _maxMovement = 0;
        _startX = null;
        _startY = null;
    }
}
