using BetterClickPPT.Core;
using BetterClickPPT.Core.Config;
using BetterClickPPT.Core.Input;

namespace BetterClickPPT.App.Tray;

/// <summary>设置窗口：配置读写。</summary>
internal sealed partial class SettingsForm
{
    private void LoadFromConfig()
    {
        _enabledBox.Checked = _config.Enabled;

        foreach (var target in _config.Targets)
        {
            if (_targetBoxes.TryGetValue(target.Id, out var box))
            {
                box.Checked = target.Enabled;
            }
        }

        RefreshCustomTargetList();

        _touchBox.Checked = _config.AdvanceOnTouch;
        _swallowTouchBox.Checked = _config.SwallowTouchClick;
        _mouseBox.Checked = _config.AdvanceOnMouse;
        _swallowMouseBox.Checked = _config.SwallowMouseClick;
        _rawTouchBox.Checked = _config.RawTouchEnabled;
        _mouseModeBox.Checked = _config.AutoSwitchToMouseMode;
        _notifyBox.Checked = _config.ShowNotifications;
        _verboseBox.Checked = _config.VerboseLog;

        _nextKeyCombo.SelectedIndex = Math.Max(0, NextKeyNames.All.ToList().IndexOf(_config.NextKey));
        _sendModeCombo.SelectedIndex = Math.Max(0, KeySendModes.All.ToList().IndexOf(_config.KeySendMode));
        _touchActionCombo.SelectedIndex = Math.Max(0, TouchActions.All.ToList().IndexOf(_config.TouchAction));

        _debounceInput.Value = Math.Clamp(_config.DebounceMs, (int)_debounceInput.Minimum, (int)_debounceInput.Maximum);
        _tapDurationInput.Value = Math.Clamp(_config.TapMaxDurationMs, (int)_tapDurationInput.Minimum, (int)_tapDurationInput.Maximum);
        _edgeMarginInput.Value = Math.Clamp(_config.EdgeMarginPx, (int)_edgeMarginInput.Minimum, (int)_edgeMarginInput.Maximum);
    }

    private void ReadIntoConfig()
    {
        _config.Enabled = _enabledBox.Checked;

        foreach (var target in _config.Targets)
        {
            if (_targetBoxes.TryGetValue(target.Id, out var box))
            {
                target.Enabled = box.Checked;
            }
        }

        _config.AdvanceOnTouch = _touchBox.Checked;
        _config.SwallowTouchClick = _swallowTouchBox.Checked;
        _config.AdvanceOnMouse = _mouseBox.Checked;
        _config.SwallowMouseClick = _swallowMouseBox.Checked;
        _config.RawTouchEnabled = _rawTouchBox.Checked;
        _config.AutoSwitchToMouseMode = _mouseModeBox.Checked;
        _config.ShowNotifications = _notifyBox.Checked;
        _config.VerboseLog = _verboseBox.Checked;

        _config.NextKey = NextKeyNames.All[_nextKeyCombo.SelectedIndex < 0 ? 0 : _nextKeyCombo.SelectedIndex];
        _config.KeySendMode = KeySendModes.All[_sendModeCombo.SelectedIndex < 0 ? 0 : _sendModeCombo.SelectedIndex];
        _config.TouchAction = TouchActions.All[_touchActionCombo.SelectedIndex < 0 ? 0 : _touchActionCombo.SelectedIndex];
        _config.DebounceMs = (int)_debounceInput.Value;
        _config.TapMaxDurationMs = (int)_tapDurationInput.Value;
        _config.EdgeMarginPx = (int)_edgeMarginInput.Value;
    }

    private void SaveAndApply()
    {
        ReadIntoConfig();
        _owner.SaveConfig(_config, applyNow: true);
        _statusLabel.Text = $"已保存并生效（{DateTime.Now:HH:mm:ss}）";
        RefreshDiagnosticsTab();
    }

    private void ResetToDefaults()
    {
        var defaults = AppConfig.CreateDefault();
        var custom = _config.Targets.Where(t => !t.IsBuiltIn).Select(t => t.Clone()).ToList();

        _config.Enabled = defaults.Enabled;
        _config.AdvanceOnTouch = defaults.AdvanceOnTouch;
        _config.AdvanceOnMouse = defaults.AdvanceOnMouse;
        _config.SwallowTouchClick = defaults.SwallowTouchClick;
        _config.SwallowMouseClick = defaults.SwallowMouseClick;
        _config.RawTouchEnabled = defaults.RawTouchEnabled;
        _config.AutoSwitchToMouseMode = defaults.AutoSwitchToMouseMode;
        _config.EdgeMarginPx = defaults.EdgeMarginPx;
        _config.DebounceMs = defaults.DebounceMs;
        _config.NextKey = defaults.NextKey;
        _config.KeySendMode = defaults.KeySendMode;
        _config.TouchAction = defaults.TouchAction;
        _config.TapMaxDurationMs = defaults.TapMaxDurationMs;
        _config.TapMaxMovementRatio = defaults.TapMaxMovementRatio;
        _config.ShowNotifications = defaults.ShowNotifications;
        _config.VerboseLog = defaults.VerboseLog;

        foreach (var target in _config.Targets.Where(t => t.IsBuiltIn))
        {
            target.Enabled = true;
        }

        _config.Targets.RemoveAll(t => !t.IsBuiltIn);
        _config.Targets.AddRange(custom);

        LoadFromConfig();
        _statusLabel.Text = "已恢复默认设置，点击“保存并立即生效”后写入。";
    }

    private void RefreshCustomTargetList()
    {
        _customTargetList.BeginUpdate();
        _customTargetList.Items.Clear();
        foreach (var target in _config.Targets.Where(t => !t.IsBuiltIn))
        {
            _customTargetList.Items.Add(target.DisplayName);
        }

        _customTargetList.EndUpdate();
    }

    private void AddCustomTarget()
    {
        var name = Ui.TextPromptDialog.Show(this, "添加自定义程序", "请输入进程名（例如 myplayer.exe）：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (_config.Targets.Any(t => t.ProcessNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))))
        {
            MessageBox.Show("该进程名已存在。", "添加自定义程序", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _config.Targets.Add(new TargetRule
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = name,
            Enabled = true,
            IsBuiltIn = false,
            ProcessNames = new List<string> { name },
            ClassNames = new List<string>(),
            RequireFullScreen = true,
            RequireBorderless = true
        });

        RefreshCustomTargetList();
    }

    private void RemoveCustomTarget()
    {
        if (_customTargetList.SelectedIndex < 0)
        {
            return;
        }

        var name = _customTargetList.SelectedItem as string;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        _config.Targets.RemoveAll(t => !t.IsBuiltIn && string.Equals(t.DisplayName, name, StringComparison.Ordinal));
        RefreshCustomTargetList();
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                AppPaths.EnsureDirectory(Path.GetDirectoryName(path) ?? path);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开失败：" + ex.Message, AppPaths.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
