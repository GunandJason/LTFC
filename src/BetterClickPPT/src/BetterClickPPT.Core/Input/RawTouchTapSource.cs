using System.Runtime.InteropServices;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Input;

/// <summary>
/// 深度触控模式：通过 Raw Input（RIDEV_INPUTSINK）直接监听触摸屏 HID 报告。
/// 部分程序（例如 PowerPoint 触控模式）会自行处理触摸输入，系统就不会再把它“提升”为鼠标消息，
/// 这种情况下只有本模式才能识别到手指轻触。
/// </summary>
public sealed class RawTouchTapSource : IDisposable
{
    private const string WindowClassName = "BetterClickPPTRawInputSink";

    // 静态字段保持委托与类注册，避免被 GC 回收 / 重复注册。
    private static WndProcDelegate? _windowProcKeepAlive;
    private static IntPtr _windowProcPointer = IntPtr.Zero;
    private static bool _classRegistered;

    private readonly AppLog? _log;
    private readonly TapGestureDetector _detector;
    private readonly Dictionary<IntPtr, HidDigitizerDevice> _devices = new();

    private IntPtr _hwnd;

    public RawTouchTapSource(int tapMaxDurationMs = 400, double tapMaxMovementRatio = 0.02, AppLog? log = null)
    {
        _log = log;
        _detector = new TapGestureDetector(tapMaxDurationMs, tapMaxMovementRatio);
    }

    /// <summary>识别到一次轻触。</summary>
    public event EventHandler<TouchTapEvent>? Tap;

    /// <summary>是否正在监听。</summary>
    public bool IsRunning => _hwnd != IntPtr.Zero;

    /// <summary>已识别的轻触次数。</summary>
    public int TapCount { get; private set; }

    /// <summary>收到的 HID 报告总数。</summary>
    public long ReportCount { get; private set; }

