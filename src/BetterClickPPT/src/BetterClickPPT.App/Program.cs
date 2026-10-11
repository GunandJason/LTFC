using BetterClickPPT.App.Setup;
using BetterClickPPT.App.Tray;
using BetterClickPPT.App.Ui;
using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.App;

/// <summary>程序入口。</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = CommandLineOptions.Parse(args);

        // 命令行模式：输出到父控制台，便于脚本与排查。
        if (options.Mode is AppMode.Help or AppMode.Version or AppMode.Detect or AppMode.Probe or AppMode.Cleanup
            or AppMode.SilentInstall or AppMode.SilentUninstall)
        {
            ConsoleHelper.AttachToParent();
            var cliLog = new AppLog(verbose: options.Verbose);

            try
            {
                return CliRunner.Run(options, cliLog);
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteLine("执行失败：" + ex.Message);
                return 1;
            }
        }

        var log = new AppLog();

        try
        {
            switch (options.Mode)
            {
                case AppMode.Setup:
                    return RunSetupWizard(log, options);
                case AppMode.Uninstall:
                    return RunUninstallFlow(log, options);
                case AppMode.Tray:
                    return RunTray(log, options.OpenSettings);
                default:
                    return RunAuto(log, options);
            }
        }
        catch (Exception ex)
        {
            log.Error("程序异常退出", ex);
            MessageBox.Show(
                $"程序发生异常：{ex.Message}{Environment.NewLine}{Environment.NewLine}详细日志：{log.FilePath}",
                AppPaths.ProductDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>自动判断运行模式。</summary>
    private static int RunAuto(AppLog log, CommandLineOptions options)
    {
        var state = InstallManager.GetState();
        var runningFromInstallDirectory = state.IsInstalled
            && AppInstanceManager.CurrentExecutablePath.StartsWith(state.InstallDirectory!, StringComparison.OrdinalIgnoreCase);

        log.Info($"自动模式：已安装={state.IsInstalled}，从安装目录启动={runningFromInstallDirectory}");
        return runningFromInstallDirectory ? RunTray(log) : RunSetupWizard(log, options);
    }

    /// <summary>托盘模式（单实例）。</summary>
    private static int RunTray(AppLog log, bool openSettings = false)
    {
        using var mutex = new Mutex(initiallyOwned: true, AppPaths.SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            log.Info("检测到已有实例在运行，本次启动直接退出。");
            return 0;
        }

        using var context = new TrayApplicationContext(log, openSettings);
        Application.Run(context);
        return 0;
    }

    /// <summary>安装向导。</summary>
    private static int RunSetupWizard(AppLog log, CommandLineOptions options)
    {
        using var form = new SetupWizardForm(log, options);
        Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>卸载（带确认与进度）。</summary>
    private static int RunUninstallFlow(AppLog log, CommandLineOptions options)
    {
        var state = InstallManager.GetState();
        if (!state.IsInstalled)
        {
            MessageBox.Show(
                "未检测到已安装的 " + AppPaths.ProductDisplayName + "。",
                "卸载",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        if (!options.Quiet)
        {
            var answer = MessageBox.Show(
                $"确定要卸载 {AppPaths.ProductDisplayName} 吗？{Environment.NewLine}{Environment.NewLine}"
                + $"安装目录：{state.InstallDirectory}{Environment.NewLine}"
                + $"设置与日志（{AppPaths.DataRoot}）也会一并删除。",
                "卸载确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                return 0;
            }
        }

        var manager = new InstallManager(log);
        using var dialog = new ProgressDialog("正在卸载", "正在删除程序文件与注册表信息…");
        dialog.Show();

        InstallResult result;
        try
        {
            result = UiRunner.RunWithPump(() => manager.Uninstall(removeUserData: true, dialog.Progress), dialog.Pump);
        }
        finally
        {
            dialog.Close();
        }

        MessageBox.Show(
            result.Message,
            "卸载",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        return result.Success ? 0 : 1;
    }
}
