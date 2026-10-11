using System.Drawing;
using System.Windows.Forms;

namespace OpenChoose
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label lblHeader;
        private Label lblSeparator;
        private Label lblFile;
        private ListBox lstPrograms;
        private LinkLabel lnkWindowsDialog;
        private LinkLabel lnkSetDefault;
        private Label lblStatus;
        private Button btnOpen;
        private Button btnCancel;
        private ImageList imageList;
        private ToolTip toolTip;

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.components != null)
            {
                this.components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.lblHeader = new Label();
            this.lblSeparator = new Label();
            this.lblFile = new Label();
            this.lstPrograms = new ListBox();
            this.lnkWindowsDialog = new LinkLabel();
            this.lnkSetDefault = new LinkLabel();
            this.lblStatus = new Label();
            this.btnOpen = new Button();
            this.btnCancel = new Button();
            this.imageList = new ImageList(this.components);
            this.toolTip = new ToolTip(this.components);

            this.SuspendLayout();

            // 标题
            this.lblHeader.Font = new Font(this.Font.FontFamily, 12F, FontStyle.Bold);
            this.lblHeader.ForeColor = Color.FromArgb(31, 78, 121);
            this.lblHeader.Location = new Point(12, 12);
            this.lblHeader.Name = "lblHeader";
            this.lblHeader.Size = new Size(396, 32);
            this.lblHeader.TabIndex = 0;
            this.lblHeader.Text = "选择打开 PPT 的程序";
            this.lblHeader.TextAlign = ContentAlignment.MiddleCenter;

            // 分隔线
            this.lblSeparator.BorderStyle = BorderStyle.Fixed3D;
            this.lblSeparator.Location = new Point(12, 50);
            this.lblSeparator.Name = "lblSeparator";
            this.lblSeparator.Size = new Size(396, 2);
            this.lblSeparator.TabIndex = 1;

            // 文件信息
            this.lblFile.AutoEllipsis = true;
            this.lblFile.ForeColor = Color.DimGray;
            this.lblFile.Location = new Point(12, 58);
            this.lblFile.Name = "lblFile";
            this.lblFile.Size = new Size(396, 20);
            this.lblFile.TabIndex = 2;
            this.lblFile.Text = "文件：";

            // 程序列表
            this.lstPrograms.DrawMode = DrawMode.OwnerDrawFixed;
            this.lstPrograms.Font = new Font(this.Font.FontFamily, 10F, FontStyle.Regular);
            this.lstPrograms.IntegralHeight = false;
            this.lstPrograms.ItemHeight = 30;
            this.lstPrograms.Location = new Point(12, 82);
            this.lstPrograms.Name = "lstPrograms";
            this.lstPrograms.Size = new Size(396, 132);
            this.lstPrograms.TabIndex = 3;

            // 说明：这里原来是一栏“记住我的选择，以后直接用该程序打开”的复选框，
            // 按要求已隐藏：不再创建该控件，其下方的链接 / 状态 / 按钮整体上移一行。

            // 系统“打开方式”对话框
            this.lnkWindowsDialog.AutoSize = true;
            this.lnkWindowsDialog.Location = new Point(12, 222);
            this.lnkWindowsDialog.Name = "lnkWindowsDialog";
            this.lnkWindowsDialog.Size = new Size(160, 18);
            this.lnkWindowsDialog.TabIndex = 4;
            this.lnkWindowsDialog.TabStop = true;
            this.lnkWindowsDialog.Text = "使用 Windows“打开方式”对话框";

            // 设为默认打开方式
            this.lnkSetDefault.AutoSize = true;
            this.lnkSetDefault.Location = new Point(206, 222);
            this.lnkSetDefault.Name = "lnkSetDefault";
            this.lnkSetDefault.Size = new Size(140, 18);
            this.lnkSetDefault.TabIndex = 5;
            this.lnkSetDefault.TabStop = true;
            this.lnkSetDefault.Text = "设为默认打开方式";

            // 状态栏文字
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.ForeColor = Color.DimGray;
            this.lblStatus.Location = new Point(12, 244);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(396, 20);
            this.lblStatus.TabIndex = 6;
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            // 按钮
            this.btnOpen.DialogResult = DialogResult.OK;
            this.btnOpen.Location = new Point(226, 272);
            this.btnOpen.Name = "btnOpen";
            this.btnOpen.Size = new Size(88, 28);
            this.btnOpen.TabIndex = 7;
            this.btnOpen.Text = "打开";

            this.btnCancel.DialogResult = DialogResult.Cancel;
            this.btnCancel.Location = new Point(320, 272);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new Size(88, 28);
            this.btnCancel.TabIndex = 8;
            this.btnCancel.Text = "取消";

            // 图标列表
            this.imageList.ColorDepth = ColorDepth.Depth32Bit;
            this.imageList.ImageSize = new Size(20, 20);
            this.imageList.TransparentColor = Color.Transparent;

            // 窗体本身
            this.AcceptButton = this.btnOpen;
            this.CancelButton = this.btnCancel;
            // 布局由 WinForms 按 DPI 缩放（设计基准 96 DPI = 100%）：
            // AutoScaleMode.Dpi 会正确换算窗口与控件的物理尺寸，
            // 而 ListBox.ItemHeight / ImageList.ImageSize 等“非尺寸属性”
            // 由 MainForm.OnLoad 用实测到的缩放系数补齐（见 ApplyScaledMetrics）。
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.ClientSize = new Size(420, 312);
            this.Controls.Add(this.lblHeader);
            this.Controls.Add(this.lblSeparator);
            this.Controls.Add(this.lblFile);
            this.Controls.Add(this.lstPrograms);
            this.Controls.Add(this.lnkWindowsDialog);
            this.Controls.Add(this.lnkSetDefault);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnOpen);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MainForm";
            this.ShowInTaskbar = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "选择打开 PPT 的程序";

            this.ResumeLayout(false);

        }
    }
}
