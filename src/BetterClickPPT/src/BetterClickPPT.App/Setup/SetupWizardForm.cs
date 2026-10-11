using BetterClickPPT.App.Ui;
using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.App.Setup;

/// <summary>安装向导主窗口。</summary>
internal sealed partial class SetupWizardForm : Form
{
    private readonly AppLog _log;
    private readonly CommandLineOptions _options;
    private readonly SetupModel _model;
    private readonly List<WizardPage> _pages = new();

    private Panel _contentPanel = null!;
    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;
    private Button _cancelButton = null!;
    private Label _stepLabel = null!;

    private int _index = -1;
    private bool _installing;
    private bool _finished;

    public SetupWizardForm(AppLog log, CommandLineOptions options)
    {
        _log = log;
        _options = options;
        _model = SetupModel.Create(options);

        InitializeUi();
        ShowPage(0);
    }

    /// <summary>退出码（0 表示成功）。</summary>
    public int ExitCode { get; private set; }

    private void InitializeUi()
    {
        Text = AppPaths.ProductDisplayName + " - 安装向导";
        ClientSize = new Size(660, 530);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = IconFactory.AppIcon;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        var header = new GradientHeaderPanel { Dock = DockStyle.Top, Height = 78 };
        _titleLabel = new Label
        {
            Text = AppPaths.ProductDisplayName,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 12)
        };
        _subtitleLabel = new Label
        {
            Text = "让触摸 / 笔点击直接翻到下一页",
            ForeColor = Color.FromArgb(224, 231, 255),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            AutoSize = true,
            Location = new Point(22, 44)
        };
        header.Controls.Add(_titleLabel);
        header.Controls.Add(_subtitleLabel);

        _contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.FromArgb(248, 250, 252) };
        footer.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, 0, footer.Width, 0);

        _stepLabel = new Label
        {
            Text = "步骤 1/5",
            Location = new Point(20, 20),
            Size = new Size(280, 22),
            ForeColor = Color.FromArgb(100, 116, 139)
        };

        _backButton = new Button { Text = "上一步", Location = new Point(324, 16), Size = new Size(96, 30) };
        _backButton.Click += (_, _) => GoBack();

        _nextButton = new Button
        {
            Text = "下一步",
            Location = new Point(428, 16),
            Size = new Size(120, 30),
            BackColor = IconFactory.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _nextButton.FlatAppearance.BorderSize = 0;
        _nextButton.Click += (_, _) => GoNext();

        _cancelButton = new Button { Text = "取消", Location = new Point(556, 16), Size = new Size(84, 30) };
        _cancelButton.Click += (_, _) => Cancel();

        footer.Controls.Add(_stepLabel);
        footer.Controls.Add(_backButton);
        footer.Controls.Add(_nextButton);
        footer.Controls.Add(_cancelButton);

        Controls.Add(_contentPanel);
        Controls.Add(footer);
        Controls.Add(header);

        AcceptButton = _nextButton;
        CancelButton = _cancelButton;
    }

    /// <summary>创建向导页面（顺序即步骤顺序）。</summary>
    private void EnsurePages()
    {
        if (_pages.Count > 0)
        {
            return;
        }

        _pages.Add(new WelcomePage());
        _pages.Add(new TargetPage());
        _pages.Add(new OptionsPage());
        _pages.Add(new ProgressPage());
        _pages.Add(new FinishPage());
    }

    private void ShowPage(int index)
    {
        EnsurePages();
        index = Math.Clamp(index, 0, _pages.Count - 1);

        if (_index >= 0 && _index < _pages.Count)
        {
            _pages[_index].OnLeave(_model);
        }

        _index = index;
        var page = _pages[_index];

        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(page);
        page.OnEnter(_model);

        _titleLabel.Text = page.Title;
        _subtitleLabel.Text = page.Subtitle;
        _stepLabel.Text = $"步骤 {_index + 1}/{_pages.Count}";
        _nextButton.Text = page.NextText;
        _nextButton.Visible = page is not ProgressPage;
        _backButton.Enabled = _index > 0 && page is not ProgressPage && !_installing;
        _cancelButton.Text = _finished ? "关闭" : "取消";
    }

    private void GoBack()
    {
        if (_index <= 0 || _installing)
        {
            return;
        }

        ShowPage(_index - 1);
    }

    private void GoNext()
    {
        if (_installing)
        {
            return;
        }

        var page = _pages[_index];

        if (!page.CanLeave(_model, out var error))
        {
            MessageBox.Show(error ?? "请先完成本页设置。", AppPaths.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        page.OnLeave(_model);

        if (page is OptionsPage)
        {
            StartInstall();
            return;
        }

        if (page is FinishPage)
        {
            MarkFinished();
            Close();
            return;
        }

        ShowPage(_index + 1);
    }

    private void StartInstall()
    {
        _installing = true;
        _backButton.Enabled = false;
        _nextButton.Enabled = false;
        _cancelButton.Enabled = false;

        ShowPage(_index + 1);
        var progressPage = (ProgressPage)_pages[_index];
        progressPage.Reset();
        Application.DoEvents();

        InstallResult result;
        try
        {
            var manager = new InstallManager(_log);
            result = UiRunner.RunWithPump(
                () => manager.Install(_model.ToInstallOptions(), progressPage.Progress),
                progressPage.Pump);
        }
        catch (Exception ex)
        {
            _log.Error("安装过程异常", ex);
            result = new InstallResult(false, "安装失败：" + ex.Message, _model.InstallDirectory, null);
        }

        _model.Result = result;
        _installing = false;
        _cancelButton.Enabled = true;
        _nextButton.Enabled = true;

        if (result.Success && _model.StartAfterInstall && result.ExecutablePath is not null
            && AppInstanceManager.Start(result.ExecutablePath) is not null)
        {
            progressPage.AppendLog("已启动 BetterClickPPT（在托盘区运行）。");
        }

        progressPage.Complete(result);
        ShowPage(_pages.Count - 1);
    }

    private void Cancel()
    {
        if (_installing)
        {
            return;
        }

        if (_finished)
        {
            Close();
            return;
        }

        var answer = MessageBox.Show(
            "确定要退出安装吗？",
            AppPaths.ProductName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer == DialogResult.Yes)
        {
            ExitCode = 1;
            Close();
        }
    }

    /// <inheritdoc />
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_model.Result?.Success != true && !_finished && ExitCode == 0)
        {
            ExitCode = 1;
        }

        base.OnFormClosed(e);
    }

    /// <summary>标记安装完成（由完成页调用）。</summary>
    private void MarkFinished()
    {
        _finished = true;
        ExitCode = 0;
        _cancelButton.Text = "关闭";
    }
}
