using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>命令行选项。</summary>
    internal static class AppOptions
    {
        public static bool StartInTray { get; private set; }
        public static bool NoModules { get; private set; }
        public static bool Demo { get; private set; }
        public static bool Snapshot { get; private set; }
        public static string SnapshotDir { get; private set; }
        public static string StartPage { get; private set; }

        public static void Parse(string[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                string raw = (args[i] ?? string.Empty).Trim();
                if (raw.Length == 0) continue;
                string arg = raw.TrimStart('-', '/').ToLowerInvariant();

                if (arg == "tray" || arg == "minimized") StartInTray = true;
                else if (arg == "no-modules" || arg == "nomodules") NoModules = true;
                else if (arg == "demo") Demo = true;
                else if (arg == "snapshot")
                {
                    Snapshot = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) SnapshotDir = args[++i];
                }
                else if (arg.StartsWith("page"))
                {
                    int index = raw.IndexOf('=');
                    if (index > 0) StartPage = raw.Substring(index + 1).Trim();
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) StartPage = args[++i];
                }
            }
        }
    }

    internal static class Program
    {
        private const string MutexName = "LTFC.SingleInstance.Mutex";

        [STAThread]
        private static void Main(string[] args)
        {
            AppOptions.Parse(args);

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    Ask.Info(null, Theme.ShortName, Theme.ProductName + " 已经在运行中。");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppPaths.EnsureDataDir();
                HookGlobalExceptions();

                AppLog.Info("LTFC 启动，版本 " + Theme.VersionText + "，参数：" + string.Join(" ", args ?? new string[0]));

                try
                {
                    using (var shell = new ShellForm())
                    {
                        Application.Run(shell);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Exception_("主窗口异常退出", ex);
                    Ask.Error(null, Theme.ShortName,
                        "程序发生异常：" + ex.Message,
                        "详细日志：" + AppLog.FilePath);
                }
                finally
                {
                    Modules.StopAll();
                    Modules.DisposeAll();
                    AppLog.Info("LTFC 已退出。");
                }

                GC.KeepAlive(mutex);
            }
        }

        /// <summary>全局异常处理：任何模块出错都不允许直接结束主程序。</summary>
        private static void HookGlobalExceptions()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
            {
                AppLog.Exception_("UI 线程未处理异常", e.Exception);
                Ask.Warn(null, Theme.ShortName, "发生未处理的错误：" + e.Exception.Message,
                    "详细日志：" + AppLog.FilePath);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLog.Exception_("应用域未处理异常", e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                AppLog.Exception_("未观察的任务异常", e.Exception);
                e.SetObserved();
            };
        }
    }
}
