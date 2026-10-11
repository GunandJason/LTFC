using BetterClickPPT.App.Ui;
using BetterClickPPT.Core;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Engine;
using BetterClickPPT.Core.Input;

namespace BetterClickPPT.App.Tray;

/// <summary>设置窗口：目标软件、翻页行为、诊断。</summary>
internal sealed partial class SettingsForm : Form
{
    private readonly TrayApplicationContext _owner;
    private readonly AppConfig _config;
    private readonly SlideshowEnhancer _enhancer;

    private readonly Dictionary<string, CheckBox> _targetBoxes = new();
    private readonly Dictionary<string, Label> _targetPathLabels = new();

    private CheckBox _enabledBox = null!;
    private ListBox _customTargetList = null!;
    private CheckBox _touchBox = null!;
    private CheckBox _swallowTouchBox = null!;
    private CheckBox _mouseBox = null!;
    private CheckBox _swallowMouseBox = null!;
    private CheckBox _rawTouchBox = null!;
    private CheckBox _mouseModeBox = null!;
    private CheckBox _notifyBox = null!;
    private CheckBox _verboseBox = null!;
    private ComboBox _nextKeyCombo = null!;
    private ComboBox _sendModeCombo = null!;
    private ComboBox _touchActionCombo = null!;
    private NumericUpDown _debounceInput = null!;
    private NumericUpDown _tapDurationInput = null!;
    private NumericUpDown _edgeMarginInput = null!;
    private Label _statusLabel = null!;

    public SettingsForm(TrayApplicationContext owner)
    {
        _owner = owner;
        _enhancer = owner.Enhancer;
        _config = owner.CurrentConfig.Clone();

        InitializeUi();
        LoadFromConfig();

        _enhancer.Activity += OnActivity;
        RefreshDiagnosticsTab();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _enhancer.Activity -= OnActivity;
            _diagnosticsTimer?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeUi()
    {
        Text = AppPaths.ProductDisplayName + " - 设置";
        ClientSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = IconFactory.AppIcon;
        Font = new Font("Microsoft YaHei UI", 9f);
        ShowInTaskbar = true;

        var header = new GradientHeaderPanel { Dock = DockStyle.Top, Height = 58 };
        header.Controls.Add(new Label
        {
            Text = AppPaths.ProductDisplayName + " 设置",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 16)
        });

        var tabs = new TabControl
        {
            Location = new Point(12, 70),
            Size = new Size(616, 430),
            Padding = new Point(14, 6)
        };

        tabs.TabPages.Add(BuildTargetsTab());
        tabs.TabPages.Add(BuildBehaviorTab());
        tabs.TabPages.Add(BuildDiagnosticsTab());

        var saveButton = new Button
        {
            Text = "保存并立即生效",
            Location = new Point(384, 512),
            Size = new Size(140, 32),
            BackColor = IconFactory.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += (_, _) => SaveAndApply();

        var cancelButton = new Button
        {
            Text = "关闭",
            Location = new Point(532, 512),
            Size = new Size(96, 32)
        };
        cancelButton.Click += (_, _) => Close();

        _statusLabel = new Label
        {
            Text = "配置文件：" + AppPaths.ConfigFile,
            AutoSize = false,
            Location = new Point(16, 518),
            Size = new Size(360, 22),
            ForeColor = Color.FromArgb(100, 116, 139)
        };

        Controls.Add(header);
        Controls.Add(tabs);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);
        Controls.Add(_statusLabel);
    }

