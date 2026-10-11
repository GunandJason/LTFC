using System;
using System.IO;

namespace LTFC
{
    /// <summary>
    /// 统一的模块启动入口（页面、侧边栏、首页都用它，保证行为一致）：
    /// · 外部程序：没有运行时新开一个独立窗口；已经在运行时只显示它已有的窗口，不会重复启动；
    /// · 内置模块（AutoNotice）：显示 LTFC 提供的窗口（同样单实例）。
    /// </summary>
    internal static class ModuleLauncher
    {
        /// <summary>AutoNotice 服务（由主窗口在启动时创建）。</summary>
        public static AutoNoticeService AutoNotice;

        private static ShellForm _shell;

        public static void Attach(ShellForm shell)
        {
            _shell = shell;
        }

        /// <summary>启动或显示模块窗口。返回空字符串表示成功，否则是给用户看的提示。</summary>
        public static string LaunchOrShow(ModuleDefinition definition)
        {
            if (definition == null) return "找不到该模块。";
            if (!Settings.ModuleEnabled(definition.Id))
            {
                return definition.DisplayName + " 已在设置中禁用，请先在它的“设置”页里启用。";
            }

            if (definition.Kind == ModuleKind.Internal)
            {
                if (definition.Id == Modules.AutoNoticeId) return AutoNoticeWindow.ShowFor(_shell, AutoNotice);
                return "该模块没有独立窗口。";
            }

            ModuleProcess process = Modules.Process(definition.Id);
            if (process == null) return "找不到 " + definition.DisplayName + " 的进程配置。";

            string message;
            if (process.IsRunning || process.HasExternalInstance())
            {
                // 已经启动过：直接显示已有窗口，不再开一个
                if (process.ActivateWindow(out message)) return string.Empty;
                return message;
            }

            if (!process.Start(out message)) return message;
            return string.Empty;
        }

        /// <summary>停止模块（外部程序为结束进程；内置模块为停用定时提醒）。</summary>
        public static string StopModule(ModuleDefinition definition)
        {
            if (definition == null) return "找不到该模块。";

            if (definition.Kind == ModuleKind.Internal)
            {
                if (definition.Id == Modules.AutoNoticeId && AutoNotice != null)
                {
                    Settings.AutoNoticeEnabled = false;
                    AutoNotice.Stop();
                    return string.Empty;
                }
                return "该模块由主程序直接提供，无需单独停止。";
            }

            ModuleProcess process = Modules.Process(definition.Id);
            if (process == null) return "找不到 " + definition.DisplayName + " 的进程配置。";

            string message;
            return process.Stop(out message) ? string.Empty : message;
        }

        /// <summary>用指定参数启动（例如打开子程序自己的设置界面）。</summary>
        public static string StartWithArguments(ModuleDefinition definition, string arguments)
        {
            if (definition == null || definition.Kind != ModuleKind.ExternalProcess) return "该模块没有独立进程。";

            ModuleProcess process = Modules.Process(definition.Id);
            if (process == null) return "找不到 " + definition.DisplayName + " 的进程配置。";

            string message;
            if (process.IsRunning || process.HasExternalInstance()) return string.Empty;

            if (!process.StartWithArguments(arguments, out message)) return message;
            return string.Empty;
        }

        /// <summary>用指定文件启动（打开方式选择器）。</summary>
        public static string OpenFileWith(ModuleDefinition definition, string file)
        {
            if (string.IsNullOrEmpty(file)) return "没有选择文件。";
            if (!File.Exists(file)) return "文件不存在：" + file;

            return StartWithArguments(definition, "\"" + file + "\"");
        }

        /// <summary>重新读取 AutoNotice 的 command.txt。</summary>
        public static void ReloadAutoNotice()
        {
            if (AutoNotice == null) ServiceHolder.Ensure();
            AutoNoticeService service = AutoNotice ?? ServiceHolder.Current;
            service.Reload();
            ModuleLauncher.AutoNotice = service;
        }
    }
}
