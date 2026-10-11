using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenChoose
{
    /// <summary>界面相关的辅助方法（含高 DPI 支持）。</summary>
    internal static class Ui
    {
        private const int LogPixelsX = 88;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        /// <summary>命令行 / 配置指定的界面缩放（null 表示按系统 DPI 自动）。</summary>
        public static float? ScaleOverride;

        /// <summary>进程看到的系统 DPI（96 = 100%，120 = 125%，144 = 150%，192 = 200%）。</summary>
        public static int GetSystemDpi()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    int dpi = GetDeviceCaps(dc, LogPixelsX);
                    ReleaseDC(IntPtr.Zero, dc);
                    if (dpi > 0)
                    {
                        return dpi;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取系统 DPI 失败", ex);
            }

            return 96;
        }

        /// <summary>
        /// 界面倍率：叠加在系统 DPI 缩放之上的额外倍率（1.0 = 完全跟随系统 DPI）。
        /// 例如 --scale=1.5 表示界面比系统 DPI 缩放再大 50%。
        /// </summary>
        public static float GetOverrideFactor()
        {
            if (ScaleOverride.HasValue && ScaleOverride.Value > 0F)
            {
                return ScaleOverride.Value;
            }

            return 1F;
        }

        /// <summary>DPI 描述文字，例如 “240 DPI（250%）”。</summary>
        public static string DescribeDpi()
        {
            int dpi = GetSystemDpi();
            return dpi + " DPI（" + Math.Round(dpi / 96F * 100F) + "%）";
        }

        /// <summary>按缩放系数取整。</summary>
        public static int Scale(int value, float scale)
        {
            return (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }

        /// <summary>选择合适的中文界面字体。</summary>
        public static Font CreateUiFont()
        {
            string[] preferred = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            foreach (string name in preferred)
            {
                try
                {
                    Font font = new Font(name, 9F, FontStyle.Regular, GraphicsUnit.Point);
                    if (string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return font;
                    }

                    font.Dispose();
                }
                catch
                {
                }
            }

            return SystemFonts.MessageBoxFont;
        }

        /// <summary>本程序的图标（用于窗体、托盘、列表）。</summary>
        public static Icon LoadAppIcon()
        {
            try
            {
                string path = StartupManager.ExecutablePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Icon icon = Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取程序图标失败", ex);
            }

            return SystemIcons.Application;
        }

        /// <summary>读取可执行文件的图标（找不到时用通用图标）。</summary>
        public static Icon LoadIconFor(string executablePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                {
                    Icon icon = Icon.ExtractAssociatedIcon(executablePath);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取图标失败: " + executablePath, ex);
            }

            return SystemIcons.Application;
        }

        /// <summary>程序友好名称：优先使用文件说明，其次文件名。</summary>
        public static string DescribeExecutable(string executablePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);
                    if (info != null && !string.IsNullOrEmpty(info.FileDescription))
                    {
                        return info.FileDescription.Trim();
                    }

                    return Path.GetFileNameWithoutExtension(executablePath);
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取程序信息失败: " + executablePath, ex);
            }

            return null;
        }

        /// <summary>从 shell 命令行中取出第一个可执行文件路径。</summary>
        public static string ExtractExecutableFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return null;
            }

            string text = command.Trim();
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = text.IndexOf('"', 1);
                if (end > 1)
                {
                    return text.Substring(1, end - 1);
                }
            }

            int space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : text;
        }

        /// <summary>当前默认打开程序的显示名称。</summary>
        public static string DescribeCurrentDefault(string extension)
        {
            try
            {
                string command = FileAssociation.GetEffectiveOpenCommand(extension);
                string executable = ExtractExecutableFromCommand(command);
                if (string.IsNullOrEmpty(executable))
                {
                    return "未设置";
                }

                string name = DescribeExecutable(executable);
                if (string.IsNullOrEmpty(name))
                {
                    name = Path.GetFileName(executable);
                }

                return name;
            }
            catch (Exception ex)
            {
                Log.Write("读取默认打开程序失败: " + extension, ex);
                return "未知";
            }
        }
    }
}
