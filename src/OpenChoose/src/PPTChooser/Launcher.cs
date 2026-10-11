using System;
using System.Diagnostics;
using System.IO;

namespace OpenChoose
{
    /// <summary>
    /// 负责以 <c>程序.exe "文件.pptx"</c> 的形式启动外部程序。
    /// </summary>
    internal static class Launcher
    {
        /// <summary>用指定程序打开文件。</summary>
        public static void OpenWith(string executablePath, string filePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new ArgumentNullException("executablePath");
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("找不到程序文件：" + executablePath, executablePath);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executablePath;
            startInfo.Arguments = Quote(filePath);
            startInfo.UseShellExecute = true;
            startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            Process.Start(startInfo);
            Log.Write("已启动: " + startInfo.FileName + " " + startInfo.Arguments);
        }

        /// <summary>调用 Windows 自带的“打开方式”对话框。</summary>
        public static void OpenWithWindowsDialog(string filePath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(
                "rundll32.exe", "shell32.dll,OpenAs_RunDLL " + Quote(filePath));
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }

        /// <summary>打开 Windows 的“默认应用”设置页（Win10/11）。</summary>
        public static void OpenDefaultAppsSettings()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("ms-settings:defaultapps");
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log.Write("打开设置失败", ex);
                try
                {
                    // 旧版 Windows 的兼容入口
                    Process.Start("control.exe", "/name Microsoft.DefaultPrograms");
                }
                catch
                {
                }
            }
        }

        /// <summary>打开资源管理器并选中文件。</summary>
        public static void ShowInExplorer(string filePath)
        {
            try
            {
                Process.Start("explorer.exe", "/select," + Quote(filePath));
            }
            catch (Exception ex)
            {
                Log.Write("打开资源管理器失败", ex);
            }
        }

        /// <summary>为命令行参数加引号。</summary>
        public static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Trim('"') + "\"";
        }
    }
}
