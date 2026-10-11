using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OpenChoose
{
    /// <summary>
    /// 主窗口：“选择打开 PPT 的程序”。
    /// </summary>
    internal partial class MainForm : Form
    {
        // 设计基准（96 DPI / 100% 缩放）下的度量，实际值 = 设计值 × 实测缩放系数
        private const int DesignItemHeight = 30;
        private const int DesignIconSize = 20;
        private const int DesignIconLeft = 6;
        private const int DesignTextLeft = 34;
        private const int DesignTextRightPadding = 6;
        private const int DesignLinkGap = 12;
        private const int DesignListHeight = 132;

        private readonly string _filePath;
        private readonly AppConfig _config;
        private readonly List<ProgramCandidate> _items = new List<ProgramCandidate>();
        private readonly Dictionary<string, int> _imageIndexes =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private float _scale = 1F;
        private int _itemHeight = DesignItemHeight;
        private int _iconSize = DesignIconSize;
        private int _iconLeft = DesignIconLeft;
        private int _iconTop = 5;
        private int _textLeft = DesignTextLeft;
        private int _textRightPadding = DesignTextRightPadding;
        private int _linkGap = DesignLinkGap;
        private bool _metricsApplied;

        public MainForm(string filePath)
            : this(filePath, AppConfig.Load())
        {
        }

        public MainForm(string filePath, AppConfig config)
        {
            this._filePath = filePath;
            this._config = config ?? AppConfig.Load();

            this.InitializeComponent();

            // 高分屏：WinForms 会按 DPI 缩放布局；--scale 只是在 DPI 基础上再乘一个倍率
            float factor = Ui.GetOverrideFactor();
            if (Math.Abs(factor - 1F) > 0.001F)
            {
                this.AutoScaleDimensions = new SizeF(96F / factor, 96F / factor);
            }

            this.Font = Ui.CreateUiFont();
            this.Icon = Ui.LoadAppIcon();
            this.lblHeader.Font = new Font(this.Font.FontFamily, 12F, FontStyle.Bold);
            this.lstPrograms.Font = new Font(this.Font.FontFamily, 10F, FontStyle.Regular);
            this.lblFile.Text = "文件：" + Path.GetFileName(this._filePath);
            this.toolTip.SetToolTip(this.lblFile, this._filePath);

            this.btnOpen.Click += new EventHandler(this.OnOpenClick);
            this.btnCancel.Click += new EventHandler(this.OnCancelClick);
            this.lstPrograms.DrawItem += new DrawItemEventHandler(this.OnDrawItem);
            this.lstPrograms.DoubleClick += new EventHandler(this.OnListDoubleClick);
            this.lstPrograms.MouseMove += new MouseEventHandler(this.OnListMouseMove);
            this.lnkWindowsDialog.LinkClicked += new LinkLabelLinkClickedEventHandler(this.OnWindowsDialogClick);
            this.lnkSetDefault.LinkClicked += new LinkLabelLinkClickedEventHandler(this.OnSetDefaultClick);
            this.lnkWindowsDialog.SizeChanged += new EventHandler(this.OnWindowsDialogLayoutChanged);
        }

        /// <summary>被选择的文件。</summary>
        public string FilePath
        {
            get { return this._filePath; }
        }

        /// <summary>
        /// 高分屏适配：
        /// WinForms 已按 DPI 缩放了窗口与控件（AutoScaleMode.Dpi），
        /// 但 ListBox.ItemHeight、ImageList.ImageSize 以及自绘时的图标/文字偏移
        /// 属于不会被自动缩放的属性，这里用实测到的缩放系数补齐，
        /// 否则行高与字体会不一致（文字被裁切或与图标错位）。
        /// 字体以“点”为单位，本身会随 DPI 放大，无需额外处理。
        /// </summary>
        private void ApplyScaledMetrics()
        {
            float factor = this.lstPrograms.Height / (float)DesignListHeight;
            if (factor <= 0F)
            {
                factor = 1F;
            }

            this._scale = factor;
            this._itemHeight = Ui.Scale(DesignItemHeight, factor);
            this._iconSize = Ui.Scale(DesignIconSize, factor);
            this._iconLeft = Ui.Scale(DesignIconLeft, factor);
            this._iconTop = Math.Max(1, (this._itemHeight - this._iconSize) / 2);
            this._textLeft = Ui.Scale(DesignTextLeft, factor);
            this._textRightPadding = Ui.Scale(DesignTextRightPadding, factor);
            this._linkGap = Ui.Scale(DesignLinkGap, factor);

            if (this.lstPrograms.ItemHeight != this._itemHeight)
            {
                this.lstPrograms.ItemHeight = this._itemHeight;
            }

            // ImageSize 必须在加入图片之前设置，因此只设一次
            if (!this._metricsApplied)
            {
                this.imageList.ImageSize = new Size(this._iconSize, this._iconSize);
                this._metricsApplied = true;
                Log.Write("界面缩放: " + Math.Round(factor * 100) + "%（系统 DPI " + Ui.GetSystemDpi() + "，倍率 " +
                    Ui.GetOverrideFactor().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "）" +
                    "，行高 " + this._itemHeight + "px，图标 " + this._iconSize + "px");
            }

            this.PositionLinks();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.ApplyOverrideScale();
            this.LoadPrograms();
            this.ApplyScaledMetrics();
        }

        /// <summary>
        /// 命令行 --scale 指定了额外倍率时：在 WinForms 按 DPI 完成自动缩放之后，
        /// 再用 Control.Scale 把整个窗口（含控件与字体）放大同样的倍数，
        /// 保证文字与行高始终成比例（Control.Scale 会一并缩放字体，不能重复放大）。
        /// </summary>
        private void ApplyOverrideScale()
        {
            float factor = Ui.GetOverrideFactor();
            if (Math.Abs(factor - 1F) <= 0.001F)
            {
                return;
            }

            this.Scale(new SizeF(factor, factor));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 自动缩放完成后再次校正（幂等）
            this.ApplyScaledMetrics();
        }

        /// <summary>两个链接按钮按实际文字宽度依次排列，避免字体/DPI 变化时重叠。</summary>
        private void PositionLinks()
        {
            this.lnkSetDefault.Location = new Point(
                this.lnkWindowsDialog.Right + this._linkGap, this.lnkWindowsDialog.Top);
        }

        private void OnWindowsDialogLayoutChanged(object sender, EventArgs e)
        {
            this.PositionLinks();
        }

        /// <summary>填充程序列表（PowerPoint / WPS / LibreOffice / 其他）。</summary>
        private void LoadPrograms()
        {
            this._items.Clear();
            this.lstPrograms.Items.Clear();
            this._items.AddRange(ProgramFinder.DetectAll());

            foreach (ProgramCandidate item in this._items)
            {
                this.lstPrograms.Items.Add(item);
            }

            // 预选上次记住的程序
            string extension = Path.GetExtension(this._filePath);
            string saved = this._config.GetSavedProgram(extension);
            int selected = 0;
            if (!string.IsNullOrEmpty(saved))
            {
                for (int i = 0; i < this._items.Count; i++)
                {
                    if (string.Equals(this._items[i].ExecutablePath, saved, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = i;
                        break;
                    }
                }
            }

            if (this.lstPrograms.Items.Count > 0)
            {
                this.lstPrograms.SelectedIndex = selected;
            }

            this.UpdateStatus();
        }

        /// <summary>自绘列表：图标 + 文字。</summary>
        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            if (e.Index < 0 || e.Index >= this.lstPrograms.Items.Count)
            {
                return;
            }

            ProgramCandidate item = this.lstPrograms.Items[e.Index] as ProgramCandidate;
            if (item == null)
            {
                return;
            }

            int imageIndex = this.GetImageIndex(item);
            if (imageIndex >= 0)
            {
                Image image = this.imageList.Images[imageIndex];
                if (image != null)
                {
                    Rectangle iconBounds = new Rectangle(
                        e.Bounds.Left + this._iconLeft,
                        e.Bounds.Top + this._iconTop,
                        this._iconSize,
                        this._iconSize);

                    // 高 DPI 下图标会被放大绘制，用高质量插值避免锯齿
                    System.Drawing.Drawing2D.InterpolationMode oldMode = e.Graphics.InterpolationMode;
                    try
                    {
                        e.Graphics.InterpolationMode =
                            System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        e.Graphics.DrawImage(image, iconBounds);
                    }
                    finally
                    {
                        e.Graphics.InterpolationMode = oldMode;
                    }
                }
            }

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color textColor;
            if (isSelected)
            {
                textColor = SystemColors.HighlightText;
            }
            else if (item.IsAvailable || item.IsOther)
            {
                textColor = SystemColors.ControlText;
            }
            else
            {
                textColor = SystemColors.GrayText;
            }

            Rectangle textBounds = new Rectangle(
                e.Bounds.Left + this._textLeft,
                e.Bounds.Top,
                Math.Max(10, e.Bounds.Width - this._textLeft - this._textRightPadding),
                e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, item.DisplayText, e.Font, textBounds, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            e.DrawFocusRectangle();
        }

        /// <summary>取得（并缓存）列表项图标。</summary>
        private int GetImageIndex(ProgramCandidate item)
        {
            string key;
            Icon icon;
            if (item.IsOther)
            {
                key = "<other>";
                icon = SystemIcons.Question;
            }
            else if (item.IsAvailable)
            {
                key = item.ExecutablePath;
                icon = Ui.LoadIconFor(item.ExecutablePath);
            }
            else
            {
                key = item.Key + ":missing";
                icon = SystemIcons.Application;
            }

            int index;
            if (this._imageIndexes.TryGetValue(key, out index))
            {
                return index;
            }

            try
            {
                this.imageList.Images.Add(icon);
                index = this.imageList.Images.Count - 1;
                this._imageIndexes[key] = index;
                return index;
            }
            catch (Exception ex)
            {
                Log.Write("添加图标失败: " + key, ex);
                return -1;
            }
        }

        /// <summary>鼠标悬停时显示程序的完整路径。</summary>
        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            int index = this.lstPrograms.IndexFromPoint(e.Location);
            if (index < 0 || index >= this.lstPrograms.Items.Count)
            {
                this.toolTip.SetToolTip(this.lstPrograms, string.Empty);
                return;
            }

            ProgramCandidate item = this.lstPrograms.Items[index] as ProgramCandidate;
            this.toolTip.SetToolTip(this.lstPrograms, item == null ? string.Empty : item.ToolTipText);
        }

        /// <summary>“打开”按钮。</summary>
        private void OnOpenClick(object sender, EventArgs e)
        {
            // 校验失败时保持窗口打开
            this.DialogResult = DialogResult.None;
            this.OpenSelected();
        }

        /// <summary>双击列表项直接打开。</summary>
        private void OnListDoubleClick(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.None;
            this.OpenSelected();
        }

        /// <summary>“取消”按钮。</summary>
        private void OnCancelClick(object sender, EventArgs e)
        {
            this.Close();
        }

        /// <summary>使用已选中的程序打开文件。</summary>
        private void OpenSelected()
        {
            ProgramCandidate item = this.lstPrograms.SelectedItem as ProgramCandidate;
            if (item == null)
            {
                return;
            }

            if (item.IsOther || (!item.IsAvailable && string.IsNullOrEmpty(item.ExecutablePath)))
            {
                ProgramCandidate chosen = this.PickOtherProgram();
                if (chosen == null)
                {
                    return;
                }

                item = chosen;
            }
            else if (!item.IsAvailable)
            {
                MessageBox.Show(this,
                    "未在本机检测到 " + item.DisplayName + "。\r\n请选择列表中的其它程序，或使用“选择其他程序...”。",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // “记住我的选择，以后直接用该程序打开”这一栏已在界面上隐藏：
            // 是否记住完全由配置决定（settings.ini 的 RememberChoice=1，默认不记住）。
            if (this._config.RememberChoice)
            {
                this._config.SetSavedProgram(
                    Path.GetExtension(this._filePath), item.ExecutablePath, item.DisplayName);
                this._config.Save();
            }

            try
            {
                Launcher.OpenWith(item.ExecutablePath, this._filePath);
                this.Close();
            }
            catch (Exception ex)
            {
                Log.Write("打开文件失败: " + this._filePath, ex);
                MessageBox.Show(this, "无法启动程序：\r\n" + ex.Message,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>让用户选择其它程序，并把结果加入列表（去重）。</summary>
        private ProgramCandidate PickOtherProgram()
        {
            string executable = this.AskForProgram();
            if (string.IsNullOrEmpty(executable))
            {
                return null;
            }

            foreach (ProgramCandidate existing in this._items)
            {
                if (string.Equals(existing.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase))
                {
                    this.lstPrograms.SelectedItem = existing;
                    return existing;
                }
            }

            string name = Ui.DescribeExecutable(executable);
            if (string.IsNullOrEmpty(name))
            {
                name = Path.GetFileNameWithoutExtension(executable);
            }

            ProgramCandidate custom = new ProgramCandidate("custom", name, executable);

            // 插入到“选择其他程序...”之前，并选中
            int index = 0;
            for (int i = 0; i < this._items.Count; i++)
            {
                if (this._items[i].IsOther)
                {
                    index = i;
                    break;
                }
            }

            this._items.Insert(index, custom);
            this.lstPrograms.Items.Insert(index, custom);
            this.lstPrograms.SelectedIndex = index;
            this.lstPrograms.Refresh();
            this.toolTip.SetToolTip(this.lstPrograms, custom.ToolTipText);
            return custom;
        }

        /// <summary>选择其它程序（浏览 .exe）。</summary>
        private string AskForProgram()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择用于打开 PPT 的程序";
                dialog.Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.RestoreDirectory = true;

                string initial = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                {
                    dialog.InitialDirectory = initial;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    return dialog.FileName;
                }
            }

            return null;
        }

        /// <summary>使用 Windows 自带的“打开方式”对话框。</summary>
        private void OnWindowsDialogClick(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Launcher.OpenWithWindowsDialog(this._filePath);
                this.Close();
            }
            catch (Exception ex)
            {
                Log.Write("打开系统对话框失败", ex);
                MessageBox.Show(this, "无法打开 Windows 的“打开方式”对话框：\r\n" + ex.Message,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>设为默认打开方式。</summary>
        private void OnSetDefaultClick(object sender, LinkLabelLinkClickedEventArgs e)
        {
            DefaultProgramHelper.Apply(this, true);
            this.UpdateStatus();
            this.LoadPrograms();
        }

        /// <summary>更新底部状态文字。</summary>
        private void UpdateStatus()
        {
            string extension = Path.GetExtension(this._filePath);
            string current = Ui.DescribeCurrentDefault(extension);

            if (!FileAssociation.IsRegistered())
            {
                this.lblStatus.Text = "尚未注册文件关联（当前默认：" + current + "）";
            }
            else if (FileAssociation.HasUserChoice(extension))
            {
                this.lblStatus.Text = "当前默认打开方式：" + current + "（由 Windows 设置决定）";
            }
            else
            {
                this.lblStatus.Text = "当前默认打开方式：" + current;
            }
        }



    }
}
