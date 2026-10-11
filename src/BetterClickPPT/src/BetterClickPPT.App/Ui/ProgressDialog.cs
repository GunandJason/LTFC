using System.Drawing.Drawing2D;
using BetterClickPPT.Core.Deployment;

namespace BetterClickPPT.App.Ui;

/// <summary>带进度条与日志的简单进度窗口（用于卸载 / 安装等耗时操作）。</summary>
internal sealed class ProgressDialog : Form
{
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly TextBox _logBox;

    public ProgressDialog(string title, string description)
    {
        Text = title;
        Size = new Size(520, 300);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ShowInTaskbar = true;
        Icon = IconFactory.AppIcon;
        Font = new Font("Microsoft YaHei UI", 9f);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = IconFactory.Accent
        };

        var headerText = new Label
        {
            Text = description,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0)
        };
        header.Controls.Add(headerText);

        _statusLabel = new Label
        {
            Text = "准备中…",
            AutoSize = false,
            Location = new Point(16, 66),
            Size = new Size(480, 20)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(16, 90),
            Size = new Size(480, 20),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        _logBox = new TextBox
        {
            Location = new Point(16, 120),
            Size = new Size(480, 130),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(header);
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(_logBox);
    }

    /// <summary>进度回调（自动切回界面线程）。</summary>
    public IProgress<InstallProgress> Progress => new Progress<InstallProgress>(SetProgress);

    /// <summary>刷新界面（消息泵回调）。</summary>
    public void Pump() => Application.DoEvents();

    private void SetProgress(InstallProgress progress)
    {
        if (IsDisposed)
        {
            return;
        }

        _progressBar.Value = Math.Clamp(progress.Percent, 0, 100);
        _statusLabel.Text = progress.Message;
        AppendLog($"[{progress.Percent,3}%] {progress.Message}");
    }

    /// <summary>追加一行日志。</summary>
    public void AppendLog(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        _logBox.AppendText(message + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }
}

/// <summary>简单的顶部渐变标题面板（安装向导 / 设置窗口共用）。</summary>
internal sealed class GradientHeaderPanel : Panel
{
    public GradientHeaderPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = IconFactory.Accent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(
            ClientRectangle,
            IconFactory.Accent,
            Color.FromArgb(15, 118, 210),
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        using var bitmap = IconFactory.CreateBitmap(40, accent: true);
        e.Graphics.DrawImage(bitmap, new Rectangle(Width - 84, (Height - 40) / 2, 40, 40));
        base.OnPaint(e);
    }
}
