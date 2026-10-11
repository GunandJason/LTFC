// 临时探针文件，稍后由真实实现替换
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OpenChoose
{
    /// <summary>
    /// 入口点：
    /// <list type="bullet">
    /// <item>PPTChooser.exe "文件.pptx"    -> 弹出“选择打开 PPT 的程序”窗口</item>
    /// <item>PPTChooser.exe --startup      -> 开机自启动（托盘后台）</item>
    /// <item>PPTChooser.exe --register     -> 注册文件关联（写入 HKCU）</item>
    /// <item>PPTChooser.exe --unregister   -> 取消文件关联</item>
    /// <item>PPTChooser.exe --setdefault   -> 尝试设为 .ppt/.pptx 的默认打开方式</item>
    /// <item>PPTChooser.exe --selftest [f] -> 输出诊断报告</item>
    /// </list>
    /// </summary>
    internal static class Program
    {
        internal const string AppName = "OpenChoose";
        internal const string ProgId = "OpenChoose.PPT";
        internal const string FriendlyAppName = "OpenChoose（PPT 打开方式选择器）";
        internal const string AppDescription = "双击 PPT 时选择用 PowerPoint、WPS 或其它程序打开";

        /// <summary>本程序支持的文件类型。</summary>
        internal static readonly string[] SupportedExtensions =
        {
            ".ppt", ".pptx", ".pps", ".ppsx", ".pot", ".potx"
        };

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            List<string> flags = new List<string>();
            string file = null;
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                string argument = raw.Trim();
                if (argument.Length > 1 && (argument[0] == '-' || argument[0] == '/'))
                {
                    string flag = argument.TrimStart('-', '/');
                    string value = null;

                    // 支持 --scale=1.5 / --scale:1.5
                    int separator = flag.IndexOf('=');
                    if (separator < 0)
                    {
                        separator = flag.IndexOf(':');
                    }

                    if (separator > 0)
                    {
                        value = flag.Substring(separator + 1);
                        flag = flag.Substring(0, separator);
                    }

                    string name = flag.ToLowerInvariant();
                    flags.Add(name);

                    // 也支持 --scale 1.5（值作为下一个参数）
                    if (name == "scale" && value == null && i + 1 < args.Length && IsScaleValue(args[i + 1]))
                    {
                        value = args[++i].Trim();
                    }

                    if (name == "scale")
                    {
                        ApplyScaleOption(value);
                    }
                }
                else if (file == null)
                {
                    file = argument;
                }
            }

            bool silent = flags.Contains("silent") || flags.Contains("quiet") || flags.Contains("q");

            try
            {
                if (flags.Contains("help") || flags.Contains("h") || flags.Contains("?"))
                {
                    ShowUsage();
                    return 0;
                }

                if (flags.Contains("register"))
                {
                    FileAssociation.Register();
                    return 0;
                }

                if (flags.Contains("unregister"))
                {
                    FileAssociation.Unregister();
                    return 0;
                }

                if (flags.Contains("selftest") || flags.Contains("diagnostics"))
                {
                    return Diagnostics.WriteReport(file, silent);
                }

                if (flags.Contains("setdefault") || flags.Contains("default"))
                {
                    DefaultProgramHelper.Apply(null, !silent);
                    return 0;
                }

                if (flags.Contains("startup") || flags.Contains("tray") || flags.Contains("minimized"))
                {
                    return RunTray();
                }

                if (flags.Contains("enable-autostart") || flags.Contains("autostart-on"))
                {
                    StartupManager.Enable();
                    return 0;
                }

                if (flags.Contains("disable-autostart") || flags.Contains("autostart-off"))
                {
                    StartupManager.Disable();
                    return 0;
                }

                if (file == null)
                {
                    // 无参数启动（例如从开始菜单）：先让用户选择一个 PPT 文件
                    file = AskForFile();
                    if (file == null)
                    {
                        return 0;
                    }
                }

                return RunChooser(file);
            }
            catch (Exception ex)
            {
                Log.Write("运行失败", ex);
                if (!silent)
                {
                    MessageBox.Show("OpenChoose 运行出错：\r\n" + ex.Message,
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return 1;
            }
        }

        /// <summary>判断参数是否像缩放系数（用于 --scale 1.5 写法）。</summary>
        private static bool IsScaleValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string text = value.Trim();
            if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            double parsed;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        /// <summary>处理 --scale 参数（auto / 0.5 ~ 3.0）。</summary>
        private static void ApplyScaleOption(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                value == "0")
            {
                Ui.ScaleOverride = null;
                return;
            }

            float scale;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
                scale >= 0.5F && scale <= 3F)
            {
                Ui.ScaleOverride = scale;
                Log.Write("界面缩放由命令行指定: " + scale.ToString("0.##", CultureInfo.InvariantCulture));
            }
            else
            {
                Log.Write("忽略无效的 --scale 参数: " + value);
            }
        }

        /// <summary>显示选择窗口（双击 PPT 文件时进入这里）。</summary>
        private static int RunChooser(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("找不到要打开的文件：\r\n" + filePath,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            AppConfig config = AppConfig.Load();
            string saved = config.GetSavedProgram(Path.GetExtension(filePath));
            bool shiftPressed = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

            // 已记住的选择：直接打开（按住 Shift 可强制显示选择窗口）
            if (!shiftPressed && config.RememberChoice &&
                !string.IsNullOrEmpty(saved) && File.Exists(saved))
            {
                try
                {
                    Launcher.OpenWith(saved, filePath);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Write("使用记住的程序打开失败，改为显示选择窗口", ex);
                }
            }

            using (MainForm form = new MainForm(filePath, config))
            {
                Application.Run(form);
            }

            return 0;
        }

        /// <summary>托盘模式（开机自启动）。</summary>
        private static int RunTray()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\OpenChoose.TrayApp", out createdNew))
            {
                if (!createdNew)
                {
                    Log.Write("托盘程序已在运行，忽略本次启动。");
                    return 0;
                }

                // 托盘常驻时静默补齐“打开方式”注册：右键 → 打开方式里始终能看到 OpenChoose
                FileAssociation.EnsureRegistered();

                Application.Run(new TrayApp());
            }

            return 0;
        }

        /// <summary>选择要打开的 PPT 文件。</summary>
        private static string AskForFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要打开的 PPT 文件";
                dialog.Filter =
                    "PowerPoint 演示文稿 (*.ppt;*.pptx;*.pps;*.ppsx)|*.ppt;*.pptx;*.pps;*.ppsx|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.FileName;
                }
            }

            return null;
        }

        /// <summary>显示命令行用法。</summary>
        private static void ShowUsage()
        {
            string text =
                AppName + " - PPT 打开方式选择器 (" + typeof(Program).Assembly.GetName().Version + ")\r\n\r\n" +
                "用法：\r\n" +
                "  PPTChooser.exe \"文件.pptx\"    为指定文件选择打开程序\r\n" +
                "  PPTChooser.exe --startup        开机自启动（托盘后台运行）\r\n" +
                "  PPTChooser.exe --register       注册文件关联（无需管理员）\r\n" +
                "  PPTChooser.exe --unregister     取消文件关联\r\n" +
                "  PPTChooser.exe --setdefault     尝试设为 .ppt/.pptx 默认打开方式\r\n" +
                "  PPTChooser.exe --enable-autostart   启用开机自启动\r\n" +
                "  PPTChooser.exe --disable-autostart  关闭开机自启动\r\n" +
                "  PPTChooser.exe --scale=1.5          在系统 DPI 缩放的基础上再放大 1.5 倍（auto 或 0.5 ~ 3.0）\r\n" +
                "  PPTChooser.exe --selftest [文件] 生成诊断报告\r\n";

            MessageBox.Show(text, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}
