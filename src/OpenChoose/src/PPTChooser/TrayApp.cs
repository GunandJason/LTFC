using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace OpenChoose
{
    /// <summary>
    /// 托盘程序：开机自启动（--startup）时在后台运行，提供常用入口。
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _associationItem;

        public TrayApp()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripMenuItem("打开 PPT 文件...", null, new EventHandler(this.OnOpenFile)));
            menu.Items.Add(new ToolStripMenuItem("选择打开方式...", null, new EventHandler(this.OnChooseForFile)));
            menu.Items.Add(new ToolStripSeparator());

            this._associationItem = new ToolStripMenuItem("修复 / 注册文件关联", null, new EventHandler(this.OnRepairAssociation));
            menu.Items.Add(this._associationItem);
            menu.Items.Add(new ToolStripMenuItem("设为默认打开方式...", null, new EventHandler(this.OnSetDefault)));
            menu.Items.Add(new ToolStripMenuItem("清除已记住的选择", null, new EventHandler(this.OnClearRemembered)));

            this._startupItem = new ToolStripMenuItem("开机自动启动");
            this._startupItem.CheckOnClick = true;
            this._startupItem.Checked = StartupManager.IsEnabled;
            this._startupItem.Click += new EventHandler(this.OnToggleStartup);
            menu.Items.Add(this._startupItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出", null, new EventHandler(this.OnExit)));

            this._notifyIcon = new NotifyIcon();
            this._notifyIcon.Icon = Ui.LoadAppIcon();
            this._notifyIcon.Text = "OpenChoose - PPT 打开方式选择器";
            this._notifyIcon.ContextMenuStrip = menu;
            this._notifyIcon.Visible = true;
            this._notifyIcon.DoubleClick += new EventHandler(this.OnOpenFile);

            this.RefreshMenu();
            Log.Write("托盘程序已启动");
        }

        /// <summary>选择文件后弹出选择窗口。</summary>
        private void OnOpenFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要打开的 PPT 文件";
                dialog.Filter = "PowerPoint 演示文稿 (*.ppt;*.pptx;*.pps;*.ppsx;*.pot;*.potx)|*.ppt;*.pptx;*.pps;*.ppsx;*.pot;*.potx|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                ShowChooser(dialog.FileName);
            }
        }

        /// <summary>为最近使用的文件选择打开方式（这里先让用户挑文件）。</summary>
        private void OnChooseForFile(object sender, EventArgs e)
        {
            this.OnOpenFile(sender, e);
        }

        /// <summary>打开选择窗口。</summary>
        private static void ShowChooser(string filePath)
        {
            using (MainForm form = new MainForm(filePath))
            {
                form.ShowDialog();
            }
        }

        /// <summary>重新注册文件关联。</summary>
        private void OnRepairAssociation(object sender, EventArgs e)
        {
            try
            {
                FileAssociation.Register();
                MessageBox.Show("文件关联已修复。\r\n现在可以在 PPT 文件的“打开方式”中看到 OpenChoose。",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Write("修复文件关联失败", ex);
                MessageBox.Show("修复文件关联失败：\r\n" + ex.Message,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.RefreshMenu();
        }

        /// <summary>设为默认打开方式。</summary>
        private void OnSetDefault(object sender, EventArgs e)
        {
            DefaultProgramHelper.Apply(null, true);
            this.RefreshMenu();
        }

        /// <summary>清除已记住的选择。</summary>
        private void OnClearRemembered(object sender, EventArgs e)
        {
            AppConfig config = AppConfig.Load();
            config.ClearSavedPrograms();
            config.RememberChoice = false;
            config.Save();
            MessageBox.Show("已清除记住的选择。\r\n下次打开 PPT 时会重新弹出选择窗口。",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>切换开机自启动。</summary>
        private void OnToggleStartup(object sender, EventArgs e)
        {
            try
            {
                StartupManager.Set(this._startupItem.Checked);
            }
            catch (Exception ex)
            {
                Log.Write("设置开机自启动失败", ex);
                MessageBox.Show("设置开机自启动失败：\r\n" + ex.Message,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.RefreshMenu();
        }

        /// <summary>退出托盘程序。</summary>
        private void OnExit(object sender, EventArgs e)
        {
            this._notifyIcon.Visible = false;
            this._notifyIcon.Dispose();
            Log.Write("托盘程序已退出");
            this.ExitThread();
        }

        /// <summary>同步菜单勾选状态。</summary>
        private void RefreshMenu()
        {
            this._startupItem.Checked = StartupManager.IsEnabled;
            this._associationItem.Text = FileAssociation.IsRegistered()
                ? "修复 / 注册文件关联"
                : "注册文件关联（未注册）";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this._notifyIcon != null)
            {
                this._notifyIcon.Visible = false;
                this._notifyIcon.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
