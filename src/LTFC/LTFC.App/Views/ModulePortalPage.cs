using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>模块页面的分页类型（对应侧边栏的子项）。</summary>
    internal enum ModulePortalSection
    {
        /// <summary>模块概览 = 侧边栏里的分组项本身。</summary>
        Overview,

        /// <summary>ClassTell：打开软件之后收到的指令。</summary>
        Messages,

        /// <summary>ClassTell：打开软件之前的未读邮件。</summary>
        Expired,

        /// <summary>模块设置（缺少独立设置界面的模块由 LTFC 提供）。</summary>
        Settings
    }

    /// <summary>
    /// 模块页面：概览 / 消息 / 过期 / 设置。
    /// 外部模块是独立进程、独立窗口：点“启动”新开一个窗口；已经在运行时只显示它已有的窗口，不会开出第二个。
    /// </summary>
    internal sealed class ModulePortalPage : PageBase
    {
        private readonly ModuleDefinition _definition;
        private readonly ModulePortalSection _section;
        private readonly Action _refreshShell;
        private readonly List<UiButton> _buttons = new List<UiButton>();
        private readonly Dictionary<string, UiButton> _buttonMap = new Dictionary<string, UiButton>(StringComparer.OrdinalIgnoreCase);

        private UiSwitch _enabledSwitch;
        private UiSwitch _autoStartSwitch;
        private UiSwitch _soundSwitch;

        private int _contentHeight;

        public ModulePortalPage(ModuleDefinition definition, ModulePortalSection section, Action refreshShell)
        {
            _definition = definition;
            _section = section;
            _refreshShell = refreshShell;

            Key = section == ModulePortalSection.Overview ? definition.Id : definition.Id + "." + SuffixFor(section);
            Title = section == ModulePortalSection.Overview ? definition.DisplayName : definition.DisplayName + " · " + TitleFor(section);
            Subtitle = SubtitleFor(section);
            WantsScroll = true;

            BuildButtons();
            BuildSwitches();
        }

        private static string SuffixFor(ModulePortalSection section)
        {
            switch (section)
            {
                case ModulePortalSection.Messages: return Modules.PageMessages;
                case ModulePortalSection.Expired: return Modules.PageExpired;
                default: return Modules.PageSettings;
            }
        }

        private string TitleFor(ModulePortalSection section)
        {
            switch (section)
            {
                case ModulePortalSection.Messages: return "消息";
                case ModulePortalSection.Expired: return "过期";
                case ModulePortalSection.Settings: return "设置";
                default: return "概览";
            }
        }

        private string SubtitleFor(ModulePortalSection section)
        {
            switch (section)
            {
                case ModulePortalSection.Messages:
                    return "打开软件之后收到的指令邮件（数据来自 ClassTell）";
                case ModulePortalSection.Expired:
                    return "打开软件之前就已经存在的未读邮件";
                case ModulePortalSection.Settings:
                    return _definition.DisplayName + " 的设置：开关、启动方式与程序位置";
                default:
                    return _definition.Description;
            }
        }

        private void BuildButtons()
        {
            if (_definition.Kind == ModuleKind.ExternalProcess)
            {
                AddButton("start", "启动（新窗口）", UiButtonKind.Primary, (s, e) => StartModule());
                AddButton("show", "显示窗口", UiButtonKind.Secondary, (s, e) => ShowModuleWindow());
                AddButton("stop", "停止", UiButtonKind.Secondary, (s, e) => StopModule());
                AddButton("restart", "重启", UiButtonKind.Secondary, (s, e) => RestartModule());
            }
            else
            {
                AddButton("start", "启动（显示窗口）", UiButtonKind.Primary, (s, e) => StartModule());
                AddButton("stop", "停止", UiButtonKind.Secondary, (s, e) => StopModule());
            }

            if (!string.IsNullOrEmpty(_definition.SettingsArguments))
            {
                AddButton("native", "打开它自己的设置", UiButtonKind.Ghost, (s, e) => OpenWithArguments(_definition.SettingsArguments));
            }

            if (_definition.SupportsFileArgument)
            {
                AddButton("file", "选择文件并打开…", UiButtonKind.Ghost, (s, e) => OpenWithFile());
            }

            if (_definition.Kind == ModuleKind.ExternalProcess)
            {
                AddButton("location", "程序位置", UiButtonKind.Ghost, (s, e) => OpenLocation());
            }
            else
            {
                AddButton("config", "编辑配置文件", UiButtonKind.Ghost, (s, e) => OpenConfigFile());
            }

            AddButton("reload", "重新读取", UiButtonKind.Ghost, (s, e) => ReloadOnly());
            AddButton("log", "查看日志", UiButtonKind.Ghost, (s, e) => ShowLog());
        }

        private void AddButton(string key, string text, UiButtonKind kind, EventHandler onClick)
        {
            UiButton button = CreateButton(text, kind, onClick);
            button.PaddingX = 16;
            _buttons.Add(button);
            _buttonMap[key] = button;
        }

        private void BuildSwitches()
        {
            if (_section != ModulePortalSection.Settings) return;

            _enabledSwitch = CreateSwitch(Settings.ModuleEnabled(_definition.Id), (s, e) =>
            {
                Settings.SetModuleEnabled(_definition.Id, ((UiSwitch)s).Checked);
                Modules.RefreshAll();
                AfterChange();
            });

            _autoStartSwitch = CreateSwitch(Settings.GetBool("AutoStart." + _definition.Id, false), (s, e) =>
            {
                Settings.Set("AutoStart." + _definition.Id, ((UiSwitch)s).Checked ? "1" : "0");
            });

            if (_definition.Id == Modules.AutoNoticeId)
            {
                _soundSwitch = CreateSwitch(Settings.AutoNoticeSound, (s, e) =>
                {
                    Settings.AutoNoticeSound = ((UiSwitch)s).Checked;
                });
            }
        }

        private ModuleProcess Target { get { return Modules.Process(_definition.Id); } }

        // ---------- 操作 ----------
        private void StartModule()
        {
            string error = ModuleLauncher.LaunchOrShow(_definition);
            if (!string.IsNullOrEmpty(error)) Ask.Warn(this, _definition.DisplayName, error);
            AfterChange();
        }

        private void ShowModuleWindow()
        {
            ModuleProcess process = Target;
            if (process == null) return;
            if (!process.IsRunning)
            {
                StartModule();
                return;
            }

            string message;
            if (process.ActivateWindow(out message)) AfterChange();
            else Ask.Info(this, _definition.DisplayName, message);
            AfterChange();
        }

        private void StopModule()
        {
            string error = ModuleLauncher.StopModule(_definition);
            if (!string.IsNullOrEmpty(error)) Ask.Warn(this, _definition.DisplayName, error);
            AfterChange();
        }

        private void RestartModule()
        {
            ModuleLauncher.StopModule(_definition);
            string error = ModuleLauncher.LaunchOrShow(_definition);
            if (!string.IsNullOrEmpty(error)) Ask.Warn(this, "重启 " + _definition.DisplayName, error);
            AfterChange();
        }

        /// <summary>只重新读取配置（AutoNotice）或刷新状态。</summary>
        private void ReloadOnly()
        {
            if (_definition.Id == Modules.AutoNoticeId)
            {
                ModuleLauncher.ReloadAutoNotice();
                Ask.Info(this, "AutoNotice", "已重新读取配置文件：", AppPaths.AutoNoticeConfigFile);
            }

            AfterChange();
        }

        private void AfterChange()
        {
            if (_refreshShell != null) _refreshShell();
            RefreshState();
        }

        private void OpenLocation()
        {
            try
            {
                ModuleProcess process = Target;
                string path = process != null ? process.ExecutablePath : string.Empty;
                if (string.IsNullOrEmpty(path))
                {
                    Ask.Warn(this, _definition.DisplayName, "还没有解析到程序位置。");
                    return;
                }

                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                else
                {
                    Ask.Warn(this, _definition.DisplayName, "没有找到程序文件，期望位置是：", path);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("打开程序位置失败", ex);
                Ask.Error(this, _definition.DisplayName, "无法打开程序位置：" + ex.Message);
            }
        }

        private void OpenConfigFile()
        {
            try
            {
                AutoNoticeService.EnsureConfigFile();
                string path = AppPaths.AutoNoticeConfigFile;
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                AppLog.Exception_("打开配置文件失败", ex);
                Ask.Error(this, _definition.DisplayName, "无法打开配置文件：" + ex.Message);
            }
        }

        private void OpenWithArguments(string arguments)
        {
            ModuleProcess process = Target;
            if (process == null) return;

            if (process.IsRunning)
            {
                // 已经运行：先尝试显示已有窗口
                string activateMessage;
                if (process.ActivateWindow(out activateMessage))
                {
                    AfterChange();
                    return;
                }
            }

            string error = ModuleLauncher.StartWithArguments(_definition, arguments);
            if (!string.IsNullOrEmpty(error)) Ask.Warn(this, _definition.DisplayName, error);
            AfterChange();
        }

        private void OpenWithFile()
        {
            ModuleProcess process = Target;
            if (process == null) return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要用 " + _definition.DisplayName + " 打开的文件";
                dialog.Filter = "演示文稿 (*.ppt;*.pptx;*.pps;*.ppsx)|*.ppt;*.pptx;*.pps;*.ppsx|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string error = ModuleLauncher.OpenFileWith(_definition, dialog.FileName);
                if (!string.IsNullOrEmpty(error)) Ask.Warn(this, _definition.DisplayName, error);
                AfterChange();
            }
        }

        private void ShowLog()
        {
            string path = AppLog.FileFor(_definition.LogName);
            string text = AppLog.ReadTail(path, 24 * 1024);
            if (text.Length > 4000) text = text.Substring(text.Length - 4000);
            if (string.IsNullOrEmpty(text)) text = "（暂无日志）";

            Ask.Custom(this, new DialogRequest
            {
                Title = _definition.DisplayName + " 日志",
                Message = "日志文件：" + path,
                Detail = text,
                Tone = DialogTone.Info,
                Buttons = new List<string> { "关闭" }
            });
        }

        // ---------- 布局 ----------
        private Rectangle _stateRect;
        private Rectangle _middleRect;
        private Rectangle _opsRect;
        private Size _laidOutSize;

        private void EnsureLayout()
        {
            if (_laidOutSize != Size) LayoutChildren();
        }

        private int StateCardHeight()
        {
            int rows = 3;
            if (_definition.Kind == ModuleKind.ExternalProcess) rows++;
            if (_definition.Id == Modules.AutoNoticeId) rows++;
            if (_definition.Id == Modules.ClassTellId) rows++;
            return Theme.S(48) + rows * Theme.S(26) + Theme.S(14);
        }

        private int MiddleCardHeight()
        {
            switch (_section)
            {
                case ModulePortalSection.Messages:
                case ModulePortalSection.Expired:
                    return Theme.S(50) + Theme.S(26) * 6 + Theme.S(16);
                case ModulePortalSection.Settings:
                    return Theme.S(50) + SwitchRowCount() * Theme.S(52) + Theme.S(16);
                default:
                    return Theme.S(50) + Theme.S(24) * 5 + Theme.S(16);
            }
        }

        private int SwitchRowCount()
        {
            int rows = 2;                       // 启用该模块 / 随主程序启动
            if (_soundSwitch != null) rows++;   // AutoNotice：提示音
            return rows;
        }

        private int OpsCardHeight()
        {
            int rows = Math.Max(1, (int)Math.Ceiling(_buttons.Count / 3d));
            return Theme.S(44) + rows * (Theme.S(42) + Theme.S(10)) + Theme.S(8);
        }

        protected override void LayoutChildren()
        {
            Rectangle area = ContentArea;
            int y = Theme.S(12);

            _stateRect = new Rectangle(area.Left, area.Top + y - ScrollOffset, area.Width, StateCardHeight());
            y += _stateRect.Height + Theme.S(16);

            _middleRect = new Rectangle(area.Left, area.Top + y - ScrollOffset, area.Width, MiddleCardHeight());
            y += _middleRect.Height + Theme.S(16);

            _opsRect = new Rectangle(area.Left, area.Top + y - ScrollOffset, area.Width, OpsCardHeight());
            y += _opsRect.Height + Theme.S(16);

            LayoutSwitch(_enabledSwitch, 0);
            LayoutSwitch(_autoStartSwitch, 1);
            LayoutSwitch(_soundSwitch, 2);

            LayoutButtons();

            _contentHeight = y + Theme.S(12);
            _laidOutSize = Size;
        }

        private void LayoutSwitch(UiSwitch toggle, int index)
        {
            if (toggle == null || _section != ModulePortalSection.Settings) return;

            int top = _middleRect.Top + Theme.S(50) + index * Theme.S(52) + Theme.S(6);
            toggle.SetBounds(_middleRect.Right - Theme.S(20) - toggle.Width, top, toggle.Width, toggle.Height);
        }

        private void LayoutButtons()
        {
            int x = _opsRect.Left + Theme.S(18);
            int top = _opsRect.Top + Theme.S(44);
            int maxRight = _opsRect.Right - Theme.S(18);
            int rowHeight = Theme.S(42);

            using (Graphics g = SafeGraphics())
            {
                int y = top;
                foreach (UiButton button in _buttons)
                {
                    int width = g == null
                        ? Theme.S(180)
                        : Math.Max(Theme.S(130), Math.Min(Theme.S(280), button.MeasureWidth(g, button.Text)));
                    if (x + width > maxRight && x > _opsRect.Left + Theme.S(18))
                    {
                        x = _opsRect.Left + Theme.S(18);
                        y += rowHeight + Theme.S(10);
                    }

                    button.SetBounds(x, y, width, rowHeight);
                    x += width + Theme.S(10);
                }
            }
        }

        protected override int ContentHeight { get { return _contentHeight; } }

        // ---------- 状态与绘制 ----------
        public override void RefreshState()
        {
            ModuleProcess process = Target;

            SetEnabledVisual("start", IsLaunchable());
            SetEnabledVisual("show", process != null && process.IsRunning);
            SetEnabledVisual("stop", process != null && process.IsRunning);
            SetEnabledVisual("restart", IsLaunchable());
            SetEnabledVisual("native", process == null || process.Exists);
            SetEnabledVisual("file", process == null || process.Exists);
            SetEnabledVisual("location", process == null || process.Exists);

            if (_enabledSwitch != null) _enabledSwitch.Checked = Settings.ModuleEnabled(_definition.Id);
            if (_autoStartSwitch != null) _autoStartSwitch.Checked = Settings.GetBool("AutoStart." + _definition.Id, false);
            if (_soundSwitch != null) _soundSwitch.Checked = Settings.AutoNoticeSound;

            _laidOutSize = Size.Empty;
            Relayout();
        }

        private void SetEnabledVisual(string key, bool enabled)
        {
            UiButton button;
            if (!_buttonMap.TryGetValue(key, out button)) return;
            button.IsEnabledVisual = enabled;
            button.Invalidate();
        }

        private bool IsLaunchable()
        {
            if (!Settings.ModuleEnabled(_definition.Id)) return false;
            if (_definition.Kind == ModuleKind.Internal) return true;

            ModuleProcess process = Target;
            return process != null && process.Exists;
        }

        private string DescribeState()
        {
            if (_definition.Kind == ModuleKind.Internal)
            {
                if (_definition.Id == Modules.AutoNoticeId)
                {
                    var service = ModuleLauncher.AutoNotice;
                    if (!Settings.AutoNoticeEnabled) return "已停用（可在本页设置中启用）";
                    return service != null ? "运行中 · 下次提醒 " + service.NextDueText() : "运行中";
                }

                return "内置模块 · 由主程序直接提供";
            }

            ModuleProcess process = Target;
            if (process == null) return "未配置";
            if (process.IsRunning)
            {
                return "运行中（独立窗口）" + (process.StartedAt.HasValue
                    ? " · 启动于 " + process.StartedAt.Value.ToString("HH:mm:ss") : string.Empty);
            }

            switch (process.State)
            {
                case ModuleState.Failed: return "异常退出 / 启动失败，详见日志";
                case ModuleState.NotConfigured: return "未找到程序文件";
                default: return process.Exists ? "未运行" : "未找到程序文件";
            }
        }

        private List<KeyValuePair<string, string>> StateRows()
        {
            var rows = new List<KeyValuePair<string, string>>();

            if (_definition.Kind == ModuleKind.Internal)
            {
                rows.Add(new KeyValuePair<string, string>("配置文件", AppPaths.AutoNoticeConfigFile));
            }
            else
            {
                ModuleProcess process = Target;
                rows.Add(new KeyValuePair<string, string>("程序位置", process != null ? process.ExecutablePath : "（未解析）"));
                rows.Add(new KeyValuePair<string, string>("启动参数",
                    string.IsNullOrEmpty(_definition.Arguments) ? "（无）" : _definition.Arguments));
            }

            if (_definition.Id == Modules.ClassTellId)
            {
                rows.Add(new KeyValuePair<string, string>("数据交换文件", ClassTellBridge.FeedFile));
            }

            rows.Add(new KeyValuePair<string, string>("主题",
                _definition.AppliesTheme ? "跟随 LTFC 主题" : "由程序自己决定（不套用 LTFC 主题）"));

            return rows;
        }

        protected override void OnPaintContent(Graphics g, Rectangle area)
        {
            EnsureLayout();

            // 只在内容区内作画：滚动时卡片不会越界到页面标题上方
            Region clip = g.Clip;
            g.SetClip(area, System.Drawing.Drawing2D.CombineMode.Intersect);
            try
            {
                DrawCard(g, _stateRect);
                DrawSectionTitle(g, "状态", new Rectangle(_stateRect.Left + Theme.S(18), _stateRect.Top + Theme.S(12), Theme.S(80), Theme.S(22)));
                Painting.DrawText(g, DescribeState(), Theme.Small, Theme.ResultText,
                    new Rectangle(_stateRect.Left + Theme.S(110), _stateRect.Top + Theme.S(12), _stateRect.Width - Theme.S(140), Theme.S(22)),
                    StringAlignment.Near);

                List<KeyValuePair<string, string>> rows = StateRows();
                for (int i = 0; i < rows.Count; i++)
                {
                    int top = _stateRect.Top + Theme.S(46) + i * Theme.S(26);
                    Painting.DrawText(g, rows[i].Key, Theme.Small, Theme.TextFaint,
                        new Rectangle(_stateRect.Left + Theme.S(18), top, Theme.S(110), Theme.S(22)), StringAlignment.Near);
                    Painting.DrawText(g, rows[i].Value, Theme.Small, Theme.ResultText,
                        new Rectangle(_stateRect.Left + Theme.S(132), top, Math.Max(Theme.S(80), _stateRect.Width - Theme.S(160)), Theme.S(22)),
                        StringAlignment.Near);
                }

                DrawMiddleCard(g);
                DrawOpsCard(g);
            }
            finally
            {
                g.Clip = clip;
                if (clip != null) clip.Dispose();
            }
        }

        private void DrawOpsCard(Graphics g)
        {
            DrawCard(g, _opsRect);
            DrawSectionTitle(g, "操作", new Rectangle(_opsRect.Left + Theme.S(18), _opsRect.Top + Theme.S(12), Theme.S(80), Theme.S(22)));
            Painting.DrawText(g, "点“启动”会新开一个窗口；已经在运行时只会显示它已有的窗口，不会重复打开",
                Theme.Small, Theme.TextFaint,
                new Rectangle(_opsRect.Left + Theme.S(110), _opsRect.Top + Theme.S(12), _opsRect.Width - Theme.S(140), Theme.S(22)),
                StringAlignment.Near);
        }

        private void DrawMiddleCard(Graphics g)
        {
            DrawCard(g, _middleRect);

            int headerTop = _middleRect.Top + Theme.S(12);
            int textLeft = _middleRect.Left + Theme.S(18);
            int textWidth = Math.Max(Theme.S(120), _middleRect.Width - Theme.S(36));

            switch (_section)
            {
                case ModulePortalSection.Messages:
                case ModulePortalSection.Expired:
                    DrawFeedCard(g, textLeft, headerTop, textWidth);
                    break;

                case ModulePortalSection.Settings:
                    DrawSettingsCard(g, textLeft, headerTop, textWidth);
                    break;

                default:
                    DrawUsageCard(g, textLeft, headerTop, textWidth);
                    break;
            }
        }

        private void DrawFeedCard(Graphics g, int textLeft, int headerTop, int textWidth)
        {
            bool messages = _section == ModulePortalSection.Messages;
            DrawSectionTitle(g, messages ? "消息列表" : "过期列表",
                new Rectangle(textLeft, headerTop, Theme.S(110), Theme.S(22)));

            ClassTellFeed feed = ClassTellBridge.Read();
            IList<ClassTellMessage> list = messages ? feed.Messages : feed.Expired;
            string updated = string.IsNullOrEmpty(feed.Updated) ? "尚未同步" : "更新于 " + feed.Updated;

            Painting.DrawText(g, updated + " · 共 " + list.Count + " 条", Theme.Small, Theme.TextMuted,
                new Rectangle(textLeft + Theme.S(120), headerTop, Math.Max(Theme.S(60), textWidth - Theme.S(120)), Theme.S(22)),
                StringAlignment.Near);

            int top = _middleRect.Top + Theme.S(46);
            int available = Math.Max(Theme.S(20), _middleRect.Bottom - top - Theme.S(12));

            if (list.Count == 0)
            {
                Painting.DrawTextWrapped(g, FeedEmptyHint(feed, messages), Theme.Small, Theme.TextFaint,
                    new Rectangle(textLeft, top, textWidth, available), 0);
                return;
            }

            int rowHeight = Theme.S(26);
            int max = Math.Max(1, available / rowHeight);
            for (int i = 0; i < list.Count && i < max; i++)
            {
                Painting.DrawText(g, list[i].Line, Theme.Small, Theme.Text,
                    new Rectangle(textLeft, top + i * rowHeight, textWidth, Theme.S(22)), StringAlignment.Near);
            }
        }

        private string FeedEmptyHint(ClassTellFeed feed, bool messages)
        {
            if (feed.Notes.Count > 0) return string.Join(Environment.NewLine, feed.Notes.ToArray());
            return (messages
                       ? "这里显示 ClassTell 打开之后收到的指令邮件。"
                       : "这里显示 ClassTell 打开之前就已经存在的未读邮件。")
                   + Environment.NewLine + Environment.NewLine
                   + "当前还没有读到数据，数据文件是：" + Environment.NewLine + feed.Path + Environment.NewLine + Environment.NewLine
                   + "先点下面的“启动（新窗口）”打开 ClassTell，这里会随它的同步结果显示。";
        }

        private void DrawSettingsCard(Graphics g, int textLeft, int headerTop, int textWidth)
        {
            DrawSectionTitle(g, "设置", new Rectangle(textLeft, headerTop, Theme.S(80), Theme.S(22)));
            Painting.DrawText(g, "缺少设置界面的模块，设置项由 LTFC 提供", Theme.Small, Theme.TextMuted,
                new Rectangle(textLeft + Theme.S(90), headerTop, Math.Max(Theme.S(60), textWidth - Theme.S(90)), Theme.S(22)),
                StringAlignment.Near);

            DrawSwitchRow(g, 0, "启用该模块", "禁用后入口仍然可见，但不能启动");
            DrawSwitchRow(g, 1, "随主程序启动", "LTFC 启动时自动打开它的窗口");

            if (_soundSwitch != null)
            {
                DrawSwitchRow(g, 2, "提醒时播放提示音", "到点提醒时播放系统提示音");
            }
        }

        private void DrawSwitchRow(Graphics g, int index, string label, string hint)
        {
            int top = _middleRect.Top + Theme.S(50) + index * Theme.S(52);
            int textWidth = Math.Max(Theme.S(120), _middleRect.Width - Theme.S(150));

            Painting.DrawText(g, label, Theme.Body, Theme.Text,
                new Rectangle(_middleRect.Left + Theme.S(18), top, textWidth, Theme.S(22)), StringAlignment.Near);
            Painting.DrawText(g, hint, Theme.Small, Theme.TextFaint,
                new Rectangle(_middleRect.Left + Theme.S(18), top + Theme.S(22), textWidth, Theme.S(20)), StringAlignment.Near);
        }

        private void DrawUsageCard(Graphics g, int textLeft, int headerTop, int textWidth)
        {
            DrawSectionTitle(g, "使用说明", new Rectangle(textLeft, headerTop, Theme.S(100), Theme.S(22)));

            int top = _middleRect.Top + Theme.S(46);
            int available = Math.Max(Theme.S(20), _middleRect.Bottom - top - Theme.S(12));

            var lines = new List<string>();
            lines.Add(_definition.Description);

            if (_definition.Pages.Count > 0)
            {
                var names = new List<string>();
                foreach (ModulePageDefinition page in _definition.Pages) names.Add(page.Title);
                lines.Add("侧边栏分页：" + string.Join(" / ", names.ToArray()));
            }

            lines.Add(_definition.AppliesTheme
                ? "界面主题与 LTFC 保持一致；子程序是独立进程，它的异常不会影响主程序。"
                : "该程序使用自己的界面风格（不套用 LTFC 主题），点“启动”会新开一个独立窗口。");
            lines.Add("重复点击“启动”只会显示已经打开的窗口，不会重复启动。");

            Painting.DrawTextWrapped(g, string.Join(Environment.NewLine, lines.ToArray()),
                Theme.Small, Theme.TextMuted, new Rectangle(textLeft, top, textWidth, available), 4);
        }
    }
}
