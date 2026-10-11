using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Slideshow;

namespace BetterClickPPT.SelfTest;

/// <summary>放映窗口识别相关的单元测试。</summary>
internal static class UnitTestsSlideshow
{
    public static void SlideshowRuleMatching()
    {
        var powerPointRule = AppConfig.CreateDefault().Targets.First(t => t.Id == "powerpoint");

        var showWindow = new WindowProbe(
            new IntPtr(0x1234),
            "screenClass",
            "PowerPoint 幻灯片放映 - [demo.pptx]",
            1234,
            "POWERPNT.EXE",
            new System.Drawing.Rectangle(0, 0, 1920, 1080),
            new System.Drawing.Rectangle(0, 0, 1920, 1080),
            IsFullScreen: true,
            HasCaption: false,
            IsVisible: true,
            IsMinimized: false);

        TestRunner.AssertTrue(SlideshowDetector.Matches(powerPointRule, showWindow), "应命中 PowerPoint 全屏放映窗口");

        var editWindow = showWindow with
        {
            ClassName = "PPTFrameClass",
            HasCaption = true,
            IsFullScreen = false,
            Bounds = new System.Drawing.Rectangle(0, 0, 1920, 1000)
        };
        TestRunner.AssertTrue(!SlideshowDetector.Matches(powerPointRule, editWindow), "最大化的编辑窗口不应命中");

        var otherProcess = showWindow with { ProcessName = "notepad.exe", ClassName = "Notepad" };
        TestRunner.AssertTrue(!SlideshowDetector.Matches(powerPointRule, otherProcess), "其它进程不应命中");

        var disabled = powerPointRule.Clone();
        disabled.Enabled = false;
        TestRunner.AssertTrue(!SlideshowDetector.Matches(disabled, showWindow), "禁用的规则不应命中");

        // WPS 规则不限制窗口类名，只要求全屏 + 无边框
        var wpsRule = AppConfig.CreateDefault().Targets.First(t => t.Id == "wps");
        var wpsShow = showWindow with { ProcessName = "wpp.exe", ClassName = "wppShowWnd" };
        TestRunner.AssertTrue(SlideshowDetector.Matches(wpsRule, wpsShow), "WPS 全屏放映窗口应命中");
        TestRunner.AssertTrue(
            !SlideshowDetector.Matches(wpsRule, wpsShow with { IsFullScreen = false, HasCaption = true }),
            "WPS 的普通窗口不应命中");

        // 自定义规则 + 通配符
        var custom = new TargetRule
        {
            Id = "custom",
            Enabled = true,
            ProcessNames = new List<string> { "*player.exe" },
            RequireFullScreen = false,
            RequireBorderless = false
        };
        TestRunner.AssertTrue(
            SlideshowDetector.Matches(custom, showWindow with { ProcessName = "myplayer.exe", IsFullScreen = false, HasCaption = true }),
            "自定义通配符规则应命中");
    }

    public static void OfficeAppDetection()
    {
        var apps = OfficeAppDetector.DetectAll();
        TestRunner.AssertEqual(2, apps.Count, "应返回两条检测结果");

        foreach (var app in apps)
        {
            if (app.IsFound)
            {
                TestRunner.AssertTrue(File.Exists(app.ExecutablePath), $"检测到的路径应存在：{app.ExecutablePath}");
                Console.WriteLine($"         {app.DisplayName} → {app.ExecutablePath}（{app.Source}，{app.Version}）");
            }
            else
            {
                Console.WriteLine($"         {app.DisplayName} → 未检测到（本机未安装，属正常情况）");
            }
        }
    }
}
