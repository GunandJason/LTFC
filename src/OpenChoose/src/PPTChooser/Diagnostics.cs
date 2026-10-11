using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenChoose
{
    /// <summary>
    /// 诊断报告（<c>PPTChooser.exe --selftest [输出文件]</c>）：输出程序检测与文件关联状态。
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>生成诊断报告；silent 为 true 时不弹窗（供脚本/安装程序使用）。</summary>
        public static int WriteReport(string outputPath, bool silent)
        {
            string report = BuildReport();
            string path = string.IsNullOrEmpty(outputPath)
                ? Path.Combine(AppConfig.DirectoryPath, "diagnostics.txt")
                : outputPath;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, report, Encoding.UTF8);
                Log.Write("诊断报告已写入: " + path);
            }
            catch (Exception ex)
            {
                Log.Write("写入诊断报告失败", ex);
                if (!silent)
                {
                    MessageBox.Show("写入诊断报告失败：\r\n" + ex.Message,
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return 1;
            }

            if (!silent)
            {
                MessageBox.Show("诊断报告已保存到：\r\n" + path, Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return 0;
        }

        /// <summary>生成报告内容。</summary>
        public static string BuildReport()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("OpenChoose / PPTChooser 诊断报告");
            builder.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("程序路径: " + StartupManager.ExecutablePath);
            builder.AppendLine("程序版本: " + Assembly.GetEntryAssembly().GetName().Version);
            builder.AppendLine("目标框架: " + GetTargetFramework());
            builder.AppendLine("CLR 版本: " + Environment.Version);
            builder.AppendLine("操作系统: " + Environment.OSVersion + " (" +
                (Environment.Is64BitOperatingSystem ? "64 位" : "32 位") + ")");
            builder.AppendLine("系统 DPI: " + Ui.DescribeDpi() + "，界面倍率(--scale): " +
                Ui.GetOverrideFactor().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            builder.AppendLine(".NET Framework(注册表): " + GetDotNetVersion());
            builder.AppendLine();

            builder.AppendLine("[检测到的 PPT 程序]");
            List<ProgramCandidate> candidates = ProgramFinder.DetectAll();
            foreach (ProgramCandidate candidate in candidates)
            {
                if (candidate.IsOther)
                {
                    continue;
                }

                string path = candidate.ExecutablePath;
                builder.AppendLine(candidate.DisplayName + ": " +
                    (string.IsNullOrEmpty(path) ? "未检测到" : path + (File.Exists(path) ? " (存在)" : " (文件不存在)")));
            }

            builder.AppendLine();
            builder.AppendLine("[文件关联]");
            builder.AppendLine("本程序已注册为 PPT 打开方式: " + (FileAssociation.IsRegistered() ? "是" : "否"));
            foreach (string extension in Program.SupportedExtensions)
            {
                builder.AppendLine(extension +
                    ": 生效 ProgID=" + Value(FileAssociation.GetEffectiveProgId(extension)) +
                    " | UserChoice=" + Value(FileAssociation.GetUserChoiceProgId(extension)) +
                    " | 打开命令=" + Value(FileAssociation.GetEffectiveOpenCommand(extension)));
            }

            builder.AppendLine();
            builder.AppendLine("[开机自启动]");
            builder.AppendLine("状态: " + (StartupManager.IsEnabled ? "已启用" : "未启用"));
            builder.AppendLine("命令: " + Value(StartupManager.GetRegisteredCommand()));

            builder.AppendLine();
            builder.AppendLine("[配置]");
            builder.AppendLine("配置文件: " + AppConfig.FilePath);
            AppConfig config = AppConfig.Load();
            builder.AppendLine("记住选择: " + (config.RememberChoice ? "是" : "否"));
            foreach (string extension in Program.SupportedExtensions)
            {
                string saved = config.GetSavedProgram(extension);
                if (!string.IsNullOrEmpty(saved))
                {
                    builder.AppendLine("  " + extension + " -> " + saved);
                }
            }

            builder.AppendLine();
            builder.AppendLine("[日志文件]");
            builder.AppendLine(Log.FilePath);

            return builder.ToString();
        }

        private static string GetTargetFramework()
        {
            try
            {
                object[] attributes = Assembly.GetEntryAssembly().GetCustomAttributes(
                    typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false);
                if (attributes != null && attributes.Length > 0)
                {
                    return ((System.Runtime.Versioning.TargetFrameworkAttribute)attributes[0]).FrameworkName;
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取目标框架失败", ex);
            }

            return "未知";
        }

        private static string GetDotNetVersion()
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", false))
                {
                    if (key != null)
                    {
                        object version = key.GetValue("Version");
                        object release = key.GetValue("Release");
                        return Value(version == null ? null : version.ToString()) +
                               " (Release=" + Value(release == null ? null : release.ToString()) + ")";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取 .NET 版本失败", ex);
            }

            return "未知";
        }

        private static string Value(string text)
        {
            return string.IsNullOrEmpty(text) ? "(无)" : text;
        }

    }
}
