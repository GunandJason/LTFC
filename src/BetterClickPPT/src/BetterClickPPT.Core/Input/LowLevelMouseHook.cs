using BetterClickPPT.Core.Interop;

namespace BetterClickPPT.Core.Input;

/// <summary>
/// 全局低层鼠标钩子（WH_MOUSE_LL）。
/// 可以捕获“触摸 / 笔被系统提升为鼠标消息”的点击，并且能够拦截（吞掉）该点击。
/// </summary>
public sealed class LowLevelMouseHook : IDisposable
{
    private readonly HookProc _proc;
    private IntPtr _hook;
    private bool _swallowNextUp;

    public LowLevelMouseHook()
    {
        // 必须保持委托引用，否则会被 GC 回收导致回调崩溃。
        _proc = HookCallback;
    }

    /// <summary>鼠标左键按下。返回值 true 表示吞掉该事件（目标程序不会收到）。</summary>
    public event Func<MouseHookEvent, bool>? LeftButtonDown;

    /// <summary>是否已安装钩子。</summary>
    public bool IsHooked => _hook != IntPtr.Zero;

    /// <summary>
    /// 安装钩子。必须在拥有消息循环的线程（例如界面线程）上调用。
    /// </summary>
    public bool Start()
    {
        if (IsHooked)
        {
            return true;
        }

        _hook = NativeMethods.SetWindowsHookEx(NativeConstants.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        return IsHooked;
    }

    /// <summary>卸载钩子。</summary>
    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowNextUp = false;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();

        // 只关心左键按下与抬起。
        if (message != NativeConstants.WM_LBUTTONDOWN && message != NativeConstants.WM_LBUTTONUP)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        try
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var extra = unchecked((uint)data.dwExtraInfo.ToInt64());
            var injected = (data.flags & NativeConstants.LLMHF_INJECTED) != 0;

            if (message == NativeConstants.WM_LBUTTONUP)
            {
                // 如果“按下”已被吞掉，则把配对的抬起也吞掉，避免目标程序收到孤立的抬起消息。
                if (_swallowNextUp)
                {
                    _swallowNextUp = false;
                    return new IntPtr(1);
                }

                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            var handler = LeftButtonDown;
            if (handler is null)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            var source = InputClassifier.ClassifyMouseEvent(extra, injected);
            var hookEvent = new MouseHookEvent(new System.Drawing.Point(data.pt.X, data.pt.Y), source, extra, injected, data.time);
            var swallow = handler(hookEvent);
            if (swallow)
            {
                _swallowNextUp = true;
                return new IntPtr(1);
            }
        }
        catch
        {
            // 钩子回调内出现异常必须忽略，否则会影响整个桌面输入。
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
