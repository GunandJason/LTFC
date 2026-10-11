using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Engine;
using BetterClickPPT.Core.Input;

namespace BetterClickPPT.SelfTest;

/// <summary>
/// 端到端测试：真实安装全局鼠标钩子 → 真实注入“触摸点击” → 模拟放映窗口收到下一页按键。
/// </summary>
internal static class EndToEndTests
{
    /// <summary>触摸点击应翻页，且原始点击被吞掉（不会翻两页）。</summary>
    public static void TouchClickAdvancesAndSwallows()
        => Scenario(
            configure: _ => { },
            fullScreen: false,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true, Console.WriteLine),
            assert: session =>
            {
                TestRunner.AssertEqual(1, session.CountEvents("NEXT"), "触摸点击应翻一页");
                TestRunner.AssertEqual(0, session.CountEvents("CLICK_DOWN"), "触摸点击应被吞掉，避免翻两页");
            });

    /// <summary>防抖：短时间内连续两次触摸点击只翻一页。</summary>
    public static void DebouncePreventsDoubleAdvance()
        => Scenario(
            // 放大防抖窗口，避免测试机负载抖动导致第二次点击落到窗口之外
            configure: config => config.DebounceMs = 1500,
            fullScreen: false,
            click: (session, _) =>
            {
                var first = InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true);
                Pump.Wait(250);
                var second = InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true);
                return first && second;
            },
            assert: session =>
            {
                TestRunner.AssertEqual(1, session.CountEvents("NEXT"), "防抖窗口内的第二次点击不应再次翻页");
                TestRunner.AssertEqual(0, session.CountEvents("CLICK_DOWN"), "两次点击都应被吞掉");
            });

    /// <summary>关闭“吞掉原始点击”后，原始点击应照常传给放映窗口。</summary>
    public static void SwallowDisabledPassesClickThrough()
        => Scenario(
            configure: config => config.SwallowTouchClick = false,
            fullScreen: false,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true),
            assert: session =>
            {
                TestRunner.AssertEqual(1, session.CountEvents("NEXT"), "仍应翻一页");
                TestRunner.AssertEqual(1, session.CountEvents("CLICK_DOWN"), "原始点击应被放行");
            });

    /// <summary>PostMessage 发送方式同样可以翻页。</summary>
    public static void PostMessageModeAdvances()
        => Scenario(
            configure: config => config.KeySendMode = KeySendModes.PostMessage,
            fullScreen: false,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true),
            assert: session =>
            {
                TestRunner.AssertEqual(1, session.CountEvents("NEXT"), "窗口消息方式也应翻一页");
                TestRunner.AssertEqual(0, session.CountEvents("CLICK_DOWN"), "触摸点击应被吞掉");
            });

    /// <summary>目标软件不匹配时，点击必须原样放行（不影响其它程序）。</summary>
    public static void UnmatchedProcessIsNotTouched()
        => Scenario(
            configure: config =>
            {
                config.Targets.Clear();
                config.Targets.Add(new TargetRule
                {
                    Id = "other",
                    DisplayName = "其它程序",
                    Enabled = true,
                    ProcessNames = new List<string> { "根本不存在的程序.exe" },
                    RequireFullScreen = false
                });
            },
            fullScreen: false,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true),
            assert: session =>
            {
                TestRunner.AssertEqual(0, session.CountEvents("NEXT"), "不匹配时不应发送翻页按键");
                TestRunner.AssertEqual(1, session.CountEvents("CLICK_DOWN"), "不匹配时点击必须放行");
            },
            checkDetection: false);

    /// <summary>真实全屏（铺满显示器、无标题栏、类名 screenClass）能被识别并翻页。</summary>
    public static void FullScreenWindowIsDetectedAndAdvances()
        => Scenario(
            configure: _ => { },
            fullScreen: true,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true),
            assert: session =>
            {
                TestRunner.AssertEqual(1, session.CountEvents("NEXT"), "全屏放映应翻一页");
                TestRunner.AssertEqual(0, session.CountEvents("CLICK_DOWN"), "全屏放映中点击应被吞掉");
            });

    /// <summary>普通注入点击（非触摸）应被忽略：不影响自动化工具与其它辅助程序。</summary>
    public static void InjectedPlainClickIsIgnored()
        => Scenario(
            configure: config =>
            {
                config.AdvanceOnMouse = true;
                config.SwallowMouseClick = true;
            },
            fullScreen: false,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: false),
            assert: session =>
            {
                TestRunner.AssertEqual(0, session.CountEvents("NEXT"), "注入的普通点击不应触发翻页");
                TestRunner.AssertEqual(1, session.CountEvents("CLICK_DOWN"), "注入的普通点击应照常放行");
            });

    /// <summary>
    /// 默认模式：把触摸轻触“还原”成一次真实鼠标点击
    /// —— 这样分步动画、超链接、屏幕上的上一页/下一页按钮都能正常工作。
    /// </summary>
    public static void TouchTapIsReplayedAsMouseClick()
        => Scenario(
            configure: config => config.TouchAction = TouchActions.MouseClick,
            fullScreen: false,
            click: (session, _) => InputSimulator.Click(session.Center.X, session.Center.Y, asTouch: true),
            assert: session =>
            {
                TestRunner.AssertEqual(0, session.CountEvents("NEXT"), "还原鼠标点击时不发送键盘按键");
                TestRunner.AssertEqual(1, session.CountEvents("CLICK_DOWN"), "应恰好收到一次左键按下");
                TestRunner.AssertEqual(1, session.CountEvents("CLICK_UP"), "应恰好收到一次左键抬起");

                var clicked = session.GetClickedPoints();
                if (clicked.Count == 1)
                {
                    var screenPoint = session.ClientToScreen(clicked[0]);
                    var expected = session.Center;
                    var distance = Math.Max(Math.Abs(screenPoint.X - expected.X), Math.Abs(screenPoint.Y - expected.Y));
                    Console.WriteLine($"         点击位置：{screenPoint.X},{screenPoint.Y}（期望 {expected.X},{expected.Y}，偏差 {distance}px）");
                    TestRunner.AssertTrue(distance <= 8, $"还原的鼠标点击应落在轻触位置（偏差 {distance}px）");
                }
            });

    /// <summary>
    /// 触摸点在放映窗口左侧时，还原的鼠标点击也要落在左侧
    /// —— 这样屏幕上的“上一页”按钮、超链接等原有点击目标都能被正确命中。
    /// </summary>
    public static void TouchTapOnLeftSideKeepsPosition()
        => Scenario(
            configure: config => config.TouchAction = TouchActions.MouseClick,
            fullScreen: false,
            click: (session, _) =>
            {
                var left = session.Bounds.Left + session.Bounds.Width / 5;
                var middle = session.Bounds.Top + session.Bounds.Height / 2;
                return InputSimulator.Click(left, middle, asTouch: true);
            },
            assert: session =>
            {
                TestRunner.AssertEqual(0, session.CountEvents("NEXT"), "还原鼠标点击时不发送键盘按键");
                TestRunner.AssertEqual(1, session.CountEvents("CLICK_DOWN"), "应收到一次左键按下");

                var clicked = session.GetClickedPoints();
                if (clicked.Count == 1)
                {
                    var screenPoint = session.ClientToScreen(clicked[0]);
                    var expectedX = session.Bounds.Left + session.Bounds.Width / 5;
                    Console.WriteLine($"         点击位置：{screenPoint.X},{screenPoint.Y}（期望 X≈{expectedX}，窗口宽 {session.Bounds.Width}）");
                    TestRunner.AssertTrue(
                        Math.Abs(screenPoint.X - expectedX) <= 8,
                        $"左侧轻触应在左侧位置点击（实际 X={screenPoint.X}，期望 X≈{expectedX}）");
                }
            });

    private static void Scenario(
        Action<AppConfig> configure,
        bool fullScreen,
        Func<MockShowSession, SlideshowEnhancer, bool> click,
        Action<MockShowSession> assert,
        bool checkDetection = true)
    {
        using var mock = MockShowSession.Start(fullScreen);
        var log = TestEnv.CreateLog();
        var config = CreateConfig(fullScreen);
        configure(config);

        using var enhancer = new SlideshowEnhancer(config, log);
        TestRunner.AssertTrue(enhancer.Start(), "增强器应能启动");
        Pump.Wait(350);
        Console.WriteLine($"         鼠标钩子={enhancer.IsMouseHookActive}，深度触控={enhancer.IsRawTouchActive}，日志={log.FilePath}");

        if (checkDetection)
        {
            var match = enhancer.Detector.MatchPoint(mock.Center);
            TestRunner.AssertNotNull(match, "应按坐标命中模拟放映窗口");
            Console.WriteLine($"         命中规则：{match!.Describe()}");
        }

        if (!click(mock, enhancer))
        {
            // 桌面输入队列被其它程序长期占用（SendInput 被阻塞）时，跳过而不是卡死。
            TestRunner.Skip("输入注入超时：桌面输入队列被占用（SendInput 被阻塞），请在干净会话中重跑");
        }

        Pump.Wait(1000);

        var isMockForeground = BetterClickPPT.Core.Interop.NativeMethods.GetForegroundWindow() == mock.Handle;
        var advanced = enhancer.AdvanceCount > 0;
        var usedPostMessage = enhancer.RecentActivities.Any(a => a.Message.Contains("PostMessage", StringComparison.Ordinal));
        var usedSendInput = enhancer.RecentActivities.Any(a => a.Message.Contains("SendInput", StringComparison.Ordinal));

        Console.WriteLine($"         日志统计：NEXT={mock.CountEvents("NEXT")}，CLICK_DOWN={mock.CountEvents("CLICK_DOWN")}，CLICK_UP={mock.CountEvents("CLICK_UP")}，"
                          + $"窗口={mock.Bounds.Left},{mock.Bounds.Top} {mock.Bounds.Width}×{mock.Bounds.Height}，光标={Cursor.Position.X},{Cursor.Position.Y}");
        Console.WriteLine($"         增强器统计：鼠标事件={enhancer.MouseEventCount}，触摸点击={enhancer.TouchClickCount}，触发={enhancer.AdvanceCount}（还原点击 {enhancer.ClickReplayCount}），"
                          + $"模拟窗口在前台={isMockForeground}，使用窗口消息={usedPostMessage}，使用模拟按键={usedSendInput}");

        if (advanced)
        {
            if (enhancer.ClickReplayCount > 0)
            {
                // 该次动作是“把触摸轻触还原成一次真实鼠标点击”
                TestRunner.AssertTrue(
                    string.Equals(config.TouchAction, TouchActions.MouseClick, StringComparison.OrdinalIgnoreCase),
                    "只有在“模拟鼠标点击”模式下才会还原鼠标点击");
                TestRunner.AssertTrue(!usedSendInput && !usedPostMessage, "还原鼠标点击时不应再发送键盘按键");
            }
            else if (string.Equals(config.KeySendMode, KeySendModes.PostMessage, StringComparison.OrdinalIgnoreCase))
            {
                TestRunner.AssertTrue(usedPostMessage, "显式选择窗口消息方式时应使用 PostMessage");
            }
            else if (string.Equals(config.KeySendMode, KeySendModes.SendInput, StringComparison.OrdinalIgnoreCase))
            {
                TestRunner.AssertTrue(usedSendInput, "显式选择模拟按键方式时应使用 SendInput");
            }
            else
            {
                // 自动模式：放映窗口在前台时模拟按键；否则向放映窗口投递消息。
                TestRunner.AssertEqual(
                    isMockForeground ? usedSendInput : usedPostMessage,
                    true,
                    isMockForeground ? "放映窗口在前台时应使用 SendInput 模拟按键" : "放映窗口不在前台时应使用 PostMessage 窗口消息");
            }
        }

        foreach (var line in mock.ReadLog())
        {
            Console.WriteLine("         [模拟窗口] " + line);
        }
        foreach (var activity in enhancer.RecentActivities.TakeLast(4))
        {
            Console.WriteLine("         · " + activity);
        }

        if (File.Exists(log.FilePath))
        {
            foreach (var line in File.ReadAllLines(log.FilePath).TakeLast(8))
            {
                Console.WriteLine("         > " + line);
            }
        }

        assert(mock);
        enhancer.Stop();
    }

    private static AppConfig CreateConfig(bool requireFullScreen)
    {
        var config = AppConfig.CreateDefault();

        // 测试环境没有触摸屏，关闭原始输入通道，只验证“触摸被提升为鼠标点击”的通道
        config.RawTouchEnabled = false;
        config.AdvanceOnTouch = true;
        config.SwallowTouchClick = true;
        config.AdvanceOnMouse = false;
        config.SwallowMouseClick = true;
        config.TouchAction = TouchActions.NextKey;
        config.DebounceMs = 400;
        config.NextKey = NextKeyNames.PageDown;
        config.KeySendMode = KeySendModes.Auto;
        config.ShowNotifications = false;
        config.VerboseLog = true;

        config.Targets.Clear();
        config.Targets.Add(new TargetRule
        {
            Id = "mock",
            DisplayName = "模拟放映窗口",
            Enabled = true,
            ProcessNames = new List<string> { "BetterClickPPT.MockShow.exe" },
            ClassNames = new List<string> { "screenClass" },
            RequireFullScreen = requireFullScreen,
            RequireBorderless = true
        });

        return config;
    }
}
