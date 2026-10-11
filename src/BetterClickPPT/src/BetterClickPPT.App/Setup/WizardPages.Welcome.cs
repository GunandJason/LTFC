using BetterClickPPT.Core;
using BetterClickPPT.Core.Deployment;

namespace BetterClickPPT.App.Setup;

/// <summary>步骤 1：欢迎页。</summary>
internal sealed class WelcomePage : WizardPage
{
    public override string Title => "欢迎使用";

    public override string Subtitle => "让触摸点击直接翻到下一页";

    public override string NextText => "开始安装";

    public WelcomePage()
    {
        Controls.Add(CreateLabel("本程序解决一个很常见的问题：", 16, 6, 580, 24));

        Controls.Add(CreateLabel(
            "在触摸屏 / 触摸一体机上放映 PPT 时，“点一下屏幕”往往没有反应，\r\n"
            + "必须用手指滑动才能切换下一页。安装本程序后，轻触屏幕即可直接翻到下一页。",
            16, 34, 580, 48));

        var features = new ListBox
        {
            Location = new Point(16, 92),
            Size = new Size(580, 134),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 250, 252),
            SelectionMode = SelectionMode.None,
            IntegralHeight = false
        };
        features.Items.Add("兼容 Microsoft PowerPoint 与 WPS 演示（可只装其中一个）");
        features.Items.Add("安装时自动查找 PPT / WPS 的安装位置，也支持手动指定");
        features.Items.Add("只有“单指 + 快速 + 位移很小”的轻触才翻页：滑动翻页、双指缩放、长按不受影响");
        features.Items.Add("只增强“全屏 + 无边框”的放映窗口；编辑 PPT 时的点击完全不受影响");
        features.Items.Add("安装到当前用户目录（%LOCALAPPDATA%），全程不需要管理员权限");
        features.Items.Add("安装后自动开机启动，常驻托盘；可随时一键暂停或卸载");
        Controls.Add(features);

        Controls.Add(CreateHint(
            "点击“开始安装”后，向导会先检测本机的 PowerPoint / WPS 安装位置，再由您选择要为哪个软件启用。",
            16, 236, 580, 44));

        Controls.Add(CreateHint(
            "如遇问题：托盘图标右键 →「检测当前前台窗口」可查看识别结果；设置窗口的“诊断”标签页可打开日志。",
            16, 284, 580, 44));
    }
}
