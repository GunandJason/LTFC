using System.Text;
using BetterClickPPT.Core;
using BetterClickPPT.Core.Engine;
using BetterClickPPT.Core.Interop;
using BetterClickPPT.Core.Office;

namespace BetterClickPPT.App.Tray;

/// <summary>设置窗口：诊断页。</summary>
internal sealed partial class SettingsForm
{
    private ListBox _activityList = null!;
    private TextBox _diagnosticsBox = null!;
    private Label _statsLabel = null!;
    private System.Windows.Forms.Timer? _diagnosticsTimer;

    private TabPage BuildDiagnosticsTab()
    {
        var page = new TabPage("诊断") { BackColor = Color.White, AutoScroll = true };

        var probeGroup = new GroupBox
        {
            Text = "当前前台窗口（用于确认是否被识别为放映窗口）",
            Location = new Point(12, 8),
            Size = new Size(580, 194)
        };

        _diagnosticsBox = new TextBox
        {
            Location = new Point(16, 24),
            Size = new Size(548, 126),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Consolas", 9f)
        };
        probeGroup.Controls.Add(_diagnosticsBox);

        var probeButton = new Button { Text = "检测当前前台窗口", Location = new Point(16, 156), Size = new Size(150, 28) };
        probeButton.Click += (_, _) => RefreshDiagnosticsTab();
        probeGroup.Controls.Add(probeButton);

        var devicesButton = new Button { Text = "查看触摸设备", Location = new Point(174, 156), Size = new Size(130, 28) };
        devicesButton.Click += (_, _) => ShowTouchDevices();
        probeGroup.Controls.Add(devicesButton);

        var logButton = new Button { Text = "打开日志文件夹", Location = new Point(312, 156), Size = new Size(130, 28) };
        logButton.Click += (_, _) => OpenPath(AppPaths.LogDirectory);
        probeGroup.Controls.Add(logButton);

        var configButton = new Button { Text = "打开配置文件", Location = new Point(450, 156), Size = new Size(114, 28) };
        configButton.Click += (_, _) => OpenPath(AppPaths.ConfigFile);
        probeGroup.Controls.Add(configButton);

        page.Controls.Add(probeGroup);

        var activityGroup = new GroupBox
        {
            Text = "实时活动（最近 200 条）",
            Location = new Point(12, 208),
            Size = new Size(580, 166)
        };

        _activityList = new ListBox
        {
            Location = new Point(16, 24),
            Size = new Size(548, 132),
            IntegralHeight = false,
            Font = new Font("Consolas", 9f),
            HorizontalScrollbar = true
        };
        activityGroup.Controls.Add(_activityList);
        page.Controls.Add(activityGroup);

        _statsLabel = new Label
        {
            Text = "统计：—",
            Location = new Point(12, 378),
            Size = new Size(580, 22),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        page.Controls.Add(_statsLabel);

        _diagnosticsTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _diagnosticsTimer.Tick += (_, _) => UpdateStats();
        _diagnosticsTimer.Start();

        return page;
    }

    private void RefreshDiagnosticsTab()
    {
        try
        {
            var (probe, match) = _enhancer.Detector.DiagnoseForeground();
            var builder = new StringBuilder();

            if (probe is null)
            {
                builder.AppendLine("无法获取前台窗口信息。");
            }
            else
            {
                builder.AppendLine(probe.ToDiagnosticText());
                builder.AppendLine();
                builder.AppendLine(match is null
                    ? "匹配结果：不匹配（当前窗口不会被增强）"
                    : $"匹配结果：命中规则「{match.Rule.DisplayName}」→ 触摸 / 笔点击会翻到下一页");
                builder.AppendLine();
                builder.AppendLine(DescribeTouchModeToggle(probe.ProcessId));
            }

            _diagnosticsBox.Text = builder.ToString();
        }
        catch (Exception ex)
        {
            _diagnosticsBox.Text = "检测失败：" + ex.Message;
        }

        UpdateStats();
        RefreshActivityList();
    }

    /// <summary>
    /// 诊断输出：该进程的快速访问工具栏里是否有「触摸/鼠标模式」按钮。
    /// 只读检测，不会展开面板或改动 Office 界面。
    /// </summary>
    private static string DescribeTouchModeToggle(uint processId)
    {
        if (processId == 0)
        {
            return "触摸 / 鼠标模式按钮：无法检测（没有可取到的进程）";
        }

        var present = OfficeTouchModeSwitcher.IsTogglePresent(processId, out var detail);
        return $"触摸 / 鼠标模式按钮：{(present ? "可用 → 放映开始时会自动切回鼠标模式" : "不可用")}（{detail}）";
    }

    private void UpdateStats()
    {
        _statsLabel.Text = $"统计：触发 {_enhancer.AdvanceCount} 次（其中还原鼠标点击 {_enhancer.ClickReplayCount} 次），"
                         + $"捕获触摸 / 笔轻触 {_enhancer.TouchClickCount} 次，鼠标事件 {_enhancer.MouseEventCount} 次，"
                         + $"增强器 {(_enhancer.IsRunning ? "运行中" : "已停止")}，"
                         + $"鼠标钩子 {(_enhancer.IsMouseHookActive ? "已安装" : "未安装")}，"
                         + $"深度触控 {(_enhancer.IsRawTouchActive ? "已开启" : "未开启")}";
    }

    private void RefreshActivityList()
    {
        var activities = _enhancer.RecentActivities;
        if (_activityList.Items.Count == activities.Count)
        {
            return;
        }

        _activityList.BeginUpdate();
        _activityList.Items.Clear();
        foreach (var activity in activities)
        {
            _activityList.Items.Add(activity.ToString());
        }

        if (_activityList.Items.Count > 0)
        {
            _activityList.TopIndex = _activityList.Items.Count - 1;
        }

        _activityList.EndUpdate();
    }

    private void OnActivity(object? sender, EnhanceActivity activity)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => RefreshActivityList());
        }
        catch
        {
            // 窗口已关闭时忽略。
        }
    }

    private void ShowTouchDevices()
    {
        var builder = new StringBuilder();
        var devices = RawInputDeviceEnumerator.Enumerate();
        var digitizers = devices.Where(d => d.IsTouchScreen || d.IsPen || d.IsTouchPad).ToList();

        if (digitizers.Count == 0)
        {
            builder.AppendLine("未发现触摸屏 / 手写笔 / 触摸板设备。");
            builder.AppendLine();
            builder.AppendLine("说明：即使没有触摸设备，只要触摸被系统“提升”为鼠标点击（多数普通触摸屏电脑），");
            builder.AppendLine("      本程序也能把它转换成翻页。");
        }
        else
        {
            foreach (var device in digitizers)
            {
                builder.AppendLine($"[{device.KindDescription}]");
                builder.AppendLine($"    {device.Name}");
            }
        }

        MessageBox.Show(builder.ToString(), "触摸设备", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
