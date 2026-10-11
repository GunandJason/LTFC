using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Interop;

namespace BetterClickPPT.Core.Input;

/// <summary>输入来源判定。</summary>
public static class InputClassifier
{
    /// <summary>
    /// 判断鼠标消息的真实来源。
    /// Windows 会把触摸屏 / 手写笔的输入“提升”为鼠标消息，并在 dwExtraInfo 中写入特征值
    /// （MI_WP_SIGNATURE = 0xFF515700），据此即可区分“手指点击”和“鼠标点击”。
    /// </summary>
    public static InputSource ClassifyMouseEvent(
        uint extraInfo,
        bool injected,
        uint signatureValue = AppConfig.DefaultTouchSignatureValue,
        uint signatureMask = AppConfig.DefaultTouchSignatureMask)
    {
        // 触摸 / 笔的特征值优先判断：真实的触摸点击不会是注入事件，
        // 但自动化测试会通过 SendInput 携带同样的特征值来模拟触摸。
        if (signatureMask != 0 && (extraInfo & signatureMask) == (signatureValue & signatureMask))
        {
            return InputSource.TouchOrPen;
        }

        if (injected)
        {
            return InputSource.Injected;
        }

        return InputSource.Mouse;
    }
}
