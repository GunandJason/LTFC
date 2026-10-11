using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;

namespace BetterClickPPT.App.Setup;

/// <summary>安装向导的数据模型。</summary>
internal sealed class SetupModel
{
    /// <summary>检测到的 PowerPoint。</summary>
    public DetectedApp PowerPoint { get; set; } = DetectedApp.NotFound("powerpoint", "Microsoft PowerPoint");

    /// <summary>检测到的 WPS 演示。</summary>
    public DetectedApp Wps { get; set; } = DetectedApp.NotFound("wps", "WPS 演示");

    public bool EnablePowerPoint { get; set; }

    public bool EnableWps { get; set; }

    public string InstallDirectory { get; set; } = AppPaths.DefaultInstallDirectory;

    public bool AutoStart { get; set; } = true;

    public bool DesktopShortcut { get; set; } = true;

    public bool StartAfterInstall { get; set; } = true;

    public bool IsUpgrade { get; set; }

    public InstallState ExistingState { get; set; } = InstallState.NotInstalled;

    public InstallResult? Result { get; set; }

    /// <summary>根据命令行参数与当前系统状态创建模型。</summary>
    public static SetupModel Create(CommandLineOptions options)
    {
        var model = new SetupModel
        {
            ExistingState = InstallManager.GetState()
        };

        model.IsUpgrade = model.ExistingState.IsInstalled;
        if (model.IsUpgrade && !string.IsNullOrWhiteSpace(model.ExistingState.InstallDirectory))
        {
            model.InstallDirectory = model.ExistingState.InstallDirectory!;
        }

        model.Redetect(options);

        if (!string.IsNullOrWhiteSpace(options.InstallDirectory))
        {
            model.InstallDirectory = options.InstallDirectory!;
        }

        model.AutoStart = options.AutoStart ?? true;
        model.DesktopShortcut = options.DesktopShortcut ?? true;
        model.StartAfterInstall = options.StartAfterInstall ?? true;
        return model;
    }

    /// <summary>重新自动检测目标软件，并按命令行参数设置默认勾选状态。</summary>
    public void Redetect(CommandLineOptions? options = null)
    {
        PowerPoint = OfficeAppDetector.DetectPowerPoint();
        Wps = OfficeAppDetector.DetectWpsPresentation();

        EnablePowerPoint = options?.EnablePowerPoint ?? PowerPoint.IsFound;
        EnableWps = options?.EnableWps ?? Wps.IsFound;
    }

    /// <summary>转换为安装选项。</summary>
    public InstallOptions ToInstallOptions() => new()
    {
        InstallDirectory = InstallDirectory,
        EnablePowerPoint = EnablePowerPoint,
        EnableWps = EnableWps,
        PowerPointPath = PowerPoint.ExecutablePath,
        WpsPath = Wps.ExecutablePath,
        AutoStart = AutoStart,
        DesktopShortcut = DesktopShortcut,
        StartAfterInstall = StartAfterInstall,
        KeepUserConfig = IsUpgrade,
        SourceExecutable = AppInstanceManager.CurrentExecutablePath
    };
}
