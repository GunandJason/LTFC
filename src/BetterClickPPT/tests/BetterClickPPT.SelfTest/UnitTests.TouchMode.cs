using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Engine;
using BetterClickPPT.Core.Office;

namespace BetterClickPPT.SelfTest;

/// <summary>
/// 「放映开始时自动切回鼠标模式」相关自测：配置项读写、增强器接线，以及守护逻辑。
/// 守护逻辑全部用假实现驱动，不会真的去改动 Office 界面。
/// </summary>
internal static class UnitTestsTouchMode
{
    public static void ConfigToggle()
    {
        var config = AppConfig.CreateDefault();
        TestRunner.AssertTrue(config.AutoSwitchToMouseMode, "默认应在放映开始时自动切回鼠标模式");

        var clone = config.Clone();
        clone.AutoSwitchToMouseMode = false;
        TestRunner.AssertTrue(config.AutoSwitchToMouseMode, "克隆后原对象不应被修改");
        TestRunner.AssertTrue(!clone.AutoSwitchToMouseMode, "克隆应保留该开关");

        // 关闭后写入磁盘再读回，开关必须保持关闭（否则用户会莫名被改动界面）。
        var directory = TestEnv.CreateTempDirectory();
        try
        {
            var store = new ConfigStore(Path.Combine(directory, "config.json"));
            store.Save(clone);

            var loaded = store.Load();
            TestRunner.AssertTrue(!loaded.AutoSwitchToMouseMode, "关闭后重新读取应保持关闭");
            TestRunner.AssertTrue(!loaded.Clone().Normalize().AutoSwitchToMouseMode, "配置收敛不应把它改回开启");

            store.Save(config);
            TestRunner.AssertTrue(store.Load().AutoSwitchToMouseMode, "默认值写入后读回应为开启");
        }
        finally
        {
            TestEnv.TryDeleteDirectory(directory);
        }
    }

    public static void EnhancerWiring()
    {
        var log = TestEnv.CreateLog();

        var config = AppConfig.CreateDefault();
        config.AutoSwitchToMouseMode = false;
        using (var enhancer = new SlideshowEnhancer(config, log))
        {
            TestRunner.AssertTrue(!enhancer.TouchMode.Enabled, "关闭开关后守护应停用");
            TestRunner.AssertEqual(TouchModeState.Mouse, enhancer.TouchMode.Target, "守护目标应为鼠标模式");
        }

        config.AutoSwitchToMouseMode = true;
        using (var enhancer = new SlideshowEnhancer(config, log))
        {
            TestRunner.AssertTrue(enhancer.TouchMode.Enabled, "开启开关后守护应启用");

            var updated = config.Clone();
            updated.AutoSwitchToMouseMode = false;
            enhancer.ApplyConfig(updated);
            TestRunner.AssertTrue(!enhancer.TouchMode.Enabled, "应用新配置后守护应同步停用");
            TestRunner.AssertEqual(TouchModeState.Mouse, enhancer.TouchMode.Target, "应用新配置后目标仍应为鼠标模式");
        }

        config.Enabled = false;
        using (var enhancer = new SlideshowEnhancer(config, log))
        {
            TestRunner.AssertTrue(!enhancer.TouchMode.Enabled, "总开关关闭时不应再改动 Office 界面设置");
        }
    }

