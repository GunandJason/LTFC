using BetterClickPPT.Core;

namespace BetterClickPPT.App;

/// <summary>程序运行模式。</summary>
internal enum AppMode
{
    /// <summary>自动判断：已安装且从安装目录启动 → 托盘模式；否则 → 安装向导。</summary>
    Auto,

    /// <summary>托盘模式（后台增强）。</summary>
    Tray,

    /// <summary>安装向导（图形界面）。</summary>
    Setup,

    /// <summary>卸载（图形确认界面）。</summary>
    Uninstall,

    /// <summary>静默安装（命令行）。</summary>
    SilentInstall,

    /// <summary>静默卸载（命令行）。</summary>
    SilentUninstall,

    /// <summary>输出环境检测报告（命令行，便于排查问题）。</summary>
    Detect,

    /// <summary>列出相关进程的顶层窗口（命令行，便于排查识别问题）。</summary>
    Probe,

    /// <summary>卸载清理（内部使用）。</summary>
    Cleanup,

    /// <summary>显示帮助。</summary>
    Help,

    /// <summary>显示版本。</summary>
    Version
}

/// <summary>命令行参数解析。</summary>
internal sealed class CommandLineOptions
{
    public AppMode Mode { get; private set; } = AppMode.Auto;

    public bool Verbose { get; private set; }

    public bool Quiet { get; private set; }

    public string? InstallDirectory { get; private set; }

    public bool? AutoStart { get; private set; }

    public bool? DesktopShortcut { get; private set; }

    public bool? StartAfterInstall { get; private set; }

    public bool? EnablePowerPoint { get; private set; }

    public bool? EnableWps { get; private set; }

    public string? PowerPointPath { get; private set; }

    public string? WpsPath { get; private set; }

    public string? CleanupDirectory { get; private set; }

    public int WaitProcessId { get; private set; }

    public bool DeleteSelf { get; private set; }

    public bool DeleteUserData { get; private set; }

    public string? OutputFile { get; private set; }

    /// <summary>启动托盘程序时直接打开设置窗口（--settings）。</summary>
    public bool OpenSettings { get; private set; }

    /// <summary>解析命令行。</summary>
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var (name, inlineValue) = SplitArgument(raw);
            string? TakeValue()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith('-') && !args[i + 1].StartsWith('/'))
                {
                    return args[++i];
                }

                return null;
            }

            switch (name)
            {
                case "--tray":
                    options.Mode = AppMode.Tray;
                    break;
                case "--settings":
                    options.Mode = AppMode.Tray;
                    options.OpenSettings = true;
                    break;
                case "--setup":
                case "--install-gui":
                    options.Mode = AppMode.Setup;
                    break;
                case "--install":
                case "-s":
                case "/s":
                    options.Mode = AppMode.SilentInstall;
                    break;
                case "--uninstall":
                    options.Mode = AppMode.Uninstall;
                    break;
                case "--detect":
                    options.Mode = AppMode.Detect;
                    break;
                case "--probe":
                    options.Mode = AppMode.Probe;
                    break;
                case "--cleanup":
                    options.Mode = AppMode.Cleanup;
                    break;
                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    options.Mode = AppMode.Help;
                    break;
                case "--version":
                    options.Mode = AppMode.Version;
                    break;
                case "--dir":
                    options.InstallDirectory = TakeValue();
                    break;
                case "--ppt":
                    options.EnablePowerPoint = true;
                    break;
                case "--no-ppt":
                    options.EnablePowerPoint = false;
                    break;
                case "--wps":
                    options.EnableWps = true;
                    break;
                case "--no-wps":
                    options.EnableWps = false;
                    break;
                case "--ppt-path":
                    options.PowerPointPath = TakeValue();
                    break;
                case "--wps-path":
                    options.WpsPath = TakeValue();
                    break;
                case "--autostart":
                    options.AutoStart = true;
                    break;
                case "--no-autostart":
                    options.AutoStart = false;
                    break;
                case "--desktop":
                    options.DesktopShortcut = true;
                    break;
                case "--no-desktop":
                    options.DesktopShortcut = false;
                    break;
                case "--start":
                    options.StartAfterInstall = true;
                    break;
                case "--no-start":
                    options.StartAfterInstall = false;
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "--out":
                    options.OutputFile = TakeValue();
                    break;
                case "--wait-pid":
                    options.WaitProcessId = int.TryParse(TakeValue(), out var pid) ? pid : 0;
                    break;
                case "--delete-self":
                    options.DeleteSelf = true;
                    break;
                case "--delete-userdata":
                    options.DeleteUserData = true;
                    break;
            }
        }

        if (options.Mode == AppMode.Uninstall && options.Quiet)
        {
            options.Mode = AppMode.SilentUninstall;
        }

        return options;
    }

    private static (string Name, string? Value) SplitArgument(string raw)
    {
        var index = raw.IndexOf('=');
        if (index <= 0)
        {
            return (raw.ToLowerInvariant(), null);
        }

        return (raw[..index].ToLowerInvariant(), raw[(index + 1)..]);
    }

    /// <summary>帮助文本。</summary>
    public static string HelpText => string.Join(Environment.NewLine, new[]
    {
        AppPaths.ProductDisplayName + "（轻点屏幕翻页）",
        "",
        "用法：BetterClickPPT.exe [选项]",
        "",
        "  （不带参数）          已安装且从安装目录启动时进入托盘模式，否则打开安装向导",
        "  --tray                以托盘模式运行（后台增强，随系统启动时使用）",
        "  --settings            以托盘模式运行并直接打开设置窗口",
        "  --setup               打开安装向导",
        "  --install             静默安装",
        "  --uninstall           卸载（带确认界面）",
        "  --uninstall --quiet   静默卸载",
        "  --detect              输出环境检测报告（触摸屏 / PowerPoint / WPS 检测结果）",
        "  --probe               列出 PowerPoint / WPS 的顶层窗口与识别结果（排查识别问题）",
        "  --help                显示本帮助",
        "  --version             显示版本号",
        "",
        "静默安装附加参数：",
        "  --dir <路径>          安装目录（默认 %LOCALAPPDATA%\\Programs\\BetterClickPPT）",
        "  --ppt / --no-ppt      为 PowerPoint 启用 / 不启用",
        "  --wps / --no-wps      为 WPS 演示启用 / 不启用",
        "  --ppt-path <路径>     指定 PowerPoint 主程序位置",
        "  --wps-path <路径>     指定 WPS 演示主程序位置",
        "  --autostart / --no-autostart   开机自启",
        "  --desktop / --no-desktop       创建桌面快捷方式",
        "  --start / --no-start           安装完成后立即启动",
        "  --out <文件>          把检测报告写入文件",
        "",
        "示例：",
        "  BetterClickPPT.exe --install --ppt --wps --quiet",
        "  BetterClickPPT.exe --install --no-wps --dir \"D:\\Tools\\BetterClickPPT\"",
        "  BetterClickPPT.exe --detect --out report.txt"
    });
}
