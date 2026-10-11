using System;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// AutoNotice 的窗口：LTFC 为“没有独立界面 / 缺少设置”的模块提供的界面。
    /// 与主程序使用同一套主题；单实例：重复点击只把已有窗口显示到前台。
    /// </summary>
    internal sealed class AutoNoticeWindow : Form
    {
        private static AutoNoticeWindow _current;

        private readonly ModulePortalPage _page;

        private AutoNoticeWindow(AutoNoticeService service)
        {
            ModuleDefinition definition = Modules.Find(Modules.AutoNoticeId);
            Theme.Apply(Settings.FontSize, Settings.DarkMode, Settings.AccentKey, Settings.HighContrast);

            Text = "AutoNotice · 定时提醒（" + Theme.ShortName + "）";
            Icon = IconFactory.AppIcon;
            BackColor = Theme.Window;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(Theme.S(760), Theme.S(560));
            ClientSize = new Size(Theme.S(900), Theme.S(660));

            _page = new ModulePortalPage(definition, ModulePortalSection.Settings, null);
            _page.Dock = DockStyle.Fill;
            Controls.Add(_page);
            _page.Relayout();
            _page.RefreshState();
        }

        /// <summary>显示（或重新显示）AutoNotice 窗口。返回空字符串表示成功。</summary>
        public static string ShowFor(IWin32Window owner, AutoNoticeService service)
        {
            try
            {
                if (service == null)
                {
                    Settings.AutoNoticeEnabled = true;
                    ServiceHolder.Ensure();
                    service = ServiceHolder.Current;
                }

                if (_current != null && !_current.IsDisposed)
                {
                    _current.Show();
                    if (_current.WindowState == FormWindowState.Minimized) _current.WindowState = FormWindowState.Normal;
                    _current.Activate();
                    _current.BringToFront();
                    _current._page.RefreshState();
                    return string.Empty;
                }

                if (!Settings.AutoNoticeEnabled)
                {
                    Settings.AutoNoticeEnabled = true;
                    if (service != null) service.Start();
                }

                _current = new AutoNoticeWindow(service);
                _current.FormClosed += (s, e) => { _current = null; };
                _current.Show(owner);
                _current.Activate();
                return string.Empty;
            }
            catch (Exception ex)
            {
                AppLog.Exception_("打开 AutoNotice 窗口失败", ex);
                return "无法打开 AutoNotice 窗口：" + ex.Message;
            }
        }
    }

    /// <summary>AutoNotice 服务的持有者（主窗口与窗口共用同一个实例）。</summary>
    internal static class ServiceHolder
    {
        private static AutoNoticeService _service;

        public static AutoNoticeService Current
        {
            get
            {
                if (_service == null) _service = new AutoNoticeService();
                return _service;
            }
        }

        public static void Ensure()
        {
            if (_service == null) _service = new AutoNoticeService();
            ModuleLauncher.AutoNotice = _service;
            _service.Start();
        }

        public static void Dispose()
        {
            if (_service == null) return;
            try { _service.Dispose(); } catch (Exception) { }
            _service = null;
        }
    }
}