    private TabPage BuildTargetsTab()
    {
        var page = new TabPage("目标软件") { BackColor = Color.White, AutoScroll = true };

        _enabledBox = new CheckBox
        {
            Text = "启用触摸增强（总开关）",
            Location = new Point(16, 14),
            Size = new Size(400, 24),
            Checked = true
        };
        page.Controls.Add(_enabledBox);

        var builtInGroup = new GroupBox
        {
            Text = "为哪些软件启用（只增强“全屏 + 无边框”的放映窗口）",
            Location = new Point(12, 46),
            Size = new Size(580, 132)
        };

        var index = 0;
        foreach (var target in _config.Targets.Where(t => t.IsBuiltIn))
        {
            var box = new CheckBox
            {
                Text = target.DisplayName,
                Location = new Point(16, 24 + index * 42),
                Size = new Size(540, 24),
                Checked = target.Enabled
            };
            _targetBoxes[target.Id] = box;
            builtInGroup.Controls.Add(box);

            var pathLabel = new Label
            {
                Text = "主程序：" + (string.IsNullOrWhiteSpace(target.ExePath) ? "未记录（不影响使用）" : target.ExePath),
                Location = new Point(38, 46 + index * 42),
                Size = new Size(520, 20),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoEllipsis = true
            };
            _targetPathLabels[target.Id] = pathLabel;
            builtInGroup.Controls.Add(pathLabel);
            index++;
        }

        page.Controls.Add(builtInGroup);

        var customGroup = new GroupBox
        {
            Text = "自定义程序（其他演示软件，按进程名匹配）",
            Location = new Point(12, 186),
            Size = new Size(580, 150)
        };

        _customTargetList = new ListBox
        {
            Location = new Point(16, 24),
            Size = new Size(420, 112),
            IntegralHeight = false
        };
        customGroup.Controls.Add(_customTargetList);

        var addButton = new Button { Text = "添加…", Location = new Point(448, 24), Size = new Size(116, 30) };
        addButton.Click += (_, _) => AddCustomTarget();
        customGroup.Controls.Add(addButton);

        var removeButton = new Button { Text = "删除", Location = new Point(448, 60), Size = new Size(116, 30) };
        removeButton.Click += (_, _) => RemoveCustomTarget();
        customGroup.Controls.Add(removeButton);

        customGroup.Controls.Add(new Label
        {
            Text = "填写进程名，\r\n例如 myplayer.exe",
            Location = new Point(448, 96),
            Size = new Size(120, 44),
            ForeColor = Color.FromArgb(100, 116, 139)
        });

        page.Controls.Add(customGroup);

        page.Controls.Add(new Label
        {
            Text = "识别条件：该进程的窗口铺满显示器且没有标题栏（真正的放映窗口）。\r\n"
                 + "最大化的编辑窗口带标题栏，因此不会被误判，点击也不会被拦截。",
            Location = new Point(16, 344),
            Size = new Size(570, 44),
            ForeColor = Color.FromArgb(71, 85, 105)
        });

        return page;
    }

