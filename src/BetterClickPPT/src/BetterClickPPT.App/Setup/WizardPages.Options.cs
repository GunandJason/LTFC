using BetterClickPPT.Core;

namespace BetterClickPPT.App.Setup;

/// <summary>步骤 3：安装选项。</summary>
internal sealed class OptionsPage : WizardPage
{
    private readonly TextBox _directoryBox = new();
    private readonly CheckBox _autoStartBox = new();
    private readonly CheckBox _desktopBox = new();
    private readonly CheckBox _startNowBox = new();

    public override string Title => "安装选项";

    public override string Subtitle => "安装位置与启动方式";

    public override string NextText => "开始安装";

    public OptionsPage()
    {
        Controls.Add(CreateLabel("安装位置", 16, 10, 200, 22));

        _directoryBox.Location = new Point(16, 36);
        _directoryBox.Size = new Size(470, 24);
        Controls.Add(_directoryBox);

        var browseButton = new Button { Text = "浏览…", Location = new Point(496, 34), Size = new Size(90, 28) };
        browseButton.Click += (_, _) => BrowseDirectory();
        Controls.Add(browseButton);

        Controls.Add(CreateHint(
            "安装到当前用户目录可免管理员权限（推荐）。安装程序只会新增一个文件夹、快捷方式与卸载信息。",
            16, 66, 580, 24));

        var group = new GroupBox { Text = "启动方式", Location = new Point(12, 98), Size = new Size(600, 130) };

        _autoStartBox.Text = "开机自动启动（常驻托盘，推荐）";
        _autoStartBox.Location = new Point(16, 28);
        _autoStartBox.Size = new Size(540, 24);
        group.Controls.Add(_autoStartBox);

        _desktopBox.Text = "创建桌面快捷方式";
        _desktopBox.Location = new Point(16, 58);
        _desktopBox.Size = new Size(540, 24);
        group.Controls.Add(_desktopBox);

        _startNowBox.Text = "安装完成后立即启动";
        _startNowBox.Location = new Point(16, 88);
        _startNowBox.Size = new Size(540, 24);
        group.Controls.Add(_startNowBox);

        Controls.Add(group);

        Controls.Add(CreateHint(
            "说明：程序会常驻系统托盘（右下角）。左键双击托盘图标打开设置；\r\n"
            + "右键菜单可暂停增强、查看诊断信息或退出。",
            16, 238, 580, 44));

        Controls.Add(CreateHint(
            "卸载：开始菜单 → PPT 触摸增强工具 右键，或在“设置 → 应用 → 已安装的应用”中找到本程序卸载。",
            16, 286, 580, 44));
    }

    public override void OnEnter(SetupModel model)
    {
        _directoryBox.Text = model.InstallDirectory;
        _autoStartBox.Checked = model.AutoStart;
        _desktopBox.Checked = model.DesktopShortcut;
        _startNowBox.Checked = model.StartAfterInstall;
    }

    public override void OnLeave(SetupModel model)
    {
        model.InstallDirectory = _directoryBox.Text.Trim();
        model.AutoStart = _autoStartBox.Checked;
        model.DesktopShortcut = _desktopBox.Checked;
        model.StartAfterInstall = _startNowBox.Checked;
    }

    public override bool CanLeave(SetupModel model, out string? error)
    {
        error = null;
        var directory = _directoryBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "请填写安装位置。";
            return false;
        }

        try
        {
            var full = Path.GetFullPath(directory);
            var root = Path.GetPathRoot(full)?.TrimEnd('\\');

            if (string.Equals(full.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase))
            {
                error = "请不要直接安装到磁盘根目录，请选择一个子文件夹。";
                return false;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if ((!string.IsNullOrWhiteSpace(programFiles) && full.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(programFilesX86) && full.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
            {
                error = "该目录需要管理员权限才能写入。\r\n请改用默认目录（%LOCALAPPDATA%\\Programs\\BetterClickPPT）或其他普通目录。";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = "安装位置无效：" + ex.Message;
            return false;
        }

        return true;
    }

    private void BrowseDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择安装位置",
            ShowNewFolderButton = true
        };

        if (Directory.Exists(_directoryBox.Text))
        {
            dialog.SelectedPath = _directoryBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _directoryBox.Text = Path.Combine(dialog.SelectedPath, AppPaths.ProductName);
        }
    }
}
