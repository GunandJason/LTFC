using System.Runtime.InteropServices;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Interop;

/// <summary>
/// 隐藏消息窗口：把动作从“钩子回调”里挪到自己的消息循环上执行。
/// 关键原因：**绝对不能在低层鼠标钩子回调里调用 SendInput**——
/// 注入出来的点击又需要本进程的钩子去处理，而钩子回调尚未返回，会自我死锁并卡住整个桌面的输入。
/// 因此钩子只负责“是否吞掉”的快速决策，真正的动作（注入点击 / 发送按键）交给本窗口执行。
/// </summary>
public sealed class HiddenMessageWindow : IDisposable
{
    /// <summary>自定义消息：执行队列中的动作。</summary>
    public const int WmExecuteActions = 0x8000 + 0x2A;

    private static readonly object Sync = new();
    private static readonly Dictionary<IntPtr, HiddenMessageWindow> Instances = new();
    private static readonly HashSet<string> RegisteredClasses = new(StringComparer.Ordinal);

    private static WndProcDelegate? _procKeepAlive;
    private static IntPtr _procPointer = IntPtr.Zero;

    private readonly AppLog? _log;
    private readonly string _className;
    private readonly Queue<Action> _queue = new();

    private IntPtr _hwnd;

    public HiddenMessageWindow(string className = "BetterClickPPTHiddenWindow", AppLog? log = null)
    {
        _className = className;
        _log = log;
    }

    /// <summary>窗口是否已创建（必须在拥有消息循环的线程上创建）。</summary>
    public bool IsCreated => _hwnd != IntPtr.Zero;

    /// <summary>创建隐藏窗口。</summary>
    public bool Create()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return true;
        }

        try
        {
            if (!EnsureClass(_className))
            {
                _log?.Warn($"注册隐藏消息窗口类失败：{_className}");
                return false;
            }

            _hwnd = NativeMethods.CreateWindowEx(
                (uint)(NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE),
                _className,
                "BetterClickPPT",
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
                _log?.Warn($"创建隐藏消息窗口失败（错误码 {Marshal.GetLastWin32Error()}）");
                return false;
            }

            lock (Sync)
            {
                Instances[_hwnd] = this;
            }

            return true;
        }
        catch (Exception ex)
        {
            _log?.Warn($"创建隐藏消息窗口异常：{ex.Message}");
            return false;
        }
    }

    /// <summary>投递一个动作（窗口未创建时退化为立即执行）。</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_hwnd == IntPtr.Zero)
        {
            action();
            return;
        }

        lock (Sync)
        {
            _queue.Enqueue(action);
        }

        NativeMethods.PostMessage(_hwnd, WmExecuteActions, IntPtr.Zero, IntPtr.Zero);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var handle = _hwnd;
        _hwnd = IntPtr.Zero;

        lock (Sync)
        {
            Instances.Remove(handle);
            _queue.Clear();
        }

        try
        {
            NativeMethods.DestroyWindow(handle);
        }
        catch
        {
            // 忽略。
        }
    }

    private void DrainQueue()
    {
        while (true)
        {
            Action? action;
            lock (Sync)
            {
                if (_queue.Count == 0)
                {
                    return;
                }

                action = _queue.Dequeue();
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log?.Error("执行延迟动作失败", ex);
            }
        }
    }

    private static bool EnsureClass(string className)
    {
        lock (Sync)
        {
            if (RegisteredClasses.Contains(className))
            {
                return true;
            }

            _procKeepAlive ??= StaticWindowProc;
            _procPointer = Marshal.GetFunctionPointerForDelegate(_procKeepAlive);

            var wndClass = new WNDCLASS
            {
                style = 0,
                lpfnWndProc = _procPointer,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = NativeMethods.GetModuleHandle(null),
                lpszClassName = className
            };

            var atom = NativeMethods.RegisterClass(ref wndClass);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                return false;
            }

            RegisteredClasses.Add(className);
            return true;
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WmExecuteActions)
            {
                HiddenMessageWindow? target;
                lock (Sync)
                {
                    Instances.TryGetValue(hWnd, out target);
                }

                target?.DrainQueue();
                return IntPtr.Zero;
            }
        }
        catch
        {
            // 忽略来自消息窗口的异常。
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
