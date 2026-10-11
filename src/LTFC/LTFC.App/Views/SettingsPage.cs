using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>总设置：界面、启动、子程序、提醒、关于。</summary>
    internal sealed class SettingsPage : PageBase
    {
        private enum RowKind
        {
            Section,
            Switch,
            Buttons,
            Value,
            Swatches,
            Image
        }

        private sealed class Row
        {
            public RowKind Kind;
            public string Label;
            public string Hint;
            public Control Control;
            public Control Control2;
            public Func<string> ValueProvider;
            public List<UiSwatch> Swatches;
            public int Height;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly Action _onThemeChanged;
        private readonly Action _onModulesChanged;
        private int _contentHeight;

        public SettingsPage(Action onThemeChanged, Action onModulesChanged)
        {
            Key = "settings";
            Title = "总设置";
            Subtitle = "界面外观、启动方式、子程序与提醒设置";
            _onThemeChanged = onThemeChanged;
            _onModulesChanged = onModulesChanged;
            WantsScroll = true;
            BuildRows();
        }

        private void AddSection(string title)
        {
            _rows.Add(new Row { Kind = RowKind.Section, Label = title, Height = Theme.S(44) });
        }

        private UiSwitch AddSwitch(string label, bool value, Action<bool> onChanged, string hint = null)
        {
            var toggle = CreateSwitch(value, (s, e) =>
            {
                bool now = ((UiSwitch)s).Checked;
                if (onChanged != null) onChanged(now);
                Invalidate();
            });

            _rows.Add(new Row { Kind = RowKind.Switch, Label = label, Hint = hint, Control = toggle, Height = Theme.S(52) });
            return toggle;
        }

        private UiButton AddButton(string label, string buttonText, Action onClick, string hint = null, UiButtonKind kind = UiButtonKind.Secondary)
        {
            var button = CreateButton(buttonText, kind, (s, e) => onClick());
            _rows.Add(new Row { Kind = RowKind.Buttons, Label = label, Hint = hint, Control = button, Height = Theme.S(52) });
            return button;
        }

        private void AddButtons(string label, string first, Action onFirst, string second, Action onSecond, string hint = null)
        {
            var a = CreateButton(first, UiButtonKind.Secondary, (s, e) => onFirst());
            UiButton b = null;
            if (!string.IsNullOrEmpty(second)) b = CreateButton(second, UiButtonKind.Secondary, (s, e) => onSecond());
            _rows.Add(new Row { Kind = RowKind.Buttons, Label = label, Hint = hint, Control = a, Control2 = b, Height = Theme.S(52) });
        }

        private void AddValue(string label, Func<string> provider, string hint = null)
        {
            _rows.Add(new Row { Kind = RowKind.Value, Label = label, Hint = hint, ValueProvider = provider, Height = Theme.S(46) });
        }

        /// <summary>在“关于”里显示一张图片（赞赏码）。</summary>
        private void AddImage(string label, string path, string caption, string hint = null)
        {
            var image = new UiImage
            {
                ImagePath = path,
                Caption = caption,
                EmptyText = "图片缺失：" + path
            };

            Controls.Add(image);
            _rows.Add(new Row { Kind = RowKind.Image, Label = label, Hint = hint, Control = image, Height = Theme.S(280) });
        }

        private void AddSwatches(string label, IEnumerable<ThemePalette> palettes, Func<string> currentKey, Action<string> onPick, string hint = null)
        {
            var row = new Row { Kind = RowKind.Swatches, Label = label, Hint = hint, Swatches = new List<UiSwatch>(), Height = Theme.S(58) };
            foreach (ThemePalette palette in palettes)
            {
                string key = palette.Key;
                var swatch = new UiSwatch
                {
                    Palette = palette,
                    Selected = string.Equals(key, currentKey(), StringComparison.OrdinalIgnoreCase)
                };
                swatch.Click += (s, e) =>
                {
                    onPick(key);
                    foreach (UiSwatch other in row.Swatches)
                    {
                        other.Selected = other == swatch;
                        other.Invalidate();
                    }
                    Invalidate();
                };
                Controls.Add(swatch);
                row.Swatches.Add(swatch);
            }
            _rows.Add(row);
        }

        /// <summary>
        /// 只呈现、不可点击的配色说明行：同时展示高对比度使用的两个颜色
        /// （粉 = 可交互区域的底色，蓝 = 交互结果的呈现）。说明行不写入任何设置。
        /// </summary>
        private void AddLegend(string label, string hint)
        {
            var row = new Row { Kind = RowKind.Swatches, Label = label, Hint = hint, Swatches = new List<UiSwatch>(), Height = Theme.S(58) };
            ThemePalette[] pair =
            {
                Theme.FindPalette(Theme.HighContrastPaletteKeyB),   // 粉：可交互区域的底色
                Theme.FindPalette(Theme.HighContrastPaletteKey)     // 蓝：交互结果的呈现
            };

            foreach (ThemePalette palette in pair)
            {
                var swatch = new UiSwatch { Palette = palette, Selectable = false };
                Controls.Add(swatch);
                row.Swatches.Add(swatch);
            }

            _rows.Add(row);
        }

        private void BuildRows()
        {
            var old = new List<Control>();
            foreach (Control control in Controls) old.Add(control);
            Controls.Clear();
            foreach (Control control in old) control.Dispose();
            _rows.Clear();

            // ---------- 界面外观 ----------
            AddSection("界面外观");

            AddSwitch("深色模式", Settings.DarkMode, v =>
            {
                Settings.DarkMode = v;
                ApplyThemeAndRefresh();
            }, Theme.HighContrast ? "高对比度模式下固定使用浅色背景方案" : "跟随设置立即生效");

            AddSwitch("高对比度模式", Settings.HighContrast, v =>
            {
                Settings.HighContrast = v;
                ApplyThemeAndRefresh();
                RebuildRows();
            }, "判断框、确认框、选择框都使用明显的高对比度样式");

            if (Settings.HighContrast)
            {
                // 高对比度的两个颜色不是“可选配色”，而是同时使用的一套强调体系：
                // 粉色 = 可交互区域的底色，蓝色 = 交互结果的呈现。这里只做呈现，不给用户选择，
                // 因此也不会写入 Settings.AccentKey。
                AddLegend("高对比度配色", Theme.HighContrastPairHint);
            }
            else
            {
                AddSwatches("强调色", NormalPalettes(), () => Settings.AccentKey,
                    key => { Settings.AccentKey = key; ApplyThemeAndRefresh(); });
            }

            AddButtons("字号",
                "A −", () => ChangeFontSize(-0.5f),
                "A ＋", () => ChangeFontSize(0.5f),
                "当前 " + Settings.FontSize.ToString("0.#") + " pt（范围 " + Theme.MinFontSize + " ~ " + Theme.MaxFontSize + " pt）");

            // ---------- 启动 ----------
            AddSection("启动");

            AddSwitch("开机自动启动 LTFC", Startup.IsEnabled, v =>
            {
                string message;
                if (!Startup.Apply(v, out message)) Ask.Warn(this, Theme.ShortName, message);
                Settings.RunAtStartup = v;
            }, "写入当前用户的启动项（无需管理员权限）");

            AddSwitch("启动后驻留托盘", Settings.StartMinimized, v => Settings.StartMinimized = v,
                "开机自启动时直接进入托盘，不弹出主窗口");

            AddSwitch("关闭窗口时驻留托盘", Settings.CloseToTray, v => Settings.CloseToTray = v,
                "关闭窗口不退出程序，继续在托盘中运行");

            AddSwitch("随主程序启动子程序", Settings.AutoStartModules, v => Settings.AutoStartModules = v,
                "LTFC 启动时自动拉起已启用的子程序");

            // ---------- 子程序 ----------
            AddSection("子程序");

            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                if (definition.Kind != ModuleKind.ExternalProcess) continue;
                string id = definition.Id;
                AddSwitch("启用 " + definition.DisplayName, Settings.ModuleEnabled(id), v =>
                {
                    Settings.SetModuleEnabled(id, v);
                    Modules.RefreshAll();
                    if (_onModulesChanged != null) _onModulesChanged();
                });

                AddButton(definition.DisplayName + " 程序位置", "选择程序…", () => ChooseModulePath(id, definition.FileName),
                    null, UiButtonKind.Secondary);
                AddValue(definition.DisplayName + " 路径", () =>
                {
                    ModuleProcess process = Modules.Process(id);
                    return process != null ? process.ExecutablePath : "（未配置）";
                });
            }

            // ---------- 定时提醒 ----------
            AddSection("定时提醒（AutoNotice）");

            AddSwitch("启用定时提醒", Settings.AutoNoticeEnabled, v =>
            {
                Settings.AutoNoticeEnabled = v;
                if (ModuleLauncher.AutoNotice != null)
                {
                    if (v) ModuleLauncher.AutoNotice.Start();
                    else ModuleLauncher.AutoNotice.Stop();
                }
            }, "按 command.txt 里的星期与时间发送系统通知");

            AddSwitch("提醒时播放提示音", Settings.AutoNoticeSound, v => Settings.AutoNoticeSound = v);

            AddValue("下次提醒", () => ModuleLauncher.AutoNotice != null ? ModuleLauncher.AutoNotice.NextDueText() : "（未启动）");
            AddValue("配置文件", () => AppPaths.AutoNoticeConfigFile);

            AddButton("提醒配置", "编辑提醒配置…", OpenAutoNoticeConfig, "每行一条：周几 时间 内容（1=周一 … 7=周日）");

            // ---------- 关于 ----------
            AddSection("关于");

            AddValue("软件名称", () => Theme.ProductName);
            AddValue("简称", () => Theme.ShortName);
            AddValue("版本", () => "V" + Theme.VersionText);
            AddValue("数据目录", () => AppPaths.DataDir);
            AddValue("日志文件", () => AppLog.FilePath);
            AddValue("内置模块目录", () => AppPaths.ModulesDir);

            AddImage("赞赏码", AppPaths.RewardQrFile, "扫码赞赏 · 支持 LTFC 继续更新", "谢谢你的支持，赞赏完全自愿");

            AddButtons("赞赏码",
                "打开图片", OpenRewardQr,
                "复制到桌面", SaveRewardQr,
                null);

            AddButtons("维护",
                "打开数据目录", () => OpenDataDir(),
                "查看日志", () => ShowLog(),
                null);

            AddButton("恢复默认", "恢复默认设置…", RestoreDefaults, "只重置 LTFC 的设置，不影响子程序自己的配置", UiButtonKind.Danger);
        }

        private static List<ThemePalette> NormalPalettes()
        {
            // 高对比度专用的两个颜色不参与“强调色”选择，统一由 Theme 提供（自测也会校验这一点）
            return new List<ThemePalette>(Theme.SelectablePalettes);
        }

        private void RebuildRows()
        {
            BuildRows();
            _laidOutSize = Size.Empty;
            Relayout();
        }

        private void ChangeFontSize(float delta)
        {
            float size = Math.Max(Theme.MinFontSize, Math.Min(Theme.MaxFontSize, Settings.FontSize + delta));
            Settings.FontSize = size;
            ApplyThemeAndRefresh();
            RebuildRows();
        }

        private void ApplyThemeAndRefresh()
        {
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);
            if (_onThemeChanged != null) _onThemeChanged();
            Theme.InvalidateTree(this);
            Invalidate();
        }

        private void ChooseModulePath(string id, string fallbackFileName)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择程序文件";
                dialog.Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                try
                {
                    ModuleDefinition definition = Modules.Find(id);
                    string current = definition != null ? Modules.ResolveExecutablePath(definition) : string.Empty;
                    string dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
                }
                catch (Exception)
                {
                }

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                Settings.SetModulePath(id, dialog.FileName);
                Modules.RefreshAll();
                if (_onModulesChanged != null) _onModulesChanged();
                RebuildRows();
            }
        }

        private void OpenAutoNoticeConfig()
        {
            try
            {
                // 配置文件固定为 command.txt（默认 src\AutoNotice\command.txt，发布后是程序目录下的 AutoNotice\command.txt）
                AutoNoticeService.EnsureConfigFile();
                string path = AppPaths.AutoNoticeConfigFile;
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");

                if (ModuleLauncher.AutoNotice != null)
                {
                    // 编辑完成后由用户点“重新读取”，这里先记录一次路径，便于诊断
                    AppLog.Info("打开 AutoNotice 配置：" + path);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("打开提醒配置失败", ex);
                Ask.Warn(this, Theme.ShortName, "无法打开提醒配置文件：" + ex.Message);
            }
        }

        /// <summary>用系统默认看图程序打开赞赏码。</summary>
        private void OpenRewardQr()
        {
            try
            {
                string path = AppPaths.RewardQrFile;
                if (!File.Exists(path))
                {
                    Ask.Warn(this, "赞赏码", "没有找到赞赏码图片，期望位置：", path);
                    return;
                }

                System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                AppLog.Exception_("打开赞赏码失败", ex);
                Ask.Warn(this, "赞赏码", "无法打开赞赏码图片：" + ex.Message);
            }
        }

        /// <summary>把赞赏码复制到桌面，方便分享给同事。</summary>
        private void SaveRewardQr()
        {
            try
            {
                string path = AppPaths.RewardQrFile;
                if (!File.Exists(path))
                {
                    Ask.Warn(this, "赞赏码", "没有找到赞赏码图片，期望位置：", path);
                    return;
                }

                string target = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Theme.ShortName + "-赞赏码.jpg");
                File.Copy(path, target, true);
                Ask.Info(this, "赞赏码", "已复制到桌面：", target);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("复制赞赏码失败", ex);
                Ask.Warn(this, "赞赏码", "无法复制赞赏码图片：" + ex.Message);
            }
        }

        private void OpenDataDir()
        {
            try
            {
                AppPaths.EnsureDataDir();
                System.Diagnostics.Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
            }
            catch (Exception ex)
            {
                AppLog.Exception_("打开数据目录失败", ex);
                Ask.Warn(this, Theme.ShortName, "无法打开数据目录：" + ex.Message);
            }
        }

        private void ShowLog()
        {
            string path = AppLog.FilePath;
            string text = AppLog.ReadTail(path, 32 * 1024);
            if (text.Length > 4000) text = text.Substring(text.Length - 4000);
            if (string.IsNullOrEmpty(text)) text = "（暂无日志）";

            Ask.Custom(this, new DialogRequest
            {
                Title = "LTFC 日志",
                Message = "日志文件：" + path,
                Detail = text,
                Tone = DialogTone.Info,
                Buttons = new List<string> { "关闭" }
            });
        }

        private void RestoreDefaults()
        {
            if (!Ask.Confirm(this, "恢复默认设置",
                "确定要把 LTFC 的设置恢复为默认值吗？",
                "界面外观、启动项、子程序启用状态与提醒开关都会重置；子程序自己的配置与日志不会删除。",
                "恢复默认", "取消"))
            {
                return;
            }

            Settings.HighContrast = false;
            Settings.DarkMode = false;
            Settings.AccentKey = Theme.DefaultPaletteKey;
            Settings.FontSize = Theme.RecommendedFontSize;
            Settings.StartMinimized = false;
            Settings.CloseToTray = true;
            Settings.AutoStartModules = false;
            Settings.AutoNoticeEnabled = true;
            Settings.AutoNoticeSound = false;

            string message;
            Startup.Apply(false, out message);
            Settings.RunAtStartup = false;

            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);
            if (_onThemeChanged != null) _onThemeChanged();
            RebuildRows();
            Ask.Info(this, Theme.ShortName, "已恢复默认设置。");
        }

        private Size _laidOutSize;

        protected override int ContentHeight { get { return Math.Max(ContentArea.Height, _contentHeight); } }

        protected override void LayoutChildren()
        {
            Rectangle area = ContentArea;
            _laidOutSize = Size;

            using (Graphics g = SafeGraphics())
            {
                Font labelFont = Theme.Body;
                Font hintFont = Theme.Small;
                Font sectionFont = Theme.Font(0.5f, FontStyle.Bold);
                int labelHeight = Theme.Line(labelFont);
                int hintHeight = Theme.Line(hintFont);
                int sectionHeight = Theme.Line(sectionFont) + Theme.S(16);
                int rowPadding = Theme.S(12);

                // 行高按字体度量计算（高 DPI / 大字号下不会重叠）
                foreach (Row row in _rows)
                {
                    if (row.Kind == RowKind.Section)
                    {
                        row.Height = sectionHeight;
                    }
                    else
                    {
                        int height = labelHeight + rowPadding;
                        if (!string.IsNullOrEmpty(row.Hint)) height += hintHeight;
                        if (row.Kind == RowKind.Swatches) height = Math.Max(height, Theme.S(42) + rowPadding);
                        if (row.Kind == RowKind.Buttons) height = Math.Max(height, Theme.S(38) + rowPadding);
                        if (row.Kind == RowKind.Image) height = Math.Max(height, Theme.S(280));
                        row.Height = height;
                    }
                }

                int y = area.Top + Theme.S(12) - ScrollOffset;
                int right = area.Right - Theme.S(18);
                int labelWidth = Math.Max(Theme.S(180), (int)(area.Width * 0.40));

                foreach (Row row in _rows)
                {
                    if (row.Kind == RowKind.Switch && row.Control != null)
                    {
                        row.Control.SetBounds(right - row.Control.Width, y + (row.Height - row.Control.Height) / 2,
                            row.Control.Width, row.Control.Height);
                    }
                    else if (row.Kind == RowKind.Buttons)
                    {
                        int buttonHeight = Theme.S(38);
                        int gap = Theme.S(8);
                        int buttonTop = y + (row.Height - buttonHeight) / 2;

                        if (row.Control2 != null)
                        {
                            var second = (UiButton)row.Control2;
                            int width2 = Math.Max(Theme.S(96), second.MeasureWidth(g, second.Text));
                            width2 = Math.Min(width2, Math.Max(Theme.S(96), right - labelWidth - Theme.S(140)));
                            second.SetBounds(right - width2, buttonTop, width2, buttonHeight);
                            right -= width2 + gap;
                        }

                        var first = (UiButton)row.Control;
                        int width1 = Math.Max(Theme.S(96), first.MeasureWidth(g, first.Text));
                        width1 = Math.Min(width1, Math.Max(Theme.S(96), right - labelWidth - Theme.S(20)));
                        first.SetBounds(right - width1, buttonTop, width1, buttonHeight);
                        right = area.Right - Theme.S(18);
                    }
                    else if (row.Kind == RowKind.Swatches && row.Swatches != null)
                    {
                        int x = right;
                        for (int i = row.Swatches.Count - 1; i >= 0; i--)
                        {
                            UiSwatch swatch = row.Swatches[i];
                            x -= swatch.Width;
                            swatch.SetBounds(x, y + (row.Height - swatch.Height) / 2, swatch.Width, swatch.Height);
                            x -= Theme.S(8);
                        }
                    }
                    else if (row.Kind == RowKind.Image && row.Control != null)
                    {
                        int width = Math.Min(Theme.S(240), Math.Max(Theme.S(120), area.Width - labelWidth - Theme.S(40)));
                        row.Control.SetBounds(right - width, y + Theme.S(4), width, row.Height - Theme.S(8));
                    }

                    y += row.Height;
                }

                _contentHeight = (y - (area.Top + Theme.S(12) - ScrollOffset)) + Theme.S(24);

                // 滚动时把完全落在内容区之外的子控件隐藏，避免它们“跑到”页面标题上方
                foreach (Row row in _rows)
                {
                    ClampVisibility(row.Control, area);
                    ClampVisibility(row.Control2, area);
                    if (row.Swatches == null) continue;
                    foreach (UiSwatch swatch in row.Swatches) ClampVisibility(swatch, area);
                }
            }
        }

        /// <summary>只有完整落在内容区内的子控件才显示（滚动时不会越界到标题区域）。</summary>
        private static void ClampVisibility(Control control, Rectangle area)
        {
            if (control == null) return;
            control.Visible = area.Contains(control.Bounds);
        }

        private void EnsureLayout()
        {
            if (_laidOutSize != Size) LayoutChildren();
        }

        protected override void OnPaintContent(Graphics g, Rectangle area)
        {
            EnsureLayout();

            Font labelFont = Theme.Body;
            Font hintFont = Theme.Small;
            int labelHeight = Theme.Line(labelFont);
            int hintHeight = Theme.Line(hintFont);

            // 只在内容区内作画：滚动时文字不会越界到页面标题上方
            Region clip = g.Clip;
            g.SetClip(area, System.Drawing.Drawing2D.CombineMode.Intersect);
            try
            {
                int y = area.Top + Theme.S(12) - ScrollOffset;
                int labelWidth = Math.Max(Theme.S(180), (int)(area.Width * 0.40));

                foreach (Row row in _rows)
                {
                    if (row.Kind == RowKind.Section)
                    {
                        Font sectionFont = Theme.Font(0.5f, FontStyle.Bold);
                        int titleHeight = Theme.Line(sectionFont);
                        var titleRect = new Rectangle(area.Left + Theme.S(4), y + row.Height / 2 - titleHeight / 2,
                            Math.Max(Theme.S(120), Theme.S(240)), titleHeight);
                        DrawSectionTitle(g, row.Label, titleRect);

                        // 分隔线从标题右侧开始画，避免高对比度下穿过文字
                        int lineLeft = titleRect.Left + (int)Math.Ceiling(Painting.Measure(g, row.Label ?? string.Empty, sectionFont).Width) + Theme.S(12);
                        if (lineLeft < area.Right)
                        {
                            using (var pen = new Pen(Theme.Outline, Theme.BorderWidth))
                            {
                                int lineY = y + row.Height / 2;
                                g.DrawLine(pen, lineLeft, lineY, area.Right, lineY);
                            }
                        }

                        y += row.Height;
                        continue;
                    }

                    bool hasHint = !string.IsNullOrEmpty(row.Hint);
                    int contentHeight = labelHeight + (hasHint ? hintHeight : 0);
                    bool imageRow = row.Kind == RowKind.Image;
                    int textTop = imageRow
                        ? y + Theme.S(10)                                   // 图片行：文字贴顶，与图片一起从上往下排
                        : y + Math.Max(0, (row.Height - contentHeight) / 2);

                    Painting.DrawText(g, row.Label, labelFont, Theme.Text,
                        new Rectangle(area.Left + Theme.S(4), textTop, labelWidth, labelHeight), StringAlignment.Near);

                    if (hasHint)
                    {
                        Painting.DrawText(g, row.Hint, hintFont, Theme.TextFaint,
                            new Rectangle(area.Left + Theme.S(4), textTop + labelHeight,
                                Math.Max(Theme.S(120), area.Width - labelWidth - Theme.S(20)), hintHeight),
                            StringAlignment.Near);
                    }

                    if (row.Kind == RowKind.Value && row.ValueProvider != null)
                    {
                        string value = string.Empty;
                        try { value = row.ValueProvider() ?? string.Empty; }
                        catch (Exception) { }
                        // 值 = 交互结果（路径 / 版本 / 下次提醒等）：高对比度下用蓝色呈现（粉=可交互区域底色）
                        Painting.DrawText(g, value, hintFont, Theme.ResultText,
                            new Rectangle(area.Left + labelWidth, textTop, Math.Max(Theme.S(80), area.Width - labelWidth - Theme.S(10)), labelHeight),
                            StringAlignment.Far);
                    }

                    y += row.Height;
                }
            }
            finally
            {
                g.Clip = clip;
                if (clip != null) clip.Dispose();
            }
        }

        public override void RefreshState()
        {
            _laidOutSize = Size.Empty;
            Relayout();
        }
    }
}
