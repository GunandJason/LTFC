using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;

namespace BetterClickPPT.App.Setup;

/// <summary>步骤 2：选择目标软件（自动检测安装位置）。</summary>
internal sealed class TargetPage : WizardPage
{
    private readonly CheckBox _powerPointBox = new();
    private readonly TextBox _powerPointPath = new();
    private readonly Label _powerPointStatus = new();
    private readonly CheckBox _wpsBox = new();
    private readonly TextBox _wpsPath = new();
    private readonly Label _wpsStatus = new();

    private bool _redetectRequested;

    public override string Title => "选择目标软件";

    public override string Subtitle => "已自动检测安装位置，可手动修正";

    public TargetPage()
    {
        BuildAppGroup("Microsoft PowerPoint", 6, _powerPointBox, _powerPointStatus, _powerPointPath);
        BuildAppGroup("WPS 演示", 172, _wpsBox, _wpsStatus, _wpsPath);

        var redetectButton = new Button { Text = "重新检测", Location = new Point(16, 340), Size = new Size(120, 30) };
        redetectButton.Click += (_, _) =>
        {
            _redetectRequested = true;
            var model = (SetupModel?)Tag;
            if (model is not null)
            {
                model.Redetect();
                OnEnter(model);
            }
        };
        Controls.Add(redetectButton);

        Controls.Add(CreateHint(
            "检测方式：注册表 App Paths / COM 注册信息、Office 与 WPS 安装信息、常见安装目录、卸载列表、正在运行的进程。\r\n"
            + "若自动检测失败，可点“浏览…”手动选择主程序（POWERPNT.EXE 或 wpp.exe）。",
            150, 336, 450, 44));
    }

    public override void OnEnter(SetupModel model)
    {
        Tag = model;

        if (_redetectRequested)
        {
            _redetectRequested = false;
        }

        _powerPointBox.Checked = model.EnablePowerPoint;
        _powerPointPath.Text = model.PowerPoint.ExecutablePath ?? string.Empty;
        _powerPointStatus.Text = DescribeApp(model.PowerPoint);

        _wpsBox.Checked = model.EnableWps;
        _wpsPath.Text = model.Wps.ExecutablePath ?? string.Empty;
        _wpsStatus.Text = DescribeApp(model.Wps);
    }

    public override void OnLeave(SetupModel model)
    {
        model.EnablePowerPoint = _powerPointBox.Checked;
        model.EnableWps = _wpsBox.Checked;
        model.PowerPoint = model.PowerPoint with { ExecutablePath = NullIfEmpty(_powerPointPath.Text) };
        model.Wps = model.Wps with { ExecutablePath = NullIfEmpty(_wpsPath.Text) };
    }

    public override bool CanLeave(SetupModel model, out string? error)
    {
        error = null;

        if (!_powerPointBox.Checked && !_wpsBox.Checked)
        {
            var answer = MessageBox.Show(
                "您还没有选择任何目标软件，程序会安装但不会启用任何增强。\r\n是否继续？",
                AppPaths.ProductName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                error = "请勾选至少一个目标软件（或手动指定主程序位置）。";
                return false;
            }
        }

        if (_powerPointBox.Checked && !IsValidExecutable(_powerPointPath.Text))
        {
            error = "PowerPoint 主程序路径无效，请点击“浏览…”选择 POWERPNT.EXE。";
            return false;
        }

        if (_wpsBox.Checked && !IsValidExecutable(_wpsPath.Text))
        {
            error = "WPS 演示主程序路径无效，请点击“浏览…”选择 wpp.exe。";
            return false;
        }

        return true;
    }

    private void BuildAppGroup(string name, int top, CheckBox box, Label status, TextBox path)
    {
        var group = new GroupBox
        {
            Text = name,
            Location = new Point(12, top),
            Size = new Size(600, 158)
        };

        box.Text = $"为 {name} 启用触摸增强（轻点翻页）";
        box.Location = new Point(16, 26);
        box.Size = new Size(460, 24);
        group.Controls.Add(box);

        status.Location = new Point(16, 52);
        status.Size = new Size(560, 40);
        status.ForeColor = Color.FromArgb(71, 85, 105);
        group.Controls.Add(status);

        path.Location = new Point(16, 96);
        path.Size = new Size(460, 24);
        group.Controls.Add(path);

        var browseButton = new Button { Text = "浏览…", Location = new Point(486, 94), Size = new Size(90, 28) };
        browseButton.Click += (_, _) => Browse(path);
        group.Controls.Add(browseButton);

        group.Controls.Add(new Label
        {
            Text = "主程序位置（可手动修改）",
            Location = new Point(16, 124),
            Size = new Size(300, 20),
            ForeColor = Color.FromArgb(148, 163, 184)
        });

        Controls.Add(group);
    }

    private void Browse(TextBox target)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择主程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };

        if (File.Exists(target.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private static string DescribeApp(DetectedApp app)
        => app.IsFound
            ? $"已检测到（来源：{app.Source}，版本：{app.Version ?? "未知"}）"
            : "未检测到。若本机已安装，请点击“浏览…”手动指定主程序。";

    private static bool IsValidExecutable(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
