using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// LTFC 主窗口：左侧固定侧边栏（LTFC / ClassTell / OpenChoose / AutoNotice / 总设置），
    /// 右侧为功能页面。子程序以独立进程运行，由本窗口负责启动与管理。
    /// </summary>
    internal sealed class ShellForm : Form
    {
        private const string HomeKey = "home";
        private const string SettingsKey = "settings";

        private readonly SideNav _nav;
        private readonly Dictionary<string, PageBase> _pages = new Dictionary<string, PageBase>(StringComparer.OrdinalIgnoreCase);
        private readonly Notifier _notifier;
        private readonly Timer _statusTimer;

        /// <summary>切页时重复枚举进程很费时间，用节流保证点侧边栏立刻有反应。</summary>
        private const int StateRefreshIntervalMs = 400;

        private string _currentKey = HomeKey;
        private bool _hiddenToTray;
        private bool _exiting;

        /// <summary>上一次扫描子程序状态的时间（Environment.TickCount）。</summary>
        private int _lastStateRefreshTick;

        /// <summary>正在后台打开的子程序（避免短时间内重复启动）。</summary>
        private readonly HashSet<string> _launching = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _launchGate = new object();

        public ShellForm()
        {
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);
            Settings.EnsureDefaults();
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);

            Text = Theme.ProductName + "（" + Theme.ShortName + "）V" + Theme.VersionText;
            Icon = IconFactory.AppIcon;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Window;
            DoubleBuffered = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(Theme.S(880), Theme.S(600));

            _nav = new SideNav();
            _nav.SelectionChanged += SwitchPage;

            _nav.AddTitle(Theme.ShortName, HomeKey);
            _nav.AddSeparator();

            // 侧边栏结构：模块分组 + 该模块的分页（ClassTell：消息/过期；其他：设置）
            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                _nav.AddGroup(definition.DisplayName, definition.Id);
                foreach (ModulePageDefinition page in definition.Pages)
                {
                    _nav.AddChild(definition.Id, page.Title, page.FullKey(definition.Id));
                }

                _nav.ExpandGroup(definition.Id, definition.Id == Modules.ClassTellId);
            }

            _nav.AddSeparator();
            _nav.AddSettings("总设置", SettingsKey);

            Controls.Add(_nav);

            RegisterPage(new HomePage(SwitchPage, OnShellRefresh));
            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                RegisterPage(new ModulePortalPage(definition, ModulePortalSection.Overview, OnShellRefresh));
                foreach (ModulePageDefinition page in definition.Pages)
                {
                    RegisterPage(new ModulePortalPage(definition, SectionForKey(page.Key), OnShellRefresh));
                }
            }

            RegisterPage(new SettingsPage(OnThemeChangedByPage, OnShellRefresh));
            _nav.SelectByKey(HomeKey, false);

            _notifier = new Notifier(this, Theme.ShortName + " V" + Theme.VersionText);
            _notifier.ShowRequested += RestoreWindow;
            _notifier.ExitRequested += ExitFromTray;

            // AutoNotice（内置模块）：读取 command.txt，到点弹系统通知
            AutoNoticeService autoNotice = ServiceHolder.Current;
            autoNotice.Triggered += OnAutoNoticeTriggered;
            ModuleLauncher.Attach(this);
            ServiceHolder.Ensure();

            _statusTimer = new Timer { Interval = 2000 };
            _statusTimer.Tick += (s, e) => RefreshModuleStates(true);
            _statusTimer.Start();

            Theme.Changed += OnThemeChanged;

            ClientSize = new Size(Theme.S(1180), Theme.S(760));
        }

        private void RegisterPage(PageBase page)
        {
            page.Dock = DockStyle.None;
            page.Visible = false;
            _pages[page.Key] = page;
            Controls.Add(page);
        }

        // ---------- 生命周期 ----------
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Rectangle work = Screen.FromControl(this).WorkingArea;
            Theme.UpdateDpi(Native.ResolveDpiScale(Handle, DeviceDpi), new Size(work.Width, work.Height));
            Settings.EnsureDefaults();
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);

            Startup.SyncWithSetting(Settings.RunAtStartup);

            AppLog.Info("LTFC 界面就绪：屏幕 " + work.Width + "x" + work.Height
                        + " · DeviceDpi " + DeviceDpi
                        + " · 解析 DPI 缩放 " + Native.ResolveDpiScale(Handle, DeviceDpi).ToString("0.00")
                        + " · 缩放 " + Theme.DpiScale.ToString("0.00")
                        + " · 紧凑系数 " + Theme.Density.ToString("0.00")
                        + " · 字号 " + Theme.FontSize.ToString("0.#") + "pt"
                        + " · 客户区 " + ClientSize.Width + "x" + ClientSize.Height
                        + " · 窗口 " + Width + "x" + Height
                        + " · 侧边栏 " + Theme.NavWidth
                        + " · 模式 " + (Theme.HighContrast ? "高对比度" : (Theme.Dark ? "深色" : "浅色")));

            ApplyInitialSize();
            SwitchPage(ResolveStartPage());

            // 先让窗口带着动效出现，再把“自动启动子程序 / 最小化到托盘”这类耗时工作放到动效之后，
            // 这样点开图标马上就能看到界面，不会因为等子程序启动而白屏。
            Motion.PlayOpen(this, OnOpenAnimationCompleted);
        }

        /// <summary>窗口打开动效结束（或跳过后）执行一次的启动收尾工作。</summary>
        private void OnOpenAnimationCompleted()
        {
            if (IsDisposed || Disposing) return;

            if (Settings.AutoStartModules && !AppOptions.NoModules)
            {
                IList<string> failures = Modules.StartAllEnabled();
                foreach (string failure in failures) AppLog.Warn("自动启动子程序失败：" + failure);
                if (failures.Count > 0)
                {
                    var list = new List<string>(failures);
                    Ask.Warn(this, "启动子程序", "以下子程序未能启动：",
                        string.Join(Environment.NewLine + Environment.NewLine, list.ToArray()));
                }
            }

            RefreshModuleStates(true);

            if (AppOptions.StartInTray || Settings.StartMinimized) HideToTray();
        }

        private string ResolveStartPage()
        {
            if (!string.IsNullOrEmpty(AppOptions.StartPage))
            {
                foreach (string key in _pages.Keys)
                {
                    if (string.Equals(key, AppOptions.StartPage, StringComparison.OrdinalIgnoreCase)) return key;
                }
            }

            string last = Settings.Get("LastPage", HomeKey);
            if (!string.IsNullOrEmpty(last) && _pages.ContainsKey(last)) return last;
            return HomeKey;
        }

        private void ApplyInitialSize()
        {
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Theme.S(1180), (int)(work.Width * 0.92));
            int height = Math.Min(Theme.S(780), (int)(work.Height * 0.92));
            width = Math.Max(MinimumSize.Width, width);
            height = Math.Max(MinimumSize.Height, height);

            ClientSize = new Size(width, height);
            Location = new Point(work.Left + Math.Max(0, (work.Width - Width) / 2),
                work.Top + Math.Max(0, (work.Height - Height) / 2));
            LayoutChrome();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChrome();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Theme.UpdateDpi(Native.ResolveDpiScale(Handle, DeviceDpi), new Size(work.Width, work.Height));
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);
            LayoutChrome();
            foreach (PageBase page in _pages.Values) page.Relayout();
        }

        /// <summary>排布侧边栏与页面（不依赖 Dock，避免高 DPI 下的二次缩放）。</summary>
        private void LayoutChrome()
        {
            if (_nav == null) return;

            int navWidth = Theme.NavWidth;
            _nav.SetBounds(0, 0, navWidth, Math.Max(Theme.S(80), ClientSize.Height));
            _nav.LayoutEntries();

            int contentLeft = navWidth + 1;
            int contentWidth = Math.Max(Theme.S(120), ClientSize.Width - contentLeft);
            foreach (KeyValuePair<string, PageBase> pair in _pages)
            {
                pair.Value.SetBounds(contentLeft, 0, contentWidth, ClientSize.Height);
            }
        }

        private void OnThemeChanged()
        {
            try
            {
                BackColor = Theme.Window;
                _nav.BackColor = Theme.NavBackground;
                Theme.InvalidateTree(this);
                LayoutChrome();
                foreach (PageBase page in _pages.Values) page.Relayout();
                RefreshModuleStates(true);
                Invalidate(true);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("应用主题失败", ex);
            }
        }

        private void OnThemeChangedByPage()
        {
            OnThemeChanged();
        }

        // ---------- 页面切换 ----------
        private void SwitchPage(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            PageBase target;
            if (!_pages.TryGetValue(key, out target)) return;

            foreach (KeyValuePair<string, PageBase> pair in _pages)
            {
                pair.Value.Visible = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
            }

            _currentKey = key;
            Settings.Set("LastPage", key);
            _nav.SelectByKey(key, false);

            target.Relayout();
            // 切页时只做轻量刷新（节流），保证点侧边栏立刻切过去
            RefreshModuleStates(false);
            ActivateForPage(key);
            AppLog.Info("切换到页面：" + key);
        }

        private void OnShellRefresh()
        {
            RefreshModuleStates(true);
            PageBase current;
            if (_pages.TryGetValue(_currentKey, out current)) current.Relayout();
        }

        /// <summary>把当前页面滚动到最底部（自测快照用）。</summary>
        internal void ScrollCurrentToBottom()
        {
            PageBase current;
            if (_pages.TryGetValue(_currentKey, out current)) current.ScrollToBottom();
        }

        /// <summary>把 page.txt 里的分页后缀映射到页面类型。</summary>
        private static ModulePortalSection SectionForKey(string pageKey)
        {
            if (string.Equals(pageKey, Modules.PageMessages, StringComparison.OrdinalIgnoreCase)) return ModulePortalSection.Messages;
            if (string.Equals(pageKey, Modules.PageExpired, StringComparison.OrdinalIgnoreCase)) return ModulePortalSection.Expired;
            return ModulePortalSection.Settings;
        }

        /// <summary>定时提醒到点：弹系统通知（不把任何窗口强制置前）。</summary>
        private void OnAutoNoticeTriggered(AutoNoticeEntry entry)
        {
            try
            {
                _notifier.Notify("AutoNotice · " + entry.TimeText, entry.Text);

                PageBase current;
                if (_pages.TryGetValue(_currentKey, out current)) current.RefreshState();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("显示定时提醒失败", ex);
            }
        }

        /// <summary>
        /// 进入某个模块的分页时，保证它的窗口已经存在：
        /// 没有运行就打开一个，已经在运行则只显示已有窗口（不会开出第二个）。
        /// </summary>
        private void ActivateForPage(string key)
        {
            if (AppOptions.NoModules) return;

            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                foreach (ModulePageDefinition page in definition.Pages)
                {
                    if (!page.ActivatesModule) continue;
                    if (!string.Equals(page.FullKey(definition.Id), key, StringComparison.OrdinalIgnoreCase)) continue;

                    ActivateModule(definition);
                    return;
                }
            }
        }

        /// <summary>
        /// 打开（或显示）某个模块。
        /// 外部子程序要枚举进程、等待窗口、启动进程，这些都很慢，所以放到后台线程做，
        /// 界面线程只负责回来刷新状态——这就是“点侧边栏不再卡住”的关键。
        /// </summary>
        private void ActivateModule(ModuleDefinition definition)
        {
            ModuleProcess process = Modules.Process(definition.Id);

            if (definition.Kind != ModuleKind.ExternalProcess || process == null)
            {
                // 内置模块（AutoNotice 窗口）必须在界面线程里创建
                if (process != null && process.IsRunning) return;

                string message = ModuleLauncher.LaunchOrShow(definition);
                if (!string.IsNullOrEmpty(message))
                    AppLog.Warn("进入分页时打开 " + definition.DisplayName + " 失败：" + message);
                return;
            }

            string id = definition.Id;
            lock (_launchGate)
            {
                if (!_launching.Add(id)) return;
            }

            Task.Run(delegate
            {
                string error = null;
                try
                {
                    if (!process.IsRunning && !process.HasExternalInstance())
                    {
                        error = ModuleLauncher.LaunchOrShow(definition);
                    }
                    else
                    {
                        string activateMessage;
                        if (!process.ActivateWindow(out activateMessage))
                        {
                            // 已经启动过但这一瞬间拿不到窗口：不算失败，静默忽略
                            error = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + "：" + ex.Message;
                }
                finally
                {
                    lock (_launchGate) _launching.Remove(id);
                }

                if (!string.IsNullOrEmpty(error))
                {
                    string text = error;
                    string name = definition.DisplayName;
                    PostToUi(delegate { AppLog.Warn("进入分页时打开 " + name + " 失败：" + text); });
                }
                else
                {
                    PostToUi(delegate { RefreshModuleStates(true); });
                }
            });
        }

        /// <summary>把后台线程的结果投递回界面线程（窗口已释放时直接忽略）。</summary>
        private void PostToUi(Action action)
        {
            if (action == null) return;

            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    action();
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>定时刷新子程序状态（崩溃 / 被外部退出都能反映到界面）。</summary>
        private void RefreshModuleStates()
        {
            RefreshModuleStates(false);
        }

        /// <summary>
        /// 刷新子程序状态。
        /// <paramref name="force"/> 为 false 时做节流：枚举进程在低配机上要几十毫秒，
        /// 每次切页都扫一遍就会“点一下顿一下”，短时间内的重复请求直接跳过。
        /// </summary>
        private void RefreshModuleStates(bool force)
        {
            try
            {
                int now = Environment.TickCount;
                if (!force && now - _lastStateRefreshTick < StateRefreshIntervalMs) return;
                _lastStateRefreshTick = now;

                Modules.RefreshAll();

                PageBase current;
                if (_pages.TryGetValue(_currentKey, out current)) current.RefreshState();

                int running = 0;
                foreach (ModuleProcess process in Modules.ExternalProcesses)
                {
                    if (process.IsRunning) running++;
                }

                _notifier.SetTooltip(Theme.ShortName + " V" + Theme.VersionText + " · 子程序运行中：" + running);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("刷新模块状态失败", ex);
            }
        }

        // ---------- 托盘与退出 ----------
        private void HideToTray()
        {
            _hiddenToTray = true;
            Hide();
            ShowInTaskbar = false;
            AppLog.Info("主窗口已最小化到托盘。");
        }

        private void RestoreWindow()
        {
            try
            {
                if (!_hiddenToTray && Visible && WindowState != FormWindowState.Minimized) return;

                ShowInTaskbar = true;
                if (!Visible) Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                _hiddenToTray = false;
                Activate();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("恢复窗口失败", ex);
            }
        }

        private void ExitFromTray()
        {
            _exiting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool hideToTray = !_exiting && Settings.CloseToTray
                              && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing);

            AppLog.Info("窗口关闭请求：reason=" + e.CloseReason + " closeToTray=" + Settings.CloseToTray
                        + " exiting=" + _exiting + " → " + (hideToTray ? "驻留托盘" : "退出程序"));

            if (hideToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _statusTimer.Stop();
            Theme.Changed -= OnThemeChanged;

            try
            {
                _notifier.Dispose();
            }
            catch (Exception)
            {
            }

            // 内置定时提醒由主程序提供，退出时一并释放
            try
            {
                ServiceHolder.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("释放 AutoNotice 失败", ex);
            }

            // 退出时一并关闭由本程序启动的子程序（它们各自是独立进程）
            try
            {
                Modules.StopAll();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("退出时停止子程序失败", ex);
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _statusTimer.Dispose(); } catch (Exception) { }
                try { _notifier.Dispose(); } catch (Exception) { }
            }

            base.Dispose(disposing);
        }
    }
}
