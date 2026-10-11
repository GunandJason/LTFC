using System.Diagnostics;
using Microsoft.Win32;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Deployment;

/// <summary>安装选项。</summary>
public sealed class InstallOptions
{
    /// <summary>安装目录。</summary>
    public string InstallDirectory { get; set; } = AppPaths.DefaultInstallDirectory;

    /// <summary>开机自动启动。</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>创建桌面快捷方式。</summary>
    public bool DesktopShortcut { get; set; } = true;

    /// <summary>安装完成后立即启动。</summary>
    public bool StartAfterInstall { get; set; } = true;

    /// <summary>为 Microsoft PowerPoint 启用触摸翻页。</summary>
    public bool EnablePowerPoint { get; set; } = true;

    /// <summary>为 WPS 演示启用触摸翻页。</summary>
    public bool EnableWps { get; set; }

    /// <summary>PowerPoint 主程序路径（写入配置，便于诊断显示）。</summary>
    public string? PowerPointPath { get; set; }

    /// <summary>WPS 演示主程序路径。</summary>
    public string? WpsPath { get; set; }

    /// <summary>保留已有用户设置（升级安装）。</summary>
    public bool KeepUserConfig { get; set; } = true;

    /// <summary>要复制的程序文件（默认为当前进程自身）。</summary>
    public string? SourceExecutable { get; set; }
}

/// <summary>安装 / 卸载进度。</summary>
public sealed record InstallProgress(string Message, int Percent);

/// <summary>安装结果。</summary>
public sealed record InstallResult(bool Success, string Message, string? InstallDirectory, string? ExecutablePath);

/// <summary>当前安装状态。</summary>
public sealed record InstallState(bool IsInstalled, string? InstallDirectory, string? Version, bool AutoStart)
{
    public static InstallState NotInstalled { get; } = new(false, null, null, false);

    /// <summary>已安装程序路径。</summary>
    public string? ExecutablePath => string.IsNullOrWhiteSpace(InstallDirectory)
        ? null
        : Path.Combine(InstallDirectory, AppPaths.MainExecutableName);
}

/// <summary>安装 / 卸载管理器。</summary>
public sealed partial class InstallManager
{
    private readonly AppLog? _log;

    public InstallManager(AppLog? log = null)
    {
        _log = log;
    }

    /// <summary>读取当前安装状态。</summary>
    public static InstallState GetState()
    {
        var installDirectory = RegistryUtil.ReadString(
            RegistryHive.CurrentUser,
            RegistryView.Default,
            AppPaths.InstallRegistryKey,
            "InstallDir");

        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return InstallState.NotInstalled;
        }

        var executable = Path.Combine(installDirectory, AppPaths.MainExecutableName);
        if (!File.Exists(executable))
        {
            return InstallState.NotInstalled;
        }

