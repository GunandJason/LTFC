using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>首页：软件概览、模块状态与快捷操作。</summary>
    internal sealed class HomePage : PageBase
    {
        private const string HomeKey = "home";

        private readonly Action<string> _openPage;
        private readonly Action _refresh;
        private readonly List<UiButton> _heroButtons = new List<UiButton>();
        private readonly Dictionary<string, UiButton> _moduleButtons = new Dictionary<string, UiButton>(StringComparer.OrdinalIgnoreCase);

        private int _heroHeight;
        private int _rowHeight;
        private int _rowGap;
        private int _contentHeight;

        public HomePage(Action<string> openPage, Action refresh)
        {
            Key = HomeKey;
            Title = Theme.ShortName;
            Subtitle = Theme.ProductName + " · 一个入口，集中管理课堂常用小工具";
            _openPage = openPage;
            _refresh = refresh;
            WantsScroll = true;

            _heroButtons.Add(CreateButton("全部启动", UiButtonKind.Primary, (s, e) => OnStartAll()));
            _heroButtons.Add(CreateButton("全部停止", UiButtonKind.Secondary, (s, e) => OnStopAll()));
            _heroButtons.Add(CreateButton("数据目录", UiButtonKind.Ghost, (s, e) => OpenDataDir()));

            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                string id = definition.Id;
                if (definition.Kind == ModuleKind.ExternalProcess)
                {
                    _moduleButtons[id] = CreateButton("启动", UiButtonKind.Primary, (s, e) => ToggleModule(id));
                    _moduleButtons[id + ":open"] = CreateButton("管理", UiButtonKind.Ghost, (s, e) => OpenPage(id));
                }
                else
                {
                    _moduleButtons[id] = CreateButton("启动", UiButtonKind.Primary, (s, e) => ToggleModule(id));
                    _moduleButtons[id + ":open"] = CreateButton("打开页面", UiButtonKind.Ghost, (s, e) => OpenPage(id));
                }
            }
        }

        private void OpenPage(string key)
        {
            if (_openPage != null) _openPage(key);
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

        private void OnStartAll()
        {
            IList<string> failures = Modules.StartAllEnabled();
            if (failures.Count > 0)
            {
                var list = new List<string>(failures);
                Ask.Warn(this, "启动子程序", "以下子程序未能启动：", string.Join(Environment.NewLine + Environment.NewLine, list.ToArray()));
            }
            AfterModuleChange();
        }

        private void OnStopAll()
        {
            Modules.StopAll();
            AfterModuleChange();
        }

        private void ToggleModule(string id)
        {
            ModuleDefinition definition = Modules.Find(id);
            if (definition == null) return;

            // 启动 / 显示：没有运行时新开一个窗口；已经在运行时只显示它已有的窗口
            string error = ModuleLauncher.LaunchOrShow(definition);
            if (!string.IsNullOrEmpty(error)) Ask.Warn(this, definition.DisplayName, error);

            AfterModuleChange();
        }

        private void AfterModuleChange()
        {
            if (_refresh != null) _refresh();
            RefreshState();
        }

        public override void RefreshState()
        {
            foreach (KeyValuePair<string, UiButton> pair in _moduleButtons)
            {
                int separator = pair.Key.IndexOf(':');
                string id = separator > 0 ? pair.Key.Substring(0, separator) : pair.Key;
                ModuleProcess process = Modules.Process(id);

                if (separator > 0)
                {
                    pair.Value.IsEnabledVisual = !(process != null && !process.IsRunning && !process.Exists);
                    pair.Value.Invalidate();
                    continue;
                }

                if (process == null) continue;
                bool running = process.IsRunning;
                pair.Value.Text = running ? "显示窗口" : "启动";
                pair.Value.Kind = running ? UiButtonKind.Secondary : UiButtonKind.Primary;
                pair.Value.IsEnabledVisual = process.Exists || running;
                pair.Value.Invalidate();
            }

            Relayout();
        }

        protected override int ContentHeight { get { return Math.Max(ContentArea.Height, _contentHeight); } }

        protected override void LayoutChildren()
        {
            Rectangle area = ContentArea;
            Font heroTitleFont = Theme.Font(1.5f, FontStyle.Bold);
            Font smallFont = Theme.Small;
            int smallHeight = Theme.Line(smallFont);

            _heroHeight = Theme.S(20) + Theme.Line(heroTitleFont) + smallHeight * 2 + Theme.S(20);
            _rowHeight = Theme.S(16) + Theme.Line(Theme.Font(0.8f, FontStyle.Bold)) + smallHeight * 2 + Theme.S(16);
            _rowGap = Theme.S(10);

            int buttonHeight = Theme.S(38);
            int gap = Theme.S(10);

            // 概览卡片上的操作按钮：右对齐、垂直居中
            Rectangle hero = new Rectangle(area.Left, area.Top + Theme.S(12) - ScrollOffset, area.Width, _heroHeight);
            int x = hero.Right - Theme.S(18);
            int buttonY = hero.Top + (_heroHeight - buttonHeight) / 2;
            for (int i = _heroButtons.Count - 1; i >= 0; i--)
            {
                int width = Theme.S(112);
                x -= width;
                _heroButtons[i].SetBounds(x, buttonY, width, buttonHeight);
                x -= gap;
            }

            int y = Theme.S(12) + _heroHeight + Theme.S(16);
            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                int rowTop = area.Top + y - ScrollOffset;
                bool external = definition.Kind == ModuleKind.ExternalProcess;

                UiButton primary;
                if (_moduleButtons.TryGetValue(definition.Id, out primary))
                {
                    int width = Theme.S(104);
                    primary.SetBounds(area.Right - Theme.S(18) - width, rowTop + (_rowHeight - buttonHeight) / 2, width, buttonHeight);
                }

                UiButton open;
                if (_moduleButtons.TryGetValue(definition.Id + ":open", out open))
                {
                    int width = external ? Theme.S(104) : Theme.S(140);
                    int right = area.Right - Theme.S(18);
                    if (external) right -= Theme.S(104) + Theme.S(8);
                    open.SetBounds(right - width, rowTop + (_rowHeight - buttonHeight) / 2, width, buttonHeight);
                }

                y += _rowHeight + _rowGap;
            }

            _contentHeight = y + Theme.S(20);
        }

        protected override void OnPaintContent(Graphics g, Rectangle area)
        {
            Font heroTitleFont = Theme.Font(1.5f, FontStyle.Bold);
            Font smallFont = Theme.Small;
            int smallHeight = Theme.Line(smallFont);

            int y = Theme.S(12) - ScrollOffset;

            var hero = new Rectangle(area.Left, area.Top + y, area.Width, _heroHeight);
            DrawCard(g, hero);
            int textWidth = Math.Max(Theme.S(200), hero.Width - Theme.S(440));
            int textTop = hero.Top + Theme.S(18);
            Painting.DrawText(g, Theme.ProductName, heroTitleFont, Theme.Text,
                new Rectangle(hero.Left + Theme.S(18), textTop, textWidth, Theme.Line(heroTitleFont)), StringAlignment.Near);
            textTop += Theme.Line(heroTitleFont);
            Painting.DrawText(g, "简称 " + Theme.ShortName + " · 版本 V" + Theme.VersionText, smallFont, Theme.TextMuted,
                new Rectangle(hero.Left + Theme.S(18), textTop, textWidth, smallHeight), StringAlignment.Near);
            textTop += smallHeight + Theme.S(4);
            // 运行摘要 = 操作结果的呈现：高对比度下用蓝色（粉 = 可交互区域的底色）
            Painting.DrawText(g, RunningSummary(), smallFont, Theme.ResultText,
                new Rectangle(hero.Left + Theme.S(18), textTop, textWidth, smallHeight), StringAlignment.Near);

            y += _heroHeight + Theme.S(16);

            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                var row = new Rectangle(area.Left, area.Top + y, area.Width, _rowHeight);
                DrawCard(g, row);

                int rowTextWidth = Math.Max(Theme.S(160), row.Width - Theme.S(300));
                int rowTop = row.Top + Theme.S(14);
                Painting.DrawText(g, definition.DisplayName, Theme.Font(0.8f, FontStyle.Bold), Theme.Text,
                    new Rectangle(row.Left + Theme.S(18), rowTop, rowTextWidth, Theme.Line(Theme.Font(0.8f, FontStyle.Bold))), StringAlignment.Near);
                rowTop += Theme.Line(Theme.Font(0.8f, FontStyle.Bold));
                Painting.DrawText(g, DescribeState(definition), smallFont, StateColor(definition),
                    new Rectangle(row.Left + Theme.S(18), rowTop, rowTextWidth, smallHeight), StringAlignment.Near);
                rowTop += smallHeight;
                Painting.DrawText(g, definition.Description, smallFont, Theme.TextFaint,
                    new Rectangle(row.Left + Theme.S(18), rowTop, rowTextWidth, smallHeight), StringAlignment.Near);

                y += _rowHeight + _rowGap;
            }
        }

        private static Color StateColor(ModuleDefinition definition)
        {
            if (definition.Kind == ModuleKind.Internal) return Theme.TextMuted;
            ModuleProcess process = Modules.Process(definition.Id);
            if (process == null) return Theme.TextMuted;
            if (process.IsRunning) return Theme.Ok;
            if (process.State == ModuleState.Failed) return Theme.Danger;
            return process.Exists ? Theme.TextMuted : Theme.Danger;
        }

        private static string DescribeState(ModuleDefinition definition)
        {
            if (definition.Kind == ModuleKind.Internal)
            {
                if (definition.Id == Modules.AutoNoticeId && ModuleLauncher.AutoNotice != null)
                {
                    return Settings.AutoNoticeEnabled
                        ? "内置模块 · 运行中 · 下次提醒 " + ModuleLauncher.AutoNotice.NextDueText()
                        : "内置模块 · 已停用（打开“设置”页启用）";
                }

                return "内置模块 · 由主程序直接提供";
            }

            ModuleProcess process = Modules.Process(definition.Id);
            if (process == null) return "未配置";
            switch (process.State)
            {
                case ModuleState.Running:
                    return "运行中" + (process.StartedAt.HasValue ? "（启动于 " + process.StartedAt.Value.ToString("HH:mm:ss") + "）" : string.Empty);
                case ModuleState.Failed:
                    return "启动失败或异常退出，详见日志";
                case ModuleState.NotConfigured:
                    return "未找到程序文件";
                default:
                    return process.Exists ? "未运行" : "未找到程序文件";
            }
        }

        private static string RunningSummary()
        {
            int running = 0;
            int total = 0;
            foreach (ModuleProcess process in Modules.ExternalProcesses)
            {
                total++;
                if (process.IsRunning) running++;
            }
            return running + " / " + total + " 个子程序正在运行";
        }
    }
}
