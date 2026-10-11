using System.Diagnostics;
using BetterClickPPT.Core;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Logging;
using BetterClickPPT.Core.Office;
using BetterClickPPT.Core.Slideshow;

namespace BetterClickPPT.App;

/// <summary>命令行模式实现（帮助、版本、检测报告、静默安装 / 卸载、清理）。</summary>
internal static partial class CliRunner
{
    /// <summary>执行命令行模式，返回进程退出码。</summary>
    public static int Run(CommandLineOptions options, AppLog log)
    {
        switch (options.Mode)
        {
            case AppMode.Help:
                Console.WriteLine(CommandLineOptions.HelpText);
                return 0;

            case AppMode.Version:
                Console.WriteLine($"{AppPaths.ProductDisplayName} v{GetVersion()}");
                return 0;

            case AppMode.Detect:
                return RunDetect(options);

            case AppMode.Probe:
                return RunProbe();

            case AppMode.Cleanup:
                return UninstallScheduler.RunCleanup(
                    options.CleanupDirectory ?? AppPaths.DefaultInstallDirectory,
                    options.WaitProcessId,
                    options.DeleteSelf,
                    options.DeleteUserData);

            case AppMode.SilentInstall:
                return RunSilentInstall(options, log);

            case AppMode.SilentUninstall:
                return RunSilentUninstall(options, log);

            default:
                return 0;
        }
    }

    /// <summary>输出环境检测报告。</summary>
    private static int RunDetect(CommandLineOptions options)
    {
        TextWriter? fileWriter = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(options.OutputFile))
            {
                fileWriter = new StreamWriter(options.OutputFile, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
            }

            var state = InstallManager.GetState();
            var store = new ConfigStore();
            var config = store.Load();
            var devices = RawInputDeviceEnumerator.Enumerate();
            var apps = OfficeAppDetector.DetectAll();
            var powerPoint = apps.First(a => a.Id == "powerpoint");
            var wps = apps.First(a => a.Id == "wps");

            void Write(string line = "") => ConsoleHelper.WriteLine(line, fileWriter);

            Write($"=== {AppPaths.ProductDisplayName} 环境检测报告 ===");
            Write($"时间        : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Write($"版本        : {GetVersion()}");
            Write($"操作系统    : {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64 位" : "32 位")})");
            Write($"进程位数    : {(Environment.Is64BitProcess ? "64 位" : "32 位")}");
            Write($"配置文件    : {store.FilePath}（{(File.Exists(store.FilePath) ? "存在" : "不存在，将使用默认设置")}）");
            Write($"日志文件    : {AppPaths.LogFile}");

            Write();
            Write("--- 安装状态 ---");
            Write(state.IsInstalled
                ? $"已安装      : 是（{state.InstallDirectory}，版本 {state.Version}，开机自启 {(state.AutoStart ? "已开启" : "未开启")}）"
                : "已安装      : 否");
            Write($"当前程序    : {AppInstanceManager.CurrentExecutablePath}");

            Write();
            Write("--- 目标软件检测 ---");
            Write(powerPoint.IsFound
                ? $"PowerPoint  : 已找到 {powerPoint.ExecutablePath}（{powerPoint.Source}，版本 {powerPoint.Version}）"
                : "PowerPoint  : 未找到（可在安装向导中手动指定 POWERPNT.EXE）");
            Write(wps.IsFound
                ? $"WPS 演示    : 已找到 {wps.ExecutablePath}（{wps.Source}，版本 {wps.Version}）"
                : "WPS 演示    : 未找到（可在安装向导中手动指定 wpp.exe）");

            WriteTouchDevices(Write, devices);
            WriteTargetRules(Write, config);

