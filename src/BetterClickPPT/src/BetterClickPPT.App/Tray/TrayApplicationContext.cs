using BetterClickPPT.App.Ui;
using BetterClickPPT.Core;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Deployment;
using BetterClickPPT.Core.Engine;
using BetterClickPPT.Core.Logging;
using BetterClickPPT.Core.Office;

namespace BetterClickPPT.App.Tray;

/// <summary>
/// 托盘程序：后台运行核心增强器，并提供设置、诊断、日志、退出等入口。
/// </summary>
internal sealed partial class TrayApplicationContext : ApplicationContext
{
    private readonly AppLog _log;
    private readonly ConfigStore _store;
    private readonly SlideshowEnhancer _enhancer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly SynchronizationContext _uiContext;
    private readonly Thread _shutdownWatcher;

    private AppConfig _config;
    private SettingsForm? _settingsForm;
    private bool _isShowing;

    public TrayApplicationContext(AppLog log, bool openSettings = false)
    {
        _log = log;
        _store = new ConfigStore(null, log);
        _config = _store.Load();
        _log.Verbose = _config.VerboseLog;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _enhancer = new SlideshowEnhancer(_config, log);
        _enhancer.ShowStateChanged += OnShowStateChanged;

        SyncAutoStartWithConfig();
        _enhancer.Start();

        _toggleItem = new ToolStripMenuItem("启用触摸增强（轻点翻页）") { CheckOnClick = false, Checked = _config.Enabled };
        _toggleItem.Click += (_, _) => ToggleEnabled();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem("设置…", ShowSettings));
        menu.Items.Add(CreateItem("检测当前前台窗口", ShowDiagnostics));
        menu.Items.Add(CreateItem("打开日志文件夹", OpenLogFolder));
        menu.Items.Add(CreateItem("打开配置文件", OpenConfigFile));
        menu.Items.Add(CreateItem("关于", ShowAbout));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem("退出", ExitApplication));

        _notifyIcon = new NotifyIcon
        {
            Icon = _config.Enabled ? IconFactory.EnabledIcon : IconFactory.DisabledIcon,
            Text = BuildTooltip(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _shutdownWatcher = new Thread(WatchShutdownSignal)
        {
            IsBackground = true,
            Name = "BetterClickPPT.ShutdownWatcher"
        };
        _shutdownWatcher.Start();

        _log.Info($"托盘已启动：配置 {_store.FilePath}");
        Notify($"{AppPaths.ProductDisplayName} 已启动", "在全屏放映时轻点屏幕即可翻到下一页。右键托盘图标可打开设置。");

        if (openSettings)
        {
            // 消息循环启动后立即打开设置窗口（供快捷方式 / 命令行使用）。
            _uiContext.Post(_ => ShowSettings(), null);
        }
    }

    /// <summary>当前配置（设置窗口使用）。</summary>
    public AppConfig CurrentConfig => _config;

    /// <summary>核心增强器（设置窗口使用）。</summary>
    public SlideshowEnhancer Enhancer => _enhancer;

    /// <summary>日志。</summary>
    public AppLog Log => _log;

    /// <summary>配置存储（设置窗口使用）。</summary>
    public ConfigStore Store => _store;

    /// <summary>保存配置并立即生效（设置窗口调用）。</summary>
    public void SaveConfig(AppConfig config, bool applyNow)
    {
        _config = config;
        _config.Normalize();
        _store.Save(_config);
        _log.Verbose = _config.VerboseLog;
        SyncAutoStartWithConfig();

        if (applyNow)
        {
            _enhancer.ApplyConfig(_config);
        }

        _uiContext.Post(_ =>
        {
            _toggleItem.Checked = _config.Enabled;
            _notifyIcon.Icon = _config.Enabled ? IconFactory.EnabledIcon : IconFactory.DisabledIcon;
            _notifyIcon.Text = BuildTooltip();
        }, null);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _enhancer.Dispose();
        }

        base.Dispose(disposing);
    }

    private static ToolStripMenuItem CreateItem(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    private void ToggleEnabled()
    {
        _config.Enabled = !_config.Enabled;
        SaveConfig(_config, applyNow: true);
        Notify(
            _config.Enabled ? "已启用触摸增强" : "已暂停",
            _config.Enabled ? "在全屏放映中，轻点屏幕 / 手写笔即可翻到下一页。" : "程序仍在运行，但不会拦截任何点击。");
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(this);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ShowDiagnostics()
    {
        var (probe, match) = _enhancer.Detector.DiagnoseForeground();
        if (probe is null)
        {
            MessageBox.Show("无法获取前台窗口信息。", "诊断", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var matchText = match is null
            ? "匹配结果：不匹配（当前窗口不会被增强）"
            : $"匹配结果：命中规则「{match.Rule.DisplayName}」";

        var toggleText = "触摸 / 鼠标模式按钮："
                         + (OfficeTouchModeSwitcher.IsTogglePresent(probe.ProcessId, out var toggleDetail)
                             ? "可用 → 放映开始时会自动切回鼠标模式"
                             : "不可用")
                         + $"（{toggleDetail}）";

        MessageBox.Show(
            probe.ToDiagnosticText() + Environment.NewLine + Environment.NewLine + matchText
            + Environment.NewLine + Environment.NewLine + toggleText,
            "当前前台窗口诊断",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenLogFolder() => OpenPath(AppPaths.LogDirectory);

    private void OpenConfigFile() => OpenPath(_store.FilePath);

    private void OpenPath(string path)
    {
        try
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
            {
                AppPaths.EnsureDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "{}");
            }
            else if (!File.Exists(path) && !Directory.Exists(path))
            {
                AppPaths.EnsureDirectory(path);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开失败：" + ex.Message, AppPaths.ProductDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            $"{AppPaths.ProductDisplayName}{Environment.NewLine}"
            + $"版本 {CliRunner.GetVersion()}{Environment.NewLine}{Environment.NewLine}"
            + "这是一款 PPT 触摸增强工具：在触摸屏 / 触摸一体机上全屏放映时，"
            + "用手指或手写笔轻点屏幕即可翻到下一页，不必再用滑动切换；同时兼容 PowerPoint 与 WPS 演示。"
            + Environment.NewLine
            + "增强内容：轻点屏幕 → 翻页（默认还原成一次真实鼠标点击，页内动画、超链接、"
            + "屏幕上的“上一页 / 下一页”按钮都能正常使用）；滑动、双指缩放、长按保持原样；"
            + "放映开始时自动把 Office 切回“鼠标模式”，避免触摸模式下放映界面变大（可在设置里关闭）。"
            + Environment.NewLine + Environment.NewLine
            + "实现方式：识别放映窗口 + 全局鼠标钩子（触摸被系统提升为鼠标点击）+ "
            + "原始输入（直接监听触摸屏 HID 报告）。安装与运行均不需要管理员权限。"
            + Environment.NewLine + Environment.NewLine
            + $"程序目录：{AppContext.BaseDirectory}{Environment.NewLine}"
            + $"配置文件：{_store.FilePath}",
            $"关于 {AppPaths.ProductDisplayName}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ExitApplication()
    {
        _log.Info("用户退出托盘程序");
        _uiContext.Post(_ =>
        {
            _notifyIcon.Visible = false;
            ExitThread();
        }, null);
    }

    private void SyncAutoStartWithConfig()
    {
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, AppPaths.MainExecutableName);
            var target = File.Exists(executable) ? executable : AppInstanceManager.CurrentExecutablePath;

            if (_config.AutoStart)
            {
                AutoStartManager.Enable(target);
            }
            else if (AutoStartManager.GetCommandLine() is not null)
            {
                AutoStartManager.Disable();
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"同步开机自启设置失败：{ex.Message}");
        }
    }

    private void OnShowStateChanged(object? sender, ShowStateChangedEventArgs e)
    {
        _isShowing = e.IsShowing;
        _uiContext.Post(_ => _notifyIcon.Text = BuildTooltip(), null);

        // 只在「进入放映」时提示；退出放映（Esc / 放映结束）不再弹窗打断用户，
        // 状态变化仍会反映到托盘图标的提示文本里。
        if (!_config.ShowNotifications || !e.IsShowing)
        {
            return;
        }

        Notify("已进入全屏放映", $"已开始增强：{e.Description}。现在轻点屏幕即可翻页。");
    }

    private void Notify(string title, string text)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
        }
        catch
        {
            // 气泡提示失败不影响功能。
        }
    }

    private string BuildTooltip()
    {
        var state = _config.Enabled ? "已启用" : "已暂停";
        var text = _isShowing
            ? $"{AppPaths.ProductDisplayName} · {state} · 放映中"
            : $"{AppPaths.ProductDisplayName} · {state}";
        return text.Length > 63 ? text[..63] : text;
    }

    private void WatchShutdownSignal()
    {
        try
        {
            using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.ShutdownEventName);
            handle.WaitOne();
            _log.Info("收到退出信号，准备退出…");
            ExitApplication();
        }
        catch (Exception ex)
        {
            _log.Warn($"监听退出信号失败：{ex.Message}");
        }
    }
}