    public static void GuardStaysIdleWhenDisabled()
    {
        var log = TestEnv.CreateLog();
        var switcher = new FakeTouchModeSwitcher { Before = TouchModeState.Touch };
        using var guard = new TouchModeGuard(log, switcher) { Enabled = false, RetryDelayMs = 10 };
        var completions = 0;
        guard.Completed += (_, _) => Interlocked.Increment(ref completions);

        guard.NotifyShowStarted(1000, "测试");
        TestRunner.AssertTrue(!guard.IsBusy, "关闭时不应发起切换");
        TestRunner.AssertEqual(0, switcher.SwitchCalls, "关闭时不应调用切换实现");
        TestRunner.AssertEqual(0, completions, "关闭时不应产生结果通知");
        TestRunner.AssertNull(guard.LastResult, "关闭时不应留下结果");

        // 目标不是可切换模式 / 进程号无效时同样不动作（避免去猜一个未知目标）。
        guard.Enabled = true;
        guard.Target = TouchModeState.Unknown;
        guard.NotifyShowStarted(1000, "测试");
        guard.Target = TouchModeState.Mouse;
        guard.NotifyShowStarted(0, "测试");
        TestRunner.AssertEqual(0, switcher.SwitchCalls, "目标无效或进程号无效时不应调用切换实现");
        TestRunner.AssertNull(guard.LastResult, "目标无效时不应留下结果");
    }

    public static void GuardRetriesThenSucceeds()
    {
        var log = TestEnv.CreateLog();
        // 前两次失败（模拟放映刚开始时 Office 界面还没准备好），第三次成功。
        var switcher = new FakeTouchModeSwitcher { Before = TouchModeState.Touch, FailuresBeforeSuccess = 2 };
        using var guard = new TouchModeGuard(log, switcher) { RetryDelayMs = 10, MaxAttempts = 5 };

        TouchModeGuardResult? result = null;
        using var signal = new ManualResetEventSlim(false);
        guard.Completed += (_, r) =>
        {
            result = r;
            signal.Set();
        };

        guard.NotifyShowStarted(4242, "测试放映");
        TestRunner.AssertTrue(signal.Wait(8000), "应在超时前完成自动切换");

        var completed = TestRunner.AssertNotNull(result, "应收到切换结果");
        TestRunner.AssertTrue(completed.Success, "重试后应成功");
        TestRunner.AssertEqual(3, completed.Attempts, "应在第 3 次尝试成功");
        TestRunner.AssertEqual(TouchModeState.Touch, completed.Before, "应报告切换前的模式");
        TestRunner.AssertEqual(TouchModeState.Mouse, completed.After, "应报告切换后的模式");
        TestRunner.AssertTrue(completed.Changed, "应确实发生了模式变化");
        TestRunner.AssertTrue(completed.Describe().Contains("鼠标模式"), $"结果描述应包含目标模式（实际：{completed.Describe()}）");
        TestRunner.AssertEqual(1, guard.SuccessCount, "成功次数应累加");
        TestRunner.AssertEqual(3, switcher.SwitchCalls, "切换实现应被调用 3 次");
        TestRunner.AssertTrue(WaitFor(() => !guard.IsBusy, 3000), "完成后不应仍处于忙碌状态");
    }

    public static void GuardGivesUpAfterMaxAttempts()
    {
        var log = TestEnv.CreateLog();
        var switcher = new FakeTouchModeSwitcher { Before = TouchModeState.Touch, FailuresBeforeSuccess = int.MaxValue };
        using var guard = new TouchModeGuard(log, switcher) { RetryDelayMs = 5, MaxAttempts = 3 };

        TouchModeGuardResult? result = null;
        using var signal = new ManualResetEventSlim(false);
        guard.Completed += (_, r) =>
        {
            result = r;
            signal.Set();
        };

        guard.NotifyShowStarted(4243, "测试放映");
        TestRunner.AssertTrue(signal.Wait(8000), "应在超时前结束尝试");

        var completed = TestRunner.AssertNotNull(result, "放弃时也应给出结果");
        TestRunner.AssertTrue(!completed.Success, "一直失败时不应报告成功");
        TestRunner.AssertEqual(3, completed.Attempts, "尝试次数应等于上限（不会无限重试）");
        TestRunner.AssertEqual(3, switcher.SwitchCalls, "切换实现应被调用 3 次");
        TestRunner.AssertEqual(0, guard.SuccessCount, "失败不应累加成功次数");
        TestRunner.AssertTrue(completed.Describe().Contains("尝试 3 次"), $"描述里应包含尝试次数（实际：{completed.Describe()}）");
    }