            Write($"总开关      : {(config.Enabled ? "开启" : "关闭")}，触摸翻页 {(config.AdvanceOnTouch ? "开启" : "关闭")}，"
                  + $"鼠标翻页 {(config.AdvanceOnMouse ? "开启" : "关闭")}，深度触控 {(config.RawTouchEnabled ? "开启" : "关闭")}，"
                  + $"自动切回鼠标模式 {(config.AutoSwitchToMouseMode ? "开启" : "关闭")}");
            Write($"翻页按键    : {config.NextKey}（{config.KeySendMode}）");
            Write();
            Write("=== 报告结束 ===");
            return 0;
        }
        finally
        {
            fileWriter?.Dispose();
        }
    }

    private static void WriteTouchDevices(Action<string> write, IReadOnlyList<RawInputDeviceInfo> devices)
    {
        var touchScreens = devices.Where(d => d.IsTouchScreen).ToList();
        var pens = devices.Where(d => d.IsPen).ToList();
        var touchPads = devices.Where(d => d.IsTouchPad).ToList();

        write(string.Empty);
        write("--- 触摸 / 笔设备 ---");
        write($"触摸屏      : {touchScreens.Count} 个");
        foreach (var device in touchScreens)
        {
            write($"              {device.Name}");
        }

        write($"手写笔      : {pens.Count} 个");
        foreach (var device in pens)
        {
            write($"              {device.Name}");
        }

        write($"触摸板      : {touchPads.Count} 个（本程序不处理触摸板，避免误触）");

        if (touchScreens.Count == 0 && pens.Count == 0)
        {
            write("提示        : 未检测到触摸屏 / 手写笔设备。");
            write("              若本机确实有触摸屏，请确认驱动已安装（设备管理器 → 人机接口设备）。");
            write("              另外，当触摸被系统“提升”为鼠标点击时，即使没有触摸设备也能增强。");
        }
    }

    private static void WriteTargetRules(Action<string> write, AppConfig config)
    {
        write(string.Empty);
        write("--- 放映目标规则 ---");
        foreach (var target in config.Targets)
        {
            var classNames = target.ClassNames.Count > 0 ? string.Join(", ", target.ClassNames) : "不限";
            write($"{(target.Enabled ? "[已启用]" : "[未启用]")} {target.DisplayName}：进程 {string.Join(", ", target.ProcessNames)}，类名 {classNames}，"
                  + $"全屏要求 {(target.RequireFullScreen ? "是" : "否")}，无边框要求 {(target.RequireBorderless ? "是" : "否")}");
        }
    }

    /// <summary>列出与“放映目标规则”相关进程的顶层窗口，便于确认识别结果。</summary>
    private static int RunProbe()
    {
        var config = new ConfigStore().Load();
        var rules = config.Targets.Where(t => t.Enabled).Select(t => t.Clone()).ToArray();
        var detector = new SlideshowDetector(() => rules);

        ConsoleHelper.WriteLine("=== 当前前台窗口 ===");
        var (foreground, foregroundMatch) = detector.DiagnoseForeground();
        if (foreground is null)
        {
            ConsoleHelper.WriteLine("无法获取前台窗口。");
        }
        else
        {
            ConsoleHelper.WriteLine(foreground.ToDiagnosticText());
            ConsoleHelper.WriteLine(foregroundMatch is null
                ? "匹配结果：不匹配"
                : $"匹配结果：命中规则「{foregroundMatch.Rule.DisplayName}」");
            ConsoleHelper.WriteLine(OfficeTouchModeSwitcher.IsTogglePresent(foreground.ProcessId, out var toggleDetail)
                ? $"触摸 / 鼠标模式按钮：可用（{toggleDetail}）→ 放映开始时会自动切回鼠标模式"
                : $"触摸 / 鼠标模式按钮：不可用（{toggleDetail}）");
        }

        ConsoleHelper.WriteLine(string.Empty);
        ConsoleHelper.WriteLine("=== 规则相关进程的顶层窗口 ===");
        var windows = detector.EnumerateRuleWindows();
        if (windows.Count == 0)
        {
            ConsoleHelper.WriteLine("（未发现相关进程的窗口；请先启动 PowerPoint / WPS 并进入放映）");
            return 0;
        }

        foreach (var (probe, match) in windows)
        {
            ConsoleHelper.WriteLine($"[{(match is null ? "未命中" : "已命中")}] {probe.ProcessName} pid={probe.ProcessId} "
                                    + $"类名={probe.ClassName} 标题={probe.Title}");
            ConsoleHelper.WriteLine($"         矩形={probe.Bounds.Left},{probe.Bounds.Top} {probe.Bounds.Width}×{probe.Bounds.Height} "
                                    + $"全屏={probe.IsFullScreen} 标题栏={probe.HasCaption} 可见={probe.IsVisible} 最小化={probe.IsMinimized}");
            if (match is not null)
            {
                ConsoleHelper.WriteLine($"         命中规则：{match.Rule.DisplayName}");
            }
        }

        return 0;
    }

    /// <summary>当前程序版本号。</summary>
    public static string GetVersion()
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(AppInstanceManager.CurrentExecutablePath);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? "1.0.0" : info.FileVersion;
        }
        catch
        {
            return "1.0.0";
        }
    }

    /// <summary>把进度直接写到控制台。</summary>
    private sealed class ConsoleProgress : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => ConsoleHelper.WriteLine($"[{value.Percent,3}%] {value.Message}");
    }
}
