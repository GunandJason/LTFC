using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Input;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Util;

namespace BetterClickPPT.SelfTest;

/// <summary>配置与匹配相关的单元测试。</summary>
internal static class UnitTestsConfig
{
    public static void WildcardMatching()
    {
        TestRunner.AssertTrue(Wildcard.IsMatch("POWERPNT.EXE", "powerpnt.exe"), "应忽略大小写");
        TestRunner.AssertTrue(Wildcard.IsMatch("wpp64.exe", "wpp*.exe"), "* 通配符应生效");
        TestRunner.AssertTrue(Wildcard.IsMatch("wpp64.exe", "wpp??.exe"), "? 通配符应生效");
        TestRunner.AssertTrue(!Wildcard.IsMatch("wps.exe", "wpp*.exe"), "不应错误匹配");
        TestRunner.AssertTrue(Wildcard.IsMatchAny("KWPP.EXE", new[] { "wpp.exe", "kwpp.exe" }), "集合匹配应生效");
        TestRunner.AssertTrue(!Wildcard.IsMatchAny(null, new[] { "a.exe" }), "空值不应匹配");
    }

    public static void ConfigRoundTrip()
    {
        var directory = TestEnv.CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "config.json");
            var store = new ConfigStore(path);

            var config = AppConfig.CreateDefault();
            config.DebounceMs = 123;
            config.NextKey = NextKeyNames.Right;
            config.Targets.First(t => t.Id == "wps").Enabled = false;
            config.Targets.Add(new TargetRule
            {
                Id = "custom-test",
                DisplayName = "测试程序",
                Enabled = true,
                ProcessNames = new List<string> { "myplayer.exe" },
                RequireFullScreen = false
            });

            store.Save(config);

