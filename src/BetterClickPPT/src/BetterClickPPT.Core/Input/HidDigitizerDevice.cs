using System.Runtime.InteropServices;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Input;

/// <summary>
/// 一个 HID 数字化仪设备（触摸屏 / 笔）。
/// 通过 HID 报告描述符找出“Tip Switch / Contact Count / X / Y”等用途，
/// 从而以设备无关的方式判断“手指是否按下、是否移动”。
/// </summary>
internal sealed class HidDigitizerDevice : IDisposable
{
    private const int MaxUsagesPerCollection = 32;

    private readonly AppLog? _log;
    private readonly List<ushort> _linkCollections = new();
    private readonly ushort[] _usageBuffer = new ushort[MaxUsagesPerCollection];

    private IntPtr _preparsedData;
    private bool _hasTipSwitch;
    private bool _hasContactCount;
    private bool _hasCoordinates;
    private double? _xMin;
    private double? _xMax;
    private double? _yMin;
    private double? _yMax;

    public HidDigitizerDevice(IntPtr handle, string? deviceName, AppLog? log)
    {
        Handle = handle;
        Name = deviceName ?? $"HID#{handle.ToInt64():X}";
        _log = log;
    }

    public IntPtr Handle { get; }

    public string Name { get; }

    /// <summary>设备类型描述（触摸屏 / 笔 / 其他数字化仪）。</summary>
    public string UsageDescription { get; private set; } = "未知";

    /// <summary>是否可用（能识别接触状态）。</summary>
    public bool IsUsable { get; private set; }

    /// <summary>状态说明（诊断用）。</summary>
    public string Status { get; private set; } = "未初始化";

    /// <summary>收到的报告数量。</summary>
    public int ReportCount { get; private set; }

    /// <summary>识别出的轻触次数。</summary>
    public int TapCount { get; private set; }

