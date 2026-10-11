using System.Runtime.InteropServices;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Input;

/// <summary>
/// 向放映窗口发送“下一页”按键。
/// </summary>
public sealed class KeySender
{
    private readonly AppLog? _log;

    public KeySender(AppLog? log = null)
    {
        _log = log;
    }

    /// <summary>把配置里的按键名转换为虚拟键码。</summary>
    public static ushort ResolveVirtualKey(string? name) => name switch
    {
        NextKeyNames.Right => NativeConstants.VK_RIGHT,
        NextKeyNames.Down => NativeConstants.VK_DOWN,
        NextKeyNames.Space => NativeConstants.VK_SPACE,
        NextKeyNames.Enter => NativeConstants.VK_RETURN,
        _ => NativeConstants.VK_NEXT
    };

    /// <summary>
    /// 发送“下一页”。
    /// </summary>
    /// <param name="targetWindow">放映窗口句柄（用于自动判断发送方式 / 回退）。</param>
    /// <param name="keyName">按键名（见 <see cref="NextKeyNames"/>）。</param>
    /// <param name="mode">发送方式（见 <see cref="KeySendModes"/>）。</param>
    /// <param name="detail">返回实际使用的发送方式，便于日志与诊断。</param>
    public bool SendNext(IntPtr targetWindow, string keyName, string mode, out string detail)
    {
        var virtualKey = ResolveVirtualKey(keyName);
        var resolved = ResolveMode(targetWindow, mode);

        if (resolved == KeySendModes.PostMessage)
        {
            if (NativeMethods.IsWindow(targetWindow) && PostKeyToWindow(targetWindow, virtualKey))
            {
                detail = $"{KeySendModes.PostMessage}({keyName})";
                return true;
            }

            // 窗口已失效时退回模拟按键。
            resolved = KeySendModes.SendInput;
        }

        var ok = SendKeyInput(virtualKey);
        detail = $"{KeySendModes.SendInput}({keyName})";

        if (!ok)
        {
            // SendInput 被 UIPI 拦截或失败时，再试一次窗口消息。
            if (NativeMethods.IsWindow(targetWindow) && PostKeyToWindow(targetWindow, virtualKey))
            {
                detail = $"{KeySendModes.PostMessage}({keyName})/回退";
                return true;
            }

            _log?.Warn($"发送“{keyName}”按键失败（错误码 {Marshal.GetLastWin32Error()}）");
            return false;
        }

        return true;
    }

    /// <summary>自动判断发送方式。</summary>
    public static string ResolveMode(IntPtr targetWindow, string mode)
    {
        if (!string.Equals(mode, KeySendModes.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(mode, KeySendModes.PostMessage, StringComparison.OrdinalIgnoreCase)
                ? KeySendModes.PostMessage
                : KeySendModes.SendInput;
        }

        // 自动模式：只有放映窗口本身就是前台窗口时才模拟按键（真实按键一定会送到它）；
        // 否则直接向放映窗口投递按键消息——这样即使放映窗口不在前台（演讲者视图、
        // 多显示器、被别的窗口遮挡）也能可靠翻页，而且不会把按键送给别的程序。
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == targetWindow ? KeySendModes.SendInput : KeySendModes.PostMessage;
    }

    private static bool SendKeyInput(ushort virtualKey)
    {
        var scanCode = (ushort)NativeMethods.MapVirtualKey(virtualKey, 0);
        var inputs = new INPUT[2];

        inputs[0].type = NativeConstants.INPUT_KEYBOARD;
        inputs[0].u.ki = new KEYBDINPUT { wVk = virtualKey, wScan = scanCode, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero };

        inputs[1].type = NativeConstants.INPUT_KEYBOARD;
        inputs[1].u.ki = new KEYBDINPUT { wVk = virtualKey, wScan = scanCode, dwFlags = NativeConstants.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static bool PostKeyToWindow(IntPtr hWnd, ushort virtualKey)
    {
        var scanCode = (uint)NativeMethods.MapVirtualKey(virtualKey, 0);
        var down = 1u | (scanCode << 16);
        var up = down | (1u << 30) | 0x80000000u;

        var downResult = NativeMethods.PostMessage(hWnd, NativeConstants.WM_KEYDOWN, new IntPtr(virtualKey), new IntPtr((long)down));
        var upResult = NativeMethods.PostMessage(hWnd, NativeConstants.WM_KEYUP, new IntPtr(virtualKey), new IntPtr((long)up));
        return downResult != IntPtr.Zero && upResult != IntPtr.Zero;
    }
}