            var loaded = new ConfigStore(path).Load();
            TestRunner.AssertEqual(123, loaded.DebounceMs, "防抖时间应保存");
            TestRunner.AssertEqual(NextKeyNames.Right, loaded.NextKey, "翻页按键应保存");
            TestRunner.AssertTrue(!loaded.Targets.First(t => t.Id == "wps").Enabled, "WPS 规则的启用状态应保存");
            TestRunner.AssertTrue(loaded.Targets.Any(t => t.Id == "custom-test"), "自定义规则应保存");
            TestRunner.AssertTrue(File.ReadAllText(path).Contains("测试程序"), "配置文件应包含可读的中文");
        }
        finally
        {
            TestEnv.TryDeleteDirectory(directory);
        }
    }

    public static void ConfigNormalizeRepairs()
    {
        var config = new AppConfig
        {
            Targets = new List<TargetRule>(),
            DebounceMs = -100,
            TapMaxMovementRatio = 99,
            LogMaxKb = 1,
            NextKey = "不存在的按键",
            KeySendMode = "不存在的模式",
            TouchAction = "不存在的动作"
        };

        config.Normalize();

        TestRunner.AssertTrue(config.Targets.Any(t => t.Id == "powerpoint"), "应补齐 PowerPoint 内置规则");
        TestRunner.AssertTrue(config.Targets.Any(t => t.Id == "wps"), "应补齐 WPS 内置规则");
        TestRunner.AssertEqual(0, config.DebounceMs, "防抖时间应被收敛");
        TestRunner.AssertTrue(config.TapMaxMovementRatio <= 0.5, "移动比例应被收敛");
        TestRunner.AssertTrue(config.LogMaxKb >= 64, "日志上限应被收敛");
        TestRunner.AssertEqual(NextKeyNames.PageDown, config.NextKey, "非法按键应回退为 PageDown");
        TestRunner.AssertEqual(KeySendModes.Auto, config.KeySendMode, "非法发送方式应回退为自动");
        TestRunner.AssertEqual(TouchActions.MouseClick, config.TouchAction, "非法轻触动作应回退为“模拟鼠标点击”");
    }

    public static void TouchActionDefaults()
    {
        var config = AppConfig.CreateDefault();
        TestRunner.AssertEqual(TouchActions.MouseClick, config.TouchAction, "默认应把轻触还原为鼠标点击（动画 / 超链接 / 上一页按钮都可用）");

        var clone = config.Clone();
        clone.TouchAction = TouchActions.NextKey;
        TestRunner.AssertEqual(TouchActions.NextKey, clone.TouchAction, "克隆应保留轻触动作设置");
        TestRunner.AssertEqual(TouchActions.MouseClick, config.TouchAction, "克隆后原对象不应被修改");
    }

    public static void MouseClickCoordinateNormalization()
    {
        // 屏幕 0 ~ (size-1) 映射到 0 ~ 65535（多显示器时按整个虚拟桌面换算）
        TestRunner.AssertEqual(0, MouseClickSender.NormalizeCoordinate(0, 0, 1000), "左边界应换算为 0");
        TestRunner.AssertEqual(65535, MouseClickSender.NormalizeCoordinate(999, 0, 1000), "右边界应换算为 65535");

        var middle = MouseClickSender.NormalizeCoordinate(500, 0, 1000);
        TestRunner.AssertTrue(Math.Abs(middle - 32768) < 100, $"中点应接近 32768（实际 {middle}）");

        // 副显示器（起点为负）也要正确换算：其左右边界分别对应 0 与 65535
        TestRunner.AssertEqual(0, MouseClickSender.NormalizeCoordinate(-1920, -1920, 1920), "副显示器左边界应换算为 0");
        TestRunner.AssertEqual(65535, MouseClickSender.NormalizeCoordinate(-1, -1920, 1920), "副显示器右边界应换算为 65535");

        var secondaryMiddle = MouseClickSender.NormalizeCoordinate(-960, -1920, 1920);
        TestRunner.AssertTrue(
            Math.Abs(secondaryMiddle - 32768) < 100,
            $"副显示器中点应接近 32768（实际 {secondaryMiddle}）");

        // 边界外的坐标要被夹紧
        TestRunner.AssertEqual(0, MouseClickSender.NormalizeCoordinate(-5000, 0, 1000), "过小坐标应夹紧到 0");
        TestRunner.AssertEqual(65535, MouseClickSender.NormalizeCoordinate(5000, 0, 1000), "过大坐标应夹紧到 65535");
    }

    public static void DeferredActionWindow()
    {
        var log = TestEnv.CreateLog();
        using var window = new HiddenMessageWindow("BetterClickPPTTestActionWindow", log);
        TestRunner.AssertTrue(window.Create(), "动作窗口应创建成功");
        TestRunner.AssertTrue(window.IsCreated, "动作窗口应处于已创建状态");

        var executed = 0;
        window.Post(() => executed++);
        TestRunner.AssertEqual(0, executed, "刚投递的动作此时还不应执行");

        Pump.Wait(300);
        TestRunner.AssertEqual(1, executed, "抽消息后动作应被执行（说明已从钩子回调挪到消息循环）");

        var order = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var value = i;
            window.Post(() => order.Add(value));
        }

        Pump.Wait(300);
        TestRunner.AssertEqual(5, order.Count, "所有投递的动作都应执行");
        TestRunner.AssertTrue(order.SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "动作应按投递顺序执行");

        window.Dispose();
        TestRunner.AssertTrue(!window.IsCreated, "释放后动作窗口应被销毁");
    }

    public static void InstallStateRead()
    {
        var state = InstallManager.GetState();
        Console.WriteLine($"         安装状态：已安装={state.IsInstalled}，目录={state.InstallDirectory ?? "—"}");

        if (state.IsInstalled)
        {
            TestRunner.AssertTrue(Directory.Exists(state.InstallDirectory), "安装目录应存在");
            TestRunner.AssertTrue(state.ExecutablePath is not null && File.Exists(state.ExecutablePath), "主程序应存在");
        }
    }
}
