using BetterClickPPT.Core;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Deployment;

namespace BetterClickPPT.SelfTest;

/// <summary>安装 / 卸载流程测试（使用临时安装目录，测试前后会备份用户配置）。</summary>
internal static class InstallTests
{
    public static void InstallAndUninstallRoundTrip()
    {
        var installDirectory = Path.Combine(Path.GetTempPath(), "bcpp-install-" + Guid.NewGuid().ToString("N")[..8]);
        var result = BackupDataRoot(out var backupDirectory);

        try
        {
            var progress = new List<string>();
            var manager = new InstallManager();

            var options = new InstallOptions
            {
                InstallDirectory = installDirectory,
                EnablePowerPoint = true,
                EnableWps = false,
                PowerPointPath = @"C:\fake\POWERPNT.EXE",
                WpsPath = null,
                AutoStart = false,
                DesktopShortcut = false,
                StartAfterInstall = false,
                KeepUserConfig = false
            };

            var installResult = manager.Install(options, new Progress<InstallProgress>(p => progress.Add($"{p.Percent}% {p.Message}")));

            TestRunner.AssertTrue(installResult.Success, "安装应成功：" + installResult.Message);
            TestRunner.AssertEqual(installDirectory, installResult.InstallDirectory, "安装目录应为指定目录");
            TestRunner.AssertTrue(installResult.ExecutablePath is not null && File.Exists(installResult.ExecutablePath), "主程序应已复制");

            var state = InstallManager.GetState();
            TestRunner.AssertTrue(state.IsInstalled, "注册表应记录已安装");
            TestRunner.AssertEqual(installDirectory, state.InstallDirectory, "注册表安装目录应正确");

            // 配置：PowerPoint 启用、WPS 关闭
            var config = new ConfigStore().Load();
            TestRunner.AssertTrue(config.Targets.First(t => t.Id == "powerpoint").Enabled, "PowerPoint 应被启用");
            TestRunner.AssertTrue(!config.Targets.First(t => t.Id == "wps").Enabled, "WPS 应保持关闭");
            TestRunner.AssertEqual(@"C:\fake\POWERPNT.EXE", config.Targets.First(t => t.Id == "powerpoint").ExePath, "应记录 PowerPoint 路径");

            // 快捷方式 / 自启
            TestRunner.AssertTrue(File.Exists(ShortcutHelper.GetStartMenuLinkPath()), "开始菜单快捷方式应创建");
            TestRunner.AssertTrue(!File.Exists(ShortcutHelper.GetDesktopLinkPath()), "未勾选时不应创建桌面快捷方式");
            TestRunner.AssertTrue(!AutoStartManager.IsEnabled(), "未勾选时不应写入开机自启");

            // 重复安装（升级）应成功
            var upgradeResult = manager.Install(options);
            TestRunner.AssertTrue(upgradeResult.Success, "重复安装应成功（覆盖升级）");

            // 卸载
            var uninstallResult = manager.Uninstall(removeUserData: true);
            TestRunner.AssertTrue(uninstallResult.Success, "卸载应成功：" + uninstallResult.Message);
            TestRunner.AssertTrue(!File.Exists(Path.Combine(installDirectory, AppPaths.MainExecutableName)), "主程序应被删除");
            TestRunner.AssertTrue(!Directory.Exists(installDirectory), "安装目录应被删除");
            TestRunner.AssertTrue(!File.Exists(ShortcutHelper.GetStartMenuLinkPath()), "快捷方式应被删除");
            TestRunner.AssertTrue(!InstallManager.GetState().IsInstalled, "卸载后不应再显示已安装");

            Console.WriteLine($"         安装 / 升级 / 卸载共 {progress.Count} 条进度消息。");
        }
        finally
        {
            RestoreDataRoot(backupDirectory, result);
            TestEnv.TryDeleteDirectory(installDirectory);
        }
    }

    /// <summary>备份用户数据目录，避免测试影响真实配置。</summary>
    private static bool BackupDataRoot(out string backupDirectory)
    {
        backupDirectory = AppPaths.DataRoot + ".selftest-backup";
        TestEnv.TryDeleteDirectory(backupDirectory);

        if (!Directory.Exists(AppPaths.DataRoot))
        {
            return false;
        }

        Directory.Move(AppPaths.DataRoot, backupDirectory);
        return true;
    }

    /// <summary>恢复用户数据目录。</summary>
    private static void RestoreDataRoot(string backupDirectory, bool hadData)
    {
        TestEnv.TryDeleteDirectory(AppPaths.DataRoot);

        if (hadData && Directory.Exists(backupDirectory))
        {
            try
            {
                Directory.Move(backupDirectory, AppPaths.DataRoot);
            }
            catch
            {
                // 忽略恢复失败。
            }
        }
    }
}
