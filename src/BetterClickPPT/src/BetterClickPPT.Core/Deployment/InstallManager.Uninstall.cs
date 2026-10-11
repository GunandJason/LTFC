using Microsoft.Win32;

namespace BetterClickPPT.Core.Deployment;

/// <summary>安装 / 卸载管理器（卸载部分）。</summary>
public sealed partial class InstallManager
{
    /// <summary>执行卸载。安装目录内运行时会安排“退出后自动删除”。</summary>
    public InstallResult Uninstall(bool removeUserData, IProgress<InstallProgress>? progress = null)
    {
        var state = GetState();
        var installDirectory = state.InstallDirectory ?? AppPaths.DefaultInstallDirectory;

        try
        {
            Report(progress, "关闭正在运行的程序…", 10);
            AppInstanceManager.StopAll(installDirectory);
            Thread.Sleep(200);

            Report(progress, "关闭开机自动启动…", 25);
            AutoStartManager.Disable();

            Report(progress, "删除快捷方式…", 40);
            ShortcutHelper.Delete(ShortcutHelper.GetStartMenuLinkPath());
            ShortcutHelper.Delete(ShortcutHelper.GetDesktopLinkPath());
            ShortcutHelper.CleanupLegacyLinks();
            RemoveStartMenuFolderIfEmpty();

            Report(progress, "删除注册表信息…", 55);
            DeleteRegistryKeys();

            var runningFromInstallDirectory = AppInstanceManager.CurrentExecutablePath
                .StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase);

            Report(progress, "删除程序文件…", 75);
            if (runningFromInstallDirectory)
            {
                UninstallScheduler.ScheduleCleanup(installDirectory, removeUserData, _log);
                Report(progress, "卸载完成", 100);
                _log?.Info("卸载完成，程序文件将在程序退出后自动删除");
                return new InstallResult(true, "卸载完成，程序文件将在程序退出后自动删除。", installDirectory, null);
            }

            if (removeUserData)
            {
                TryDeleteDirectory(AppPaths.DataRoot);
            }

            TryDeleteDirectory(installDirectory);
            Report(progress, "卸载完成", 100);
            _log?.Info($"卸载完成：{installDirectory}");
            return new InstallResult(true, "卸载完成", installDirectory, null);
        }
        catch (Exception ex)
        {
            _log?.Error("卸载失败", ex);
            return new InstallResult(false, "卸载失败：" + ex.Message, installDirectory, null);
        }
    }

    private static void DeleteRegistryKeys()
    {
        TryDeleteRegistryKey(AppPaths.UninstallRegistryKey);
        TryDeleteRegistryKey(AppPaths.InstallRegistryKey);
    }

    private static void TryDeleteRegistryKey(string subKeyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // 忽略删除失败。
        }
    }

    private static void RemoveStartMenuFolderIfEmpty()
    {
        try
        {
            var folder = AppPaths.StartMenuDirectory;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch
        {
            // 忽略。
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // 忽略（可能被占用）。
        }
    }
}