    private TabPage BuildBehaviorTab()
    {
        var page = new TabPage("翻页行为") { BackColor = Color.White, AutoScroll = true };

        _touchBox = new CheckBox { Text = "触摸 / 手写笔轻触 → 触发点击 / 下一页（核心功能）", Location = new Point(16, 16), Size = new Size(520, 24) };
        page.Controls.Add(_touchBox);

        _swallowTouchBox = new CheckBox { Text = "吞掉触摸原始点击（推荐：保证“点一下只触发一次”）", Location = new Point(38, 44), Size = new Size(500, 24) };
        page.Controls.Add(_swallowTouchBox);

        _mouseBox = new CheckBox { Text = "鼠标左键点击 → 下一页（一般无需开启，鼠标本来就能翻页）", Location = new Point(16, 76), Size = new Size(520, 24) };
        page.Controls.Add(_mouseBox);

        _swallowMouseBox = new CheckBox { Text = "鼠标点击被增强时也吞掉原始点击", Location = new Point(38, 104), Size = new Size(480, 24) };
        page.Controls.Add(_swallowMouseBox);

        _rawTouchBox = new CheckBox { Text = "深度触控模式：直接监听触摸屏设备（部分电脑必须开启）", Location = new Point(16, 136), Size = new Size(520, 24) };
        page.Controls.Add(_rawTouchBox);

        _mouseModeBox = new CheckBox { Text = "放映开始时自动切回鼠标模式（避免触摸模式下放映界面变大）", Location = new Point(16, 164), Size = new Size(560, 24) };
        page.Controls.Add(_mouseModeBox);

        page.Controls.Add(new Label { Text = "翻页按键", Location = new Point(16, 204), Size = new Size(110, 24) });
        _nextKeyCombo = new ComboBox
        {
            Location = new Point(150, 200),
            Size = new Size(300, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var name in NextKeyNames.All)
        {
            _nextKeyCombo.Items.Add(NextKeyNames.ToDisplayName(name));
        }

        page.Controls.Add(_nextKeyCombo);

        page.Controls.Add(new Label { Text = "发送方式", Location = new Point(16, 238), Size = new Size(110, 24) });
        _sendModeCombo = new ComboBox
        {
            Location = new Point(150, 234),
            Size = new Size(300, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var name in KeySendModes.All)
        {
            _sendModeCombo.Items.Add(KeySendModes.ToDisplayName(name));
        }

        page.Controls.Add(_sendModeCombo);

        page.Controls.Add(new Label { Text = "防抖间隔 (ms)", Location = new Point(16, 272), Size = new Size(130, 24) });
        _debounceInput = new NumericUpDown
        {
            Location = new Point(150, 268),
            Size = new Size(90, 24),
            Minimum = 0,
            Maximum = 3000,
            Increment = 50
        };
        page.Controls.Add(_debounceInput);

        page.Controls.Add(new Label { Text = "轻触最长时长 (ms)", Location = new Point(260, 272), Size = new Size(140, 24) });
        _tapDurationInput = new NumericUpDown
        {
            Location = new Point(410, 268),
            Size = new Size(90, 24),
            Minimum = 100,
            Maximum = 2000,
            Increment = 50
        };
        page.Controls.Add(_tapDurationInput);

        page.Controls.Add(new Label { Text = "忽略屏幕边缘 (px)", Location = new Point(16, 306), Size = new Size(130, 24) });
        _edgeMarginInput = new NumericUpDown
        {
            Location = new Point(150, 302),
            Size = new Size(90, 24),
            Minimum = 0,
            Maximum = 600,
            Increment = 10
        };
        page.Controls.Add(_edgeMarginInput);

        _notifyBox = new CheckBox { Text = "进入放映时显示气泡提示（退出放映不提示）", Location = new Point(16, 340), Size = new Size(460, 24) };
        page.Controls.Add(_notifyBox);

        _verboseBox = new CheckBox { Text = "记录详细日志（排查问题时开启）", Location = new Point(16, 368), Size = new Size(460, 24) };
        page.Controls.Add(_verboseBox);

        var resetButton = new Button { Text = "恢复默认设置", Location = new Point(16, 512), Size = new Size(130, 30) };
        resetButton.Click += (_, _) => ResetToDefaults();
        page.Controls.Add(resetButton);

        page.Controls.Add(new Label { Text = "轻触的处理方式", Location = new Point(16, 406), Size = new Size(130, 24) });
        _touchActionCombo = new ComboBox
        {
            Location = new Point(150, 402),
            Size = new Size(430, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var name in TouchActions.All)
        {
            _touchActionCombo.Items.Add(TouchActions.ToDisplayName(name));
        }

        page.Controls.Add(_touchActionCombo);

        page.Controls.Add(new Label
        {
            Text = "说明：只有“单指、快速、位移很小”的轻触才会被处理。默认把轻触还原为一次真实鼠标点击，\r\n"
                 + "因此分步动画、超链接、屏幕上的“上一页 / 下一页”按钮都能正常使用；\r\n"
                 + "若希望轻触只做“翻页”（不触发动画与超链接），请改为“发送下一页按键”。\r\n"
                 + "“自动切回鼠标模式”要求 PowerPoint 的快速访问工具栏里有“触摸/鼠标模式”按钮（可在“诊断”页确认）。",
            Location = new Point(16, 434),
            Size = new Size(570, 70),
            ForeColor = Color.FromArgb(71, 85, 105)
        });

        return page;
    }
}
