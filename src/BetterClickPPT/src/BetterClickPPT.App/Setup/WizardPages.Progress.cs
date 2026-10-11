using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;

namespace BetterClickPPT.App.Setup;

/// <summary>步骤 4：安装进度。</summary>
internal sealed class ProgressPage : WizardPage
{
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly TextBox _logBox = new();

    public override string Title => "正在安装";

    public override string Subtitle => "正在复制程序、写入配置与注册信息";

    public override string NextText => "下一步";

    public ProgressPage()
    {
        _statusLabel.Location = new Point(16, 10);
        _statusLabel.Size = new Size(580, 24);
        _statusLabel.Text = "准备中…";
        Controls.Add(_statusLabel);

        _progressBar.Location = new Point(16, 40);
        _progressBar.Size = new Size(580, 22);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Style = ProgressBarStyle.Continuous;
        Controls.Add(_progressBar);

        _logBox.Location = new Point(16, 74);
        _logBox.Size = new Size(580, 250);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.BackColor = Color.FromArgb(248, 250, 252);
        _logBox.Font = new Font("Consolas", 9f);
        Controls.Add(_logBox);

        Progress = new Progress<InstallProgress>(OnProgress);
    }

    /// <summary>进度回调（会由消息泵切换到界面线程执行）。</summary>
    public IProgress<InstallProgress> Progress { get; }

    /// <summary>重置界面。</summary>
    public void Reset()
    {
        _progressBar.Value = 0;
        _statusLabel.Text = "准备中…";
        _logBox.Clear();
    }

    /// <summary>刷新界面（消息泵回调）。</summary>
    public void Pump() => Application.DoEvents();

    /// <summary>追加日志。</summary>
    public void AppendLog(string message)
    {
        _logBox.AppendText(message + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    /// <summary>安装结束。</summary>
    public void Complete(InstallResult result)
    {
        _statusLabel.Text = result.Success ? "安装成功。" : "安装失败：" + result.Message;
        if (result.Success)
        {
            _progressBar.Value = 100;
        }

        AppendLog(result.Success ? "完成。" : "出现错误，详见日志：" + AppPaths.LogFile);
    }

    private void OnProgress(InstallProgress progress)
    {
        _progressBar.Value = Math.Clamp(progress.Percent, 0, 100);
        _statusLabel.Text = progress.Message;
        AppendLog($"[{progress.Percent,3}%] {progress.Message}");
    }
}

/// <summary>步骤 5：完成。</summary>
internal sealed class FinishPage : WizardPage
{
    private readonly Label _summaryLabel = new();
    private readonly Label _targetsLabel = new();
    private readonly Button _launchButton = new();

    public override string Title => "安装完成";

    public override string Subtitle => "现在可以在放映时轻点屏幕翻页了";

    public override string NextText => "完成";

    public FinishPage()
    {
        _summaryLabel.Location = new Point(16, 8);
        _summaryLabel.Size = new Size(580, 48);
        Controls.Add(_summaryLabel);

        _targetsLabel.Location = new Point(16, 58);
        _targetsLabel.Size = new Size(580, 44);
        _targetsLabel.ForeColor = Color.FromArgb(71, 85, 105);
        Controls.Add(_targetsLabel);

        var steps = new ListBox
        {
            Location = new Point(16, 110),
            Size = new Size(580, 116),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 250, 252),
            SelectionMode = SelectionMode.None,
            IntegralHeight = false
        };
        steps.Items.Add("1. 打开 PowerPoint 或 WPS 演示，按 F5 开始全屏放映。");
        steps.Items.Add("2. 用手指轻触屏幕任意位置 → 直接翻到下一页。");
        steps.Items.Add("3. 原有的滑动翻页、双指缩放、长按等操作仍然有效。");
        steps.Items.Add("4. 托盘图标（右下角）右键可暂停增强、打开设置或查看诊断。");
        Controls.Add(steps);

        _launchButton.Text = "启动程序";
        _launchButton.Location = new Point(16, 238);
        _launchButton.Size = new Size(150, 30);
        _launchButton.Click += (_, _) => LaunchAndHint();
        Controls.Add(_launchButton);

        var openFolderButton = new Button { Text = "打开安装目录", Location = new Point(176, 238), Size = new Size(130, 30) };
        openFolderButton.Click += (_, _) => OpenInstalledDirectory();
        Controls.Add(openFolderButton);

        Controls.Add(CreateHint(
            "如果发现“点一下翻了两页”，请打开设置 → 翻页行为，确认已勾选“吞掉触摸原始点击”。\r\n"
            + "如果点击屏幕仍然没有反应，请在设置中开启“深度触控模式”，或用 --detect 生成检测报告。",
            16, 280, 580, 44));
    }

    public override void OnEnter(SetupModel model)
    {
        var result = model.Result;

        _summaryLabel.Text = result is null
            ? "安装流程已结束。"
            : (result.Success ? "✔  " + result.Message : "✘  " + result.Message);
        _summaryLabel.ForeColor = result?.Success == true ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28);

        var targets = (model.EnablePowerPoint ? "Microsoft PowerPoint  " : string.Empty)
                    + (model.EnableWps ? "WPS 演示" : string.Empty);
        if (string.IsNullOrWhiteSpace(targets))
        {
            targets = "（未启用任何软件，可在设置中开启）";
        }

        _targetsLabel.Text = "已启用：" + targets + Environment.NewLine
                           + "安装位置：" + (result?.InstallDirectory ?? model.InstallDirectory);

        _launchButton.Enabled = result?.Success == true;
    }

    private static void LaunchAndHint()
    {
        var state = InstallManager.GetState();
        if (state.ExecutablePath is not null)
        {
            AppInstanceManager.Start(state.ExecutablePath);
        }

        MessageBox.Show(
            "程序已启动并常驻托盘。\r\n右键托盘图标 →「设置…」可打开设置窗口。",
            AppPaths.ProductName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void OpenInstalledDirectory()
    {
        try
        {
            var state = InstallManager.GetState();
            var directory = state.InstallDirectory ?? AppPaths.DefaultInstallDirectory;
            AppPaths.EnsureDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开失败：" + ex.Message, AppPaths.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
