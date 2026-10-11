using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Input;

namespace BetterClickPPT.SelfTest;

/// <summary>输入识别相关的单元测试。</summary>
internal static class UnitTestsInput
{
    private const ushort VkPageDown = 0x22;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkSpace = 0x20;
    private const ushort VkEnter = 0x0D;

    public static void TapGestureDetection()
    {
        var detector = new TapGestureDetector(400, 0.02);

        // 轻触
        TestRunner.AssertEqual(TapSampleResult.None, detector.Feed(true, 1, 0.5, 0.5, 1000, out _), "按下时不应有结论");
        var result = detector.Feed(false, 0, 0.5, 0.5, 1150, out var gesture);
        TestRunner.AssertEqual(TapSampleResult.Tap, result, "短按应识别为轻触");
        TestRunner.AssertEqual(150L, gesture.DurationMs, "时长应为 150ms");

        // 长按
        detector.Feed(true, 1, 0.5, 0.5, 2000, out _);
        TestRunner.AssertEqual(TapSampleResult.NotATap, detector.Feed(false, 0, 0.5, 0.5, 3800, out _), "长按不应识别为轻触");

        // 滑动
        detector.Feed(true, 1, 0.10, 0.5, 4000, out _);
        detector.Feed(true, 1, 0.30, 0.5, 4050, out _);
        result = detector.Feed(false, 0, 0.30, 0.5, 4100, out gesture);
        TestRunner.AssertEqual(TapSampleResult.NotATap, result, "滑动不应识别为轻触");
        TestRunner.AssertTrue(gesture.MovementRatio > 0.02, "应记录到明显的位移");

        // 双指
        detector.Feed(true, 2, 0.5, 0.5, 5000, out _);
        TestRunner.AssertEqual(TapSampleResult.NotATap, detector.Feed(false, 0, 0.5, 0.5, 5100, out _), "多指不应识别为轻触");

        // 丢失抬起事件后应能重新开始
        detector.Feed(true, 1, 0.5, 0.5, 6000, out _);
        detector.Feed(true, 1, 0.5, 0.5, 9000, out _);
        result = detector.Feed(false, 0, 0.5, 0.5, 9050, out gesture);
        TestRunner.AssertEqual(TapSampleResult.Tap, result, "超时后应重新开始手势");
        TestRunner.AssertEqual(50L, gesture.DurationMs, "应使用新的手势起点");
    }

    public static void InputSourceClassification()
    {
        TestRunner.AssertEqual(InputSource.TouchOrPen, InputClassifier.ClassifyMouseEvent(0xFF515700, injected: false), "触摸特征值应判定为触摸");
        TestRunner.AssertEqual(InputSource.TouchOrPen, InputClassifier.ClassifyMouseEvent(0xFF515700, injected: true), "带特征值的注入事件也应判定为触摸");
        TestRunner.AssertEqual(InputSource.TouchOrPen, InputClassifier.ClassifyMouseEvent(0xFF515701, injected: false), "掩码匹配应生效");
        TestRunner.AssertEqual(InputSource.Injected, InputClassifier.ClassifyMouseEvent(0, injected: true), "注入事件应判定为注入");
        TestRunner.AssertEqual(InputSource.Mouse, InputClassifier.ClassifyMouseEvent(0, injected: false), "普通事件应判定为鼠标");
    }

    public static void KeyResolution()
    {
        TestRunner.AssertEqual(VkPageDown, KeySender.ResolveVirtualKey(NextKeyNames.PageDown), "PageDown 映射");
        TestRunner.AssertEqual(VkRight, KeySender.ResolveVirtualKey(NextKeyNames.Right), "右方向键映射");
        TestRunner.AssertEqual(VkDown, KeySender.ResolveVirtualKey(NextKeyNames.Down), "下方向键映射");
        TestRunner.AssertEqual(VkSpace, KeySender.ResolveVirtualKey(NextKeyNames.Space), "空格映射");
        TestRunner.AssertEqual(VkEnter, KeySender.ResolveVirtualKey(NextKeyNames.Enter), "回车映射");
        TestRunner.AssertEqual(VkPageDown, KeySender.ResolveVirtualKey("未知按键"), "未知按键应回退 PageDown");
    }

    public static void RawTouchSourceSmoke()
    {
        var log = TestEnv.CreateLog();
        using var source = new RawTouchTapSource(400, 0.02, log);

        var started = source.Start();
        Console.WriteLine($"         深度触控模式启动结果：{started}");

        foreach (var device in source.GetDeviceDiagnostics())
        {
            Console.WriteLine("         " + device);
        }

        if (started)
        {
            source.Stop();
            TestRunner.AssertTrue(!source.IsRunning, "停止后应不再监听");
        }
    }
}