        var version = RegistryUtil.ReadString(RegistryHive.CurrentUser, RegistryView.Default, AppPaths.InstallRegistryKey, "Version");
        return new InstallState(true, installDirectory, version, AutoStartManager.IsEnabled(installDirectory));
    }

    /// <summary>执行安装。</summary>
    public InstallResult Install(InstallOptions options, IProgress<InstallProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var installDirectory = string.IsNullOrWhiteSpace(options.InstallDirectory)
            ? AppPaths.DefaultInstallDirectory
            : Path.GetFullPath(options.InstallDirectory);
        var targetExecutable = Path.Combine(installDirectory, AppPaths.MainExecutableName);

        try
        {
            var source = string.IsNullOrWhiteSpace(options.SourceExecutable)
                ? AppInstanceManager.CurrentExecutablePath
                : options.SourceExecutable!;

            Report(progress, "关闭正在运行的旧版本…", 8);
            var stopped = AppInstanceManager.StopAll(installDirectory);
            if (stopped > 0)
            {
                Thread.Sleep(400);
            }

            Report(progress, "复制程序文件…", 25);
            AppPaths.EnsureDirectory(installDirectory);
            CopyProgramFiles(source, installDirectory, targetExecutable);

            Report(progress, "写入配置（按所选目标软件启用）…", 45);
            WriteConfig(options);

            Report(progress, options.AutoStart ? "设置开机自动启动…" : "关闭开机自动启动…", 60);
            if (options.AutoStart)
            {
                AutoStartManager.Enable(targetExecutable);
            }
            else
            {
                AutoStartManager.Disable();
            }

            Report(progress, "创建快捷方式…", 74);
            ShortcutHelper.Create(
                ShortcutHelper.GetStartMenuLinkPath(),
                targetExecutable,
                "--tray",
                installDirectory,
                AppPaths.ProductDisplayName);

            if (options.DesktopShortcut)
            {
                ShortcutHelper.Create(
                    ShortcutHelper.GetDesktopLinkPath(),
                    targetExecutable,
                    "--tray",
                    installDirectory,
                    AppPaths.ProductDisplayName);
            }
            else
            {
                ShortcutHelper.Delete(ShortcutHelper.GetDesktopLinkPath());
            }

            // 改名后清理旧快捷方式（否则桌面上会出现两个图标）。
            ShortcutHelper.CleanupLegacyLinks();

            Report(progress, "注册卸载信息…", 88);
            WriteInstallRegistry(installDirectory, options);
            WriteUninstallRegistry(installDirectory, targetExecutable);

            Report(progress, "安装完成", 100);
            _log?.Info($"安装成功：{installDirectory}");
            return new InstallResult(true, "安装成功", installDirectory, targetExecutable);
        }
        catch (Exception ex)
        {
            _log?.Error("安装失败", ex);
            return new InstallResult(false, "安装失败：" + ex.Message, installDirectory, targetExecutable);
        }
    }

    internal static void Report(IProgress<InstallProgress>? progress, string message, int percent)
        => progress?.Report(new InstallProgress(message, percent));

    /// <summary>
    /// 复制程序文件：单文件发布时只复制自身；多文件部署（例如开发期输出目录）时整体复制，
    /// 保证安装目录里的程序可以直接运行。
    /// </summary>
    private static void CopyProgramFiles(string source, string installDirectory, string targetExecutable)
    {
        var sourceDirectory = Path.GetDirectoryName(source);
        var files = !string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory)
            ? Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();

        var sameDirectory = IsSamePath(sourceDirectory, installDirectory);

        if (files.Length <= 1 || sameDirectory)
        {
            if (!IsSamePath(source, targetExecutable))
            {
                File.Copy(source, targetExecutable, overwrite: true);
            }

            return;
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDirectory!, file);
            var destination = Path.Combine(installDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        if (!File.Exists(targetExecutable))
        {
            File.Copy(source, targetExecutable, overwrite: true);
        }
    }

    private static bool IsSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void WriteConfig(InstallOptions options)
    {
        var store = new ConfigStore(AppPaths.ConfigFile, _log);
        var config = options.KeepUserConfig && File.Exists(store.FilePath)
            ? store.Load()
            : AppConfig.CreateDefault();

        config.Enabled = true;
        config.AutoStart = options.AutoStart;

        foreach (var target in config.Targets)
        {
            if (string.Equals(target.Id, "powerpoint", StringComparison.OrdinalIgnoreCase))
            {
                target.Enabled = options.EnablePowerPoint;
                if (!string.IsNullOrWhiteSpace(options.PowerPointPath))
                {
                    target.ExePath = options.PowerPointPath;
                }
            }
            else if (string.Equals(target.Id, "wps", StringComparison.OrdinalIgnoreCase))
            {
                target.Enabled = options.EnableWps;
                if (!string.IsNullOrWhiteSpace(options.WpsPath))
                {
                    target.ExePath = options.WpsPath;
                }
            }
        }

        store.Save(config);
    }

    private void WriteInstallRegistry(string installDirectory, InstallOptions options)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPaths.InstallRegistryKey, writable: true);
        if (key is null)
        {
            return;
        }

        key.SetValue("InstallDir", installDirectory, RegistryValueKind.String);
        key.SetValue("Version", GetProductVersion(), RegistryValueKind.String);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), RegistryValueKind.String);
        key.SetValue("TargetPowerPoint", options.EnablePowerPoint ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("TargetWps", options.EnableWps ? 1 : 0, RegistryValueKind.DWord);
    }

    private void WriteUninstallRegistry(string installDirectory, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPaths.UninstallRegistryKey, writable: true);
        if (key is null)
        {
            return;
        }

        key.SetValue("DisplayName", AppPaths.ProductDisplayName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", GetProductVersion(), RegistryValueKind.String);
        key.SetValue("Publisher", "BetterClickPPT", RegistryValueKind.String);
        key.SetValue("DisplayIcon", executablePath, RegistryValueKind.String);
        key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{executablePath}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{executablePath}\" --uninstall --quiet", RegistryValueKind.String);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateSizeKb(installDirectory), RegistryValueKind.DWord);
    }

    private static int EstimateSizeKb(string directory)
    {
        try
        {
            var total = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f).Length)
                .Sum();
            return (int)Math.Max(1, total / 1024);
        }
        catch
        {
            return 1024;
        }
    }

    private static string GetProductVersion()
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
}