    public static void GuardSwallowsSwitcherExceptions()
    {
        var log = TestEnv.CreateLog();
        var switcher = new FakeTouchModeSwitcher { ThrowAlways = true };
        using var guard = new TouchModeGuard(log, switcher) { RetryDelayMs = 5, MaxAttempts = 2 };

        TouchModeGuardResult? result = null;
        using var signal = new ManualResetEventSlim(false);
        guard.Completed += (_, r) =>
        {
            result = r;
            signal.Set();
        };

        guard.NotifyShowStarted(4244, "测试放映");
        TestRunner.AssertTrue(signal.Wait(8000), "异常也应被吞掉并给出结果");

        var completed = TestRunner.AssertNotNull(result, "异常时也应给出结果");
        TestRunner.AssertTrue(!completed.Success, "异常时不应报告成功");
        TestRunner.AssertTrue(completed.Detail.Contains("异常"), $"应记录异常原因（实际：{completed.Detail}）");
        TestRunner.AssertEqual(2, switcher.SwitchCalls, "异常后应重试到上限");
        TestRunner.AssertTrue(WaitFor(() => !guard.IsBusy, 3000), "异常后不应卡在忙碌状态");
    }

    public static void GuardCancelsWhenShowEnds()
    {
        var log = TestEnv.CreateLog();
        var switcher = new FakeTouchModeSwitcher { Before = TouchModeState.Touch, FailuresBeforeSuccess = int.MaxValue };
        using var guard = new TouchModeGuard(log, switcher) { RetryDelayMs = 200, MaxAttempts = 100 };
        var completions = 0;
        guard.Completed += (_, _) => Interlocked.Increment(ref completions);

        guard.NotifyShowStarted(4245, "测试放映");
        Thread.Sleep(150);
        guard.NotifyShowEnded();

        TestRunner.AssertTrue(WaitFor(() => guard.LastResult is not null, 5000), "取消后应尽快结束尝试");
        TestRunner.AssertEqual(0, completions, "放映已结束时不应再通知界面（避免过期提示）");
        TestRunner.AssertTrue(switcher.SwitchCalls < 100, $"取消后不应继续重试（实际调用 {switcher.SwitchCalls} 次）");
        TestRunner.AssertTrue(WaitFor(() => !guard.IsBusy, 3000), "取消后不应仍处于忙碌状态");
    }

    public static void ModesAndSelectionClassification()
    {
        TestRunner.AssertEqual("鼠标模式", TouchModes.ToDisplayName(TouchModeState.Mouse), "鼠标模式显示名应为中文");
        TestRunner.AssertEqual("触摸模式", TouchModes.ToDisplayName(TouchModeState.Touch), "触摸模式显示名应为中文");
        TestRunner.AssertTrue(TouchModes.IsSwitchable(TouchModeState.Mouse), "鼠标可以作为切换目标");
        TestRunner.AssertTrue(TouchModes.IsSwitchable(TouchModeState.Touch), "触摸可以作为切换目标");
        TestRunner.AssertTrue(!TouchModes.IsSwitchable(TouchModeState.Unknown), "「未知」不能作为切换目标");

        // 选中项 → 当前模式。两项都选中时按触摸模式处理：多切一次没有副作用，漏切才会让放映界面变大。
        TestRunner.AssertEqual(TouchModeState.Mouse, OfficeTouchModeSwitcher.ClassifySelection(true, false), "只选中「鼠标」应判定为鼠标模式");
        TestRunner.AssertEqual(TouchModeState.Touch, OfficeTouchModeSwitcher.ClassifySelection(false, true), "只选中「触摸」应判定为触摸模式");
        TestRunner.AssertEqual(TouchModeState.Touch, OfficeTouchModeSwitcher.ClassifySelection(true, true), "两项都选中时按触摸模式处理");
        TestRunner.AssertEqual(TouchModeState.Unknown, OfficeTouchModeSwitcher.ClassifySelection(false, false), "都没选中应判定为未知");
        TestRunner.AssertEqual(TouchModeState.Unknown, OfficeTouchModeSwitcher.ClassifySelection(null, null), "读不到选中项时应判定为未知");

        var result = new TouchModeGuardResult(1, TouchModeState.Mouse, TouchModeState.Mouse, TouchModeState.Mouse, 1, true, "无需切换");
        TestRunner.AssertTrue(!result.Changed, "已经是目标模式时不应算作切换");
        TestRunner.AssertTrue(result.Describe().Contains("无需切换"), $"描述应说明无需切换（实际：{result.Describe()}）");
    }

