using System;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// 托盘图标与系统通知。
    /// 说明：通知只弹气泡，不会把任何窗口置前；只有用户点击通知/托盘菜单时才回调主程序。
    /// </summary>
    internal sealed class Notifier : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Control _uiOwner;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _menuShow;
        private readonly ToolStripMenuItem _menuExit;

        public Notifier(Control uiOwner, string tooltip)
        {
            _uiOwner = uiOwner;
            _menu = new ContextMenuStrip();
            _menuShow = new ToolStripMenuItem("打开 LTFC 主界面");
            _menuExit = new ToolStripMenuItem("退出 LTFC");
            _menu.Items.Add(_menuShow);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_menuExit);

            _icon = new NotifyIcon
            {
                Icon = IconFactory.AppIcon,
                Text = Trim(tooltip, 63),
                Visible = true,
                ContextMenuStrip = _menu
            };

            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Raise(ShowRequested); };
            _icon.BalloonTipClicked += (s, e) => Raise(NotificationClicked);
            _menuShow.Click += (s, e) => Raise(ShowRequested);
            _menuExit.Click += (s, e) => Raise(ExitRequested);
        }

        /// <summary>用户希望显示主界面（点击通知或托盘菜单）。</summary>
        public event Action ShowRequested;

        /// <summary>用户点击了通知。</summary>
        public event Action NotificationClicked;

        /// <summary>用户选择退出程序。</summary>
        public event Action ExitRequested;

        /// <summary>最近一次通知的标题与正文（供诊断显示）。</summary>
        public string LastTitle { get; private set; }

        public string LastText { get; private set; }

        public NotifyIcon Icon { get { return _icon; } }

        /// <summary>弹出系统通知（气泡）。不激活任何窗口。</summary>
        public void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 10000)
        {
            try
            {
                LastTitle = title;
                LastText = text;
                _icon.BalloonTipTitle = Trim(title, 63);
                _icon.BalloonTipText = Trim(text, 255);
                _icon.BalloonTipIcon = icon;
                _icon.ShowBalloonTip(Math.Max(1000, timeoutMs));
                AppLog.Info("通知：" + title + " / " + text);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("弹出通知失败", ex);
            }
        }

        /// <summary>更新托盘悬停文字。</summary>
        public void SetTooltip(string text)
        {
            try
            {
                _icon.Text = Trim(text, 63);
            }
            catch (Exception)
            {
            }
        }

        private void Raise(Action handler)
        {
            if (handler == null) return;
            try
            {
                if (_uiOwner != null && _uiOwner.IsHandleCreated && _uiOwner.InvokeRequired)
                {
                    _uiOwner.BeginInvoke(handler);
                }
                else
                {
                    handler();
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("托盘回调失败", ex);
            }
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max - 1) + "…";
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch (Exception)
            {
            }

            try { _menu.Dispose(); } catch (Exception) { }
        }
    }
}
