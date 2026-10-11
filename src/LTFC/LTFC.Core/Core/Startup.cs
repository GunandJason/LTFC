using System;
using Microsoft.Win32;

namespace LTFC
{
    /// <summary>
    /// 开机自启动（写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。
    /// 只写当前用户，避免需要管理员权限。
    /// </summary>
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "LTFC";
        private const string StartArg = "--tray";

        /// <summary>命令行参数（自启动时携带）。</summary>
        public static string StartupArgument { get { return StartArg; } }

        /// <summary>当前注册表中的自启动命令行（未设置时为空）。</summary>
        public static string CurrentCommand
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        if (key == null) return string.Empty;
                        return Convert.ToString(key.GetValue(ValueName, string.Empty));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("读取自启动项失败", ex);
                    return string.Empty;
                }
            }
        }

        /// <summary>程序是否已登记为开机自启动。</summary>
        public static bool IsEnabled
        {
            get
            {
                string command = CurrentCommand;
                if (string.IsNullOrEmpty(command)) return false;
                return command.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static string ExePath
        {
            get { return System.IO.Path.Combine(AppPaths.ExeDir, "LTFC.exe"); }
        }

        /// <summary>期望写入的自启动命令行。</summary>
        public static string DesiredCommand
        {
            get { return "\"" + ExePath + "\" " + StartArg; }
        }

        /// <summary>写入 / 移除自启动项。</summary>
        public static bool Apply(bool enabled, out string message)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        message = "无法打开注册表启动项。";
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, DesiredCommand, RegistryValueKind.String);
                        message = "已设置为开机自启动：" + DesiredCommand;
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                        message = "已取消开机自启动。";
                    }
                }

                AppLog.Info(message);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Exception_("设置自启动失败", ex);
                message = "设置开机自启动失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 启动路径迁移：如果注册表里的自启动命令指向别的位置（例如换了安装目录），
        /// 在开启自启动的前提下自动改写为当前路径。
        /// </summary>
        public static void SyncWithSetting(bool enabled)
        {
            try
            {
                string current = CurrentCommand;
                if (!enabled)
                {
                    if (!string.IsNullOrEmpty(current)) Apply(false, out _);
                    return;
                }

                if (!string.Equals(current, DesiredCommand, StringComparison.OrdinalIgnoreCase))
                {
                    Apply(true, out _);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("同步自启动项失败", ex);
            }
        }
    }
}
