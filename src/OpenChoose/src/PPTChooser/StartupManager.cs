using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace OpenChoose
{
    /// <summary>
    /// 开机自启动管理：HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    /// </summary>
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "OpenChoose";

        /// <summary>当前程序路径。</summary>
        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return Assembly.GetEntryAssembly().Location;
                }
                catch
                {
                    return System.Windows.Forms.Application.ExecutablePath;
                }
            }
        }

        /// <summary>开机自启动使用的命令行。</summary>
        public static string CommandLine
        {
            get { return Launcher.Quote(ExecutablePath) + " --startup"; }
        }

        /// <summary>是否已启用开机自启动。</summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    {
                        if (key == null)
                        {
                            return false;
                        }

                        string value = key.GetValue(ValueName) as string;
                        return !string.IsNullOrEmpty(value);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("读取自启动项失败", ex);
                    return false;
                }
            }
        }

        /// <summary>启用开机自启动（托盘运行）。</summary>
        public static void Enable()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法写入注册表启动项。");
                }

                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            }

            Log.Write("已启用开机自启动: " + CommandLine);
        }

        /// <summary>关闭开机自启动。</summary>
        public static void Disable()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key != null && key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, false);
                }
            }

            Log.Write("已关闭开机自启动");
        }

        /// <summary>按需要设置自启动状态。</summary>
        public static void Set(bool enabled)
        {
            if (enabled)
            {
                Enable();
            }
            else
            {
                Disable();
            }
        }

        /// <summary>当前自启动项指向的程序（用于判断是否仍是本程序）。</summary>
        public static string GetRegisteredCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    return key.GetValue(ValueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>自启动项是否指向一个仍然存在的程序。</summary>
        public static bool IsStale()
        {
            string command = GetRegisteredCommand();
            if (string.IsNullOrEmpty(command))
            {
                return false;
            }

            return !File.Exists(ExecutablePath) ||
                   command.IndexOf(ExecutablePath, StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