    /// <summary>解析 HID 报告描述符；成功且可识别接触状态时返回 true。</summary>
    public bool Initialize()
    {
        try
        {
            uint size = 0;
            RawInputInterop.GetRawInputDeviceInfo(Handle, RawInputInterop.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            if (size == 0)
            {
                Status = "无法读取 HID 描述符";
                return false;
            }

            _preparsedData = Marshal.AllocHGlobal((int)size);
            if (RawInputInterop.GetRawInputDeviceInfo(Handle, RawInputInterop.RIDI_PREPARSEDDATA, _preparsedData, ref size) == uint.MaxValue)
            {
                FreePreparsedData();
                Status = "无法读取 HID 描述符";
                return false;
            }

            var status = HidInterop.HidP_GetCaps(_preparsedData, out var caps);
            if (status != HidInterop.HIDP_STATUS_SUCCESS)
            {
                FreePreparsedData();
                Status = "HidP_GetCaps 失败";
                return false;
            }

            if (caps.UsagePage != HidUsagePages.Digitizer)
            {
                Status = $"非数字化仪设备（0x{caps.UsagePage:X2}/0x{caps.Usage:X2}）";
                return false;
            }

            UsageDescription = caps.Usage switch
            {
                HidDigitizerUsages.TouchScreen => "触摸屏",
                HidDigitizerUsages.Pen => "手写笔",
                HidDigitizerUsages.TouchPad => "触摸板（已忽略）",
                _ => $"数字化仪(0x{caps.Usage:X2})"
            };

            // 触摸板不处理，避免笔记本触摸板点击被当作翻页。
            if (caps.Usage == HidDigitizerUsages.TouchPad)
            {
                Status = "触摸板，已忽略";
                return false;
            }

            ParseValueCaps(caps);

            IsUsable = _hasTipSwitch || _hasContactCount;
            Status = IsUsable
                ? $"可用（TipSwitch={_hasTipSwitch}, ContactCount={_hasContactCount}, 坐标={_hasCoordinates}, 集合={_linkCollections.Count}）"
                : "无法识别接触状态，已忽略";

            _log?.Trace($"HID 设备 {Name}：{UsageDescription} / {Status}");
            return IsUsable;
        }
        catch (Exception ex)
        {
            _log?.Warn($"解析 HID 设备失败：{ex.Message}");
            Status = "解析异常：" + ex.Message;
            FreePreparsedData();
            return false;
        }
    }

    /// <summary>
    /// 处理一个 HID 输入报告：更新手势状态机，识别到“轻触”时回调。
    /// </summary>
    public void ProcessReport(byte[] report, long timestampMs, TapGestureDetector detector, Action<TapGesture, HidDigitizerDevice> onTap)
    {
        if (!IsUsable || _preparsedData == IntPtr.Zero)
        {
            return;
        }

        ReportCount++;

        bool? touching = null;
        var contacts = 0;
        ushort touchedCollection = 0;
        var hasTouchedCollection = false;

        foreach (var linkCollection in _linkCollections)
        {
            if (_hasContactCount && TryReadValue(HidUsagePages.Digitizer, HidDigitizerUsages.ContactCount, linkCollection, report, out var contactValue))
            {
                contacts = Math.Max(contacts, (int)contactValue);
                if (contactValue > 0)
                {
                    touching = true;
                    if (!hasTouchedCollection)
                    {
                        touchedCollection = linkCollection;
                        hasTouchedCollection = true;
                    }
                }
            }

            if (!_hasTipSwitch)
            {
                continue;
            }

            if (TryReadValue(HidUsagePages.Digitizer, HidDigitizerUsages.TipSwitch, linkCollection, report, out var tipSwitch))
            {
                if (tipSwitch != 0)
                {
                    touching = true;
                    if (!hasTouchedCollection)
                    {
                        touchedCollection = linkCollection;
                        hasTouchedCollection = true;
                    }
                }
                else
                {
                    touching ??= false;
                }
            }
            else if (HidInterop.TryGetUsages(_preparsedData, report, HidUsagePages.Digitizer, linkCollection, _usageBuffer, out var usageCount) && usageCount > 0)
            {
                touching = true;
                if (!hasTouchedCollection)
                {
                    touchedCollection = linkCollection;
                    hasTouchedCollection = true;
                }
            }
        }

        if (touching is null)
        {
            return;
        }

        if (touching.Value && contacts == 0)
        {
            contacts = 1;
        }

        double? normalizedX = null;
        double? normalizedY = null;
        if (touching.Value && _hasCoordinates && hasTouchedCollection)
        {
            if (TryReadValue(HidUsagePages.GenericDesktop, HidDesktopUsages.X, touchedCollection, report, out var x))
            {
                normalizedX = Normalize(x, _xMin, _xMax);
            }

            if (TryReadValue(HidUsagePages.GenericDesktop, HidDesktopUsages.Y, touchedCollection, report, out var y))
            {
                normalizedY = Normalize(y, _yMin, _yMax);
            }
        }

        var result = detector.Feed(touching.Value, contacts, normalizedX, normalizedY, timestampMs, out var gesture);
        if (result == TapSampleResult.Tap)
        {
            TapCount++;
            onTap(gesture, this);
        }
    }

    /// <inheritdoc />
    public void Dispose() => FreePreparsedData();

    private void ParseValueCaps(HIDP_CAPS caps)
    {
        if (caps.NumberInputValueCaps == 0)
        {
            return;
        }

        var valueCaps = new HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
        var length = caps.NumberInputValueCaps;
        if (HidInterop.HidP_GetValueCaps(HidInterop.HIDP_INPUT, valueCaps, ref length, _preparsedData) != HidInterop.HIDP_STATUS_SUCCESS)
        {
            return;
        }

        for (var i = 0; i < Math.Min(length, valueCaps.Length); i++)
        {
            var valueCap = valueCaps[i];
            if (!_linkCollections.Contains(valueCap.LinkCollection))
            {
                _linkCollections.Add(valueCap.LinkCollection);
            }

            if (valueCap.UsagePage == HidUsagePages.Digitizer)
            {
                if (Covers(valueCap, HidDigitizerUsages.TipSwitch))
                {
                    _hasTipSwitch = true;
                }

                if (Covers(valueCap, HidDigitizerUsages.ContactCount))
                {
                    _hasContactCount = true;
                }
            }
            else if (valueCap.UsagePage == HidUsagePages.GenericDesktop)
            {
                if (Covers(valueCap, HidDesktopUsages.X))
                {
                    _hasCoordinates = true;
                    _xMin = valueCap.LogicalMin;
                    _xMax = valueCap.LogicalMax;
                }

                if (Covers(valueCap, HidDesktopUsages.Y))
                {
                    _hasCoordinates = true;
                    _yMin = valueCap.LogicalMin;
                    _yMax = valueCap.LogicalMax;
                }
            }
        }

        if (_linkCollections.Count == 0)
        {
            _linkCollections.Add(0);
        }
    }

    /// <summary>判断某个数值能力项是否覆盖指定用途（兼容 Range / NotRange 两种描述方式）。</summary>
    private static bool Covers(HIDP_VALUE_CAPS valueCap, ushort usage)
        => valueCap.IsRange
            ? usage >= valueCap.Range.UsageMin && usage <= valueCap.Range.UsageMax
            : valueCap.Range.UsageMin == usage;

    private bool TryReadValue(ushort usagePage, ushort usage, ushort linkCollection, byte[] report, out uint value)
        => HidInterop.TryGetUsageValue(_preparsedData, report, usagePage, usage, linkCollection, out value);

    private static double? Normalize(uint value, double? min, double? max)
    {
        if (min is null || max is null || max.Value - min.Value <= 0)
        {
            return null;
        }

        return Math.Clamp((value - min.Value) / (max.Value - min.Value), 0d, 1d);
    }

    private void FreePreparsedData()
    {
        if (_preparsedData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_preparsedData);
            _preparsedData = IntPtr.Zero;
        }
    }
}
