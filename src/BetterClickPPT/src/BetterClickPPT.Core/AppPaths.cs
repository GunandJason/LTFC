namespace BetterClickPPT.Core;

/// <summary>
/// 应用使用的所有路径（安装目录、配置、日志等）。
/// 全部位于当前用户目录下，因此安装 / 卸载都不需要管理员权限。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// 产品标识名（安装目录、注册表项、命名事件、互斥体、可执行文件名）。
    /// 改名时保持稳定，避免影响已安装用户的升级 / 卸载。
    /// </summary>
    public const string ProductName = "BetterClickPPT";

    /// <summary>产品显示名（窗口标题、快捷方式、安装 / 卸载界面里显示的名字）。</summary>
    public const string ProductDisplayName = "PPT 触摸增强工具";

    /// <summary>改名前的显示名（仅用于清理历史遗留的快捷方式）。</summary>
    public const string LegacyProductDisplayName = "BetterClickPPT 触控点击翻页增强器";

    /// <summary>安装在“应用和功能”中显示的卸载项注册表路径。</summary>
    public const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BetterClickPPT";

    /// <summary>安装信息注册表路径（安装目录、版本等）。</summary>
    public const string InstallRegistryKey = @"Software\BetterClickPPT";

    /// <summary>开机自启注册表路径（HKCU）。</summary>
    public const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>开机自启注册表值名。</summary>
    public const string RunValueName = "BetterClickPPT";

    /// <summary>主程序文件名。</summary>
    public const string MainExecutableName = "BetterClickPPT.exe";

    /// <summary>已安装程序退出时使用的命名事件（安装 / 卸载前用它优雅地关闭正在运行的实例）。</summary>
    public const string ShutdownEventName = @"Local\BetterClickPPT.Shutdown";

    /// <summary>单实例互斥体名称。</summary>
    public const string SingleInstanceMutexName = @"Local\BetterClickPPT.SingleInstance";

    /// <summary>数据根目录：%LOCALAPPDATA%\BetterClickPPT。</summary>
    public static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductName);

    /// <summary>配置文件：%LOCALAPPDATA%\BetterClickPPT\config.json。</summary>
    public static string ConfigFile => Path.Combine(DataRoot, "config.json");

    /// <summary>日志目录。</summary>
    public static string LogDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>日志文件。</summary>
    public static string LogFile => Path.Combine(LogDirectory, "betterclickppt.log");

    /// <summary>默认安装目录：%LOCALAPPDATA%\Programs\BetterClickPPT。</summary>
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        ProductName);

    /// <summary>开始菜单快捷方式目录（当前用户）：用显示名命名，方便在开始菜单里认出来。</summary>
    public static string StartMenuDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        ProductDisplayName);

    /// <summary>改名前的开始菜单目录（仅用于清理历史遗留的快捷方式）。</summary>
    public static string LegacyStartMenuDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        ProductName);

    /// <summary>桌面目录。</summary>
    public static string DesktopDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>确保目录存在。</summary>
    public static void EnsureDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}
