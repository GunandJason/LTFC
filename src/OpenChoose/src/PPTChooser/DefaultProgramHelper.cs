using System;
using System.Windows.Forms;

namespace OpenChoose
{
    /// <summary>“设为默认打开方式”的共用流程（界面 / 托盘 / 命令行共用）。</summary>
    internal static class DefaultProgramHelper
    {
        /// <summary>
        /// “设为默认打开方式”之前的提醒：
        /// 这一步要写入 HKCU 下的文件关联，部分安全软件会把这种“修改关联”的行为
        /// 当成可疑操作并弹窗拦截；先说清楚再做，避免用户看到拦截提示后以为程序坏了。
        /// 静默模式（安装程序 / 脚本）不弹这个提醒。
        /// </summary>
        public static bool ConfirmSecurityWarning(IWin32Window owner)
        {
            string extensions = string.Join("、", Program.SupportedExtensions);
            string message =
                "接下来会做两件事：\r\n\r\n" +
                "1) 把 OpenChoose 注册到 " + extensions + " 的“打开方式”列表；\r\n" +
                "2) 尝试把这些文件类型的默认打开方式改成 OpenChoose。\r\n\r\n" +
                "这一步需要写入注册表（HKCU\\Software\\Classes），部分安全软件\r\n" +
                "（360 安全卫士 / 火绒 / 电脑管家 / Windows Defender 等）会把这种\r\n" +
                "“修改文件关联”的行为当成可疑操作并弹出拦截提示。\r\n\r\n" +
                "如果出现拦截提示，请选择“允许 / 信任 / 放行”；否则设置不会生效，\r\n" +
                "双击 PPT 仍会用原来的程序打开。\r\n\r\n" +
                "现在继续设置吗？";

            return MessageBox.Show(owner, message, Program.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1) == DialogResult.Yes;
        }

        /// <summary>
        /// 注册文件关联并尝试设为默认；showUi 为 false 时静默执行（只写注册表）。
        /// showUi 为 true 时先给出安全软件提醒，用户取消则不写任何注册表。
        /// </summary>
        public static SetDefaultResult Apply(IWin32Window owner, bool showUi)
        {
            if (showUi && !ConfirmSecurityWarning(owner))
            {
                Log.Write("用户取消了“设为默认打开方式”（安全软件提醒之后）。");
                return null;
            }

            SetDefaultResult result;
            try
            {
                result = FileAssociation.SetAsDefault();
            }
            catch (Exception ex)
            {
                Log.Write("设置默认打开方式失败", ex);
                if (showUi)
                {
                    MessageBox.Show(owner, "设置默认打开方式失败：\r\n" + ex.Message,
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return null;
            }

            if (!showUi || result == null)
            {
                return result;
            }

            if (result.NeedsSettings)
            {
                string extensions = string.Join("、", result.ProtectedExtensions.ToArray());
                string message =
                    "OpenChoose 已加入“打开方式”列表。\r\n\r\n" +
                    "Windows 10 / 11 会保护默认应用的设置（" + extensions + " 由系统的 UserChoice 决定），" +
                    "程序无法直接修改。\r\n\r\n" +
                    "接下来请在“默认应用”窗口中，把 .pptx / .ppt 改为\r\n" +
                    "“" + Program.FriendlyAppName + "”。\r\n\r\n" +
                    "（若过程中安全软件弹出拦截提示，请选择“允许 / 信任”。）\r\n\r\n" +
                    "是否现在打开该设置窗口？";

                if (MessageBox.Show(owner, message, Program.AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    Launcher.OpenDefaultAppsSettings();
                }
            }
            else
            {
                MessageBox.Show(owner,
                    "已把 OpenChoose 设为 .ppt / .pptx 等文件的默认打开方式。\r\n双击 PPT 文件即可弹出选择窗口。\r\n\r\n" +
                    "若刚刚安全软件弹出过拦截提示，请再点一次“设为默认打开方式”，并在提示里选择“允许”。",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return result;
        }

        /// <summary>静默设置默认打开方式（供安装程序调用）。</summary>
        public static SetDefaultResult ApplySilently()
        {
            return Apply(null, false);
        }
    }
}