    public static void ToggleLookupSmoke()
    {
        // 只读探测：即使本机没有 Office、或进程号无效，也必须安全返回 false（绝不抛异常）。
        TestRunner.AssertTrue(!OfficeTouchModeSwitcher.IsTogglePresent(0, out var zeroDetail), "进程号为 0 时应返回不可用");
        TestRunner.AssertEqual("未指定 Office 进程", zeroDetail, "应给出「未指定进程」的说明");

        var missing = OfficeTouchModeSwitcher.IsTogglePresent(0xFFFFFFFE, out var missingDetail);
        TestRunner.AssertTrue(!missing, "不存在的进程应返回不可用");
        TestRunner.AssertTrue(!string.IsNullOrWhiteSpace(missingDetail), "应给出不可用的原因");
        Console.WriteLine($"         探测结果：{missingDetail}");
    }

    public static void OfficeShowDetection()
    {
        var powerpoint = BuiltInTargets.CreatePowerPoint();
        var wps = BuiltInTargets.CreateWps();
        var custom = new TargetRule
        {
            Id = "custom-test",
            DisplayName = "myplayer.exe",
            IsBuiltIn = false,
            ProcessNames = new List<string> { "myplayer.exe" }
        };

        TestRunner.AssertTrue(BuiltInTargets.IsOfficeShow(powerpoint, "POWERPNT.EXE"), "PowerPoint 内置规则应判定为 Office 放映");
        TestRunner.AssertTrue(BuiltInTargets.IsOfficeShow(wps, "wpp.exe"), "WPS 内置规则应判定为 Office 放映");
        TestRunner.AssertTrue(!BuiltInTargets.IsOfficeShow(custom, "myplayer.exe"), "自定义放映软件不需要切换触摸模式");
        TestRunner.AssertTrue(!BuiltInTargets.IsOfficeShow(null, "unknown.exe"), "未知规则 / 未知进程不应判定为 Office");
        TestRunner.AssertTrue(BuiltInTargets.IsOfficeShow(null, "KWPP.EXE"), "按进程名也应能认出 WPS 演示");
        TestRunner.AssertTrue(BuiltInTargets.IsOfficeShow(custom, "POWERPNT.EXE"), "自定义规则命中 Office 进程名时也应判定为 Office");
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(15);
        }

        return condition();
    }

    /// <summary>假实现：用「前 N 次失败」或「一直抛异常」模拟 Office 界面还没准备好 / UIA 报错。</summary>
    private sealed class FakeTouchModeSwitcher : ITouchModeSwitcher
    {
        private int _calls;

        public int FailuresBeforeSuccess { get; set; }

        public bool ThrowAlways { get; set; }

        public TouchModeState Before { get; set; } = TouchModeState.Touch;

        public int SwitchCalls => Volatile.Read(ref _calls);

        public TouchModeProbeResult Probe(uint processId) => new(Before, "假实现");

        public TouchModeSwitchResult Switch(uint processId, TouchModeState target)
        {
            var attempt = Interlocked.Increment(ref _calls);
            if (ThrowAlways)
            {
                throw new InvalidOperationException("模拟 UI Automation 失败");
            }

            var success = attempt > FailuresBeforeSuccess;
            return new TouchModeSwitchResult(
                target,
                Before,
                success ? target : Before,
                success,
                success ? "假实现：已切换" : "假实现：仍未切换");
        }
    }

}
