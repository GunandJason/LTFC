using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.App;

/// <summary>静默安装 / 卸载。</summary>
internal static partial class CliRunner
{
    private static int RunSilentInstall(CommandLineOptions options, AppLog log)
    {
        var detected = OfficeAppDetector.DetectAll();
        var powerPoint = detected.First(a => a.Id == "powerpoint");
        var wps = detected.First(a => a.Id == "wps");

        var enablePowerPoint = options.EnablePowerPoint ?? powerPoint.IsFound;
        var enableWps = options.EnableWps ?? wps.IsFound;

        var installOptions = new InstallOptions
        {
            InstallDirectory = options.InstallDirectory ?? AppPaths.DefaultInstallDirectory,
            EnablePowerPoint = enablePowerPoint,
            EnableWps = enableWps,
            PowerPointPath = options.PowerPointPath ?? powerPoint.ExecutablePath,
            WpsPath = options.WpsPath ?? wps.ExecutablePath,
            AutoStart = options.AutoStart ?? true,
            DesktopShortcut = options.DesktopShortcut ?? false,
            StartAfterInstall = options.StartAfterInstall ?? false,
            KeepUserConfig = true
        };

        ConsoleHelper.WriteLine($"安装目录：{installOptions.InstallDirectory}");
        ConsoleHelper.WriteLine($"目标软件：PowerPoint={(enablePowerPoint ? "启用" : "不启用")}，WPS 演示={(enableWps ? "启用" : "不启用")}");
        if (!enablePowerPoint && !enableWps)
        {
            ConsoleHelper.WriteLine("提示：未启用任何目标软件，程序仍会安装，可稍后在设置中启用。");
        }

        var manager = new InstallManager(log);
        var result = manager.Install(installOptions, new ConsoleProgress());
        ConsoleHelper.WriteLine(result.Message);

        if (!result.Success)
        {
            return 1;
        }

        if (installOptions.StartAfterInstall && result.ExecutablePath is not null)
        {
            AppInstanceManager.Start(result.ExecutablePath);
            ConsoleHelper.WriteLine("已启动 BetterClickPPT（托盘运行）。");
        }

        return 0;
    }

    private static int RunSilentUninstall(CommandLineOptions options, AppLog log)
    {
        var manager = new InstallManager(log);
        var result = manager.Uninstall(removeUserData: true, new ConsoleProgress());
        ConsoleHelper.WriteLine(result.Message);
        return result.Success ? 0 : 1;
    }
}