    /// <summary>启动监听（必须在拥有消息循环的线程上调用）。</summary>
    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        try
        {
            if (!EnsureWindowClass())
            {
                _log?.Warn("注册 Raw Input 消息窗口类失败");
                return false;
            }

            _hwnd = NativeMethods.CreateWindowEx(
                (uint)(NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE),
                WindowClassName,
                "BetterClickPPT RawInput",
                (uint)NativeConstants.WS_POPUP,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.GetModuleHandle(null),
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                _log?.Warn($"创建 Raw Input 消息窗口失败（错误码 {Marshal.GetLastWin32Error()}）");
                return false;
            }

            if (!RawInputInterop.RegisterDigitizer(_hwnd, out var errorCode))
            {
                _log?.Warn($"注册触摸屏原始输入失败（错误码 {errorCode}），深度触控模式不可用");
                Stop();
                return false;
            }

            _log?.Info("深度触控模式已启动（监听触摸屏 / 笔原始输入）");
            _instance = this;
            return true;
        }
        catch (Exception ex)
        {
            _log?.Error("启动深度触控模式失败", ex);
            Stop();
            return false;
        }
    }

    /// <summary>停止监听。</summary>
    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            RawInputInterop.UnregisterDigitizer(_hwnd, out _);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        var self = _instance;
        if (ReferenceEquals(self, this))
        {
            _instance = null;
        }

        foreach (var device in _devices.Values)
        {
            device.Dispose();
        }

        _devices.Clear();
        _detector.Reset();
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    /// <summary>设备诊断信息（界面显示）。</summary>
    public IReadOnlyList<string> GetDeviceDiagnostics()
    {
        if (_devices.Count == 0)
        {
            return new[] { "尚未收到任何触摸屏 / 笔设备的输入报告。" };
        }

        return _devices.Values
            .Select(d => $"{d.UsageDescription} | {d.Status} | 报告 {d.ReportCount} | 轻触 {d.TapCount} | {d.Name}")
            .ToList();
    }

    private static bool EnsureWindowClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        _windowProcKeepAlive = StaticWindowProc;
        _windowProcPointer = Marshal.GetFunctionPointerForDelegate(_windowProcKeepAlive);

        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = _windowProcPointer,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = NativeMethods.GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName
        };

        var atom = NativeMethods.RegisterClass(ref wndClass);
        if (atom == 0)
        {
            // 类已存在（错误码 1410）也算成功。
            return Marshal.GetLastWin32Error() == 1410;
        }

        _classRegistered = true;
        return true;
    }

    private static IntPtr StaticWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _instance;
        try
        {
            if (self is not null && msg == NativeConstants.WM_INPUT)
            {
                self.OnRawInput(lParam);
            }
            else if (self is not null && msg == NativeConstants.WM_INPUT_DEVICE_CHANGE)
            {
                self.OnDeviceChange(wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            self?._log?.Warn($"处理原始输入消息失败：{ex.Message}");
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static RawTouchTapSource? _instance;

    private void OnRawInput(IntPtr lParam)
    {
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (RawInputInterop.GetRawInputData(lParam, RawInputInterop.RID_INPUT, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var type = unchecked((uint)Marshal.ReadInt32(buffer, 0));
            if (type != RawInputInterop.RIM_TYPEHID)
            {
                return;
            }

            var deviceHandle = Marshal.ReadIntPtr(buffer, 8);
            var dataOffset = (int)headerSize;
            var sizeHid = unchecked((uint)Marshal.ReadInt32(buffer, dataOffset));
            var reportCount = unchecked((uint)Marshal.ReadInt32(buffer, dataOffset + 4));
            if (sizeHid == 0 || reportCount == 0)
            {
                return;
            }

            var device = GetOrCreateDevice(deviceHandle);
            if (device is null)
            {
                return;
            }

            var report = new byte[sizeHid];
            var reportOffset = dataOffset + sizeof(uint) * 2;
            var now = Environment.TickCount64;

            for (uint i = 0; i < reportCount; i++)
            {
                Marshal.Copy(buffer + reportOffset + (int)(i * sizeHid), report, 0, (int)sizeHid);
                ReportCount++;
                device.ProcessReport(report, now, _detector, RaiseTap);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void OnDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        var action = wParam.ToInt32();
        if (action != 2)
        {
            // GIDC_ARRIVAL：设备会在第一次收到报告时惰性初始化。
            return;
        }

        if (_devices.Remove(lParam, out var device))
        {
            device.Dispose();
            _log?.Trace($"触摸设备已移除：{device.Name}");
        }
    }

    private HidDigitizerDevice? GetOrCreateDevice(IntPtr deviceHandle)
    {
        if (deviceHandle == IntPtr.Zero)
        {
            return null;
        }

        if (_devices.TryGetValue(deviceHandle, out var cached))
        {
            return cached.IsUsable ? cached : null;
        }

        var device = new HidDigitizerDevice(deviceHandle, RawInputInterop.GetDeviceName(deviceHandle), _log);
        var usable = device.Initialize();
        _devices[deviceHandle] = device;

        if (!usable)
        {
            _log?.Trace($"忽略非触摸设备：{device.UsageDescription} / {device.Status}");
            return null;
        }

        _log?.Info($"发现触摸设备：{device.UsageDescription}（{device.Status}）");
        return device;
    }

    private void RaiseTap(TapGesture gesture, HidDigitizerDevice device)
    {
        TapCount++;
        var touchTap = new TouchTapEvent(gesture.DurationMs, gesture.MaxContacts, gesture.MovementRatio, device.Name)
        {
            NormalizedX = gesture.NormalizedX,
            NormalizedY = gesture.NormalizedY
        };

        _log?.Trace($"识别到轻触：{gesture.Describe()}（{device.UsageDescription}）");

        try
        {
            Tap?.Invoke(this, touchTap);
        }
        catch (Exception ex)
        {
            _log?.Error("处理轻触事件失败", ex);
        }
    }
}
