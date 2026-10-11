using System;
using System.Collections.Generic;
using System.IO;

namespace LTFC
{
    /// <summary>
    /// 模块清单（与侧边栏顺序一致）。
    /// 侧边栏结构：ClassTell(消息/过期)、OpenChoose(设置)、
    /// AutoNotice(设置)、视频下载、总设置。
    /// 外部模块是独立进程、独立窗口；重复点击只显示已有窗口，不会开出第二个实例。
    /// </summary>
    internal static class Modules
    {
        public const string ClassTellId = "classtell";
        public const string OpenChooseId = "openchoose";
        public const string AutoNoticeId = "autonotice";
        public const string VideoDownloadId = "videodownload";

        // 侧边栏子项键后缀（完整键 = 模块 Id + "." + 后缀）
        public const string PageMessages = "messages";
        public const string PageExpired = "expired";
        public const string PageSettings = "settings";

        private static readonly ModuleDefinition[] All = BuildDefinitions();

        private static ModuleDefinition[] BuildDefinitions()
        {
            var classTell = new ModuleDefinition(ClassTellId, "ClassTell", ModuleKind.ExternalProcess,
                @"modules\ClassTell\ClassTell.exe",
                "邮箱指令通知：把邮件当作远程指令，收到 C 类邮件时提醒（独立窗口运行）。")
            {
                Glyph = "\uE715",
                GlyphFallback = "@",
                FallbackPaths = DevFallbacks(@"src\ClassTell\bin\{cfg}\net48\ClassTell.exe")
            };
            classTell.AddPage(PageMessages, "消息", "打开软件之后收到的指令邮件", true);
            classTell.AddPage(PageExpired, "过期", "打开软件之前的未读邮件", true);

            var openChoose = new ModuleDefinition(OpenChooseId, "OpenChoose", ModuleKind.ExternalProcess,
                @"modules\OpenChoose\PPTChooser.exe",
                "打开方式选择器：双击演示文稿时选择用哪个程序打开，可自定义软件。")
            {
                Glyph = "\uE8B7",
                GlyphFallback = "▤",
                Arguments = "--startup",
                SupportsFileArgument = true,
                FallbackPaths = DevFallbacks(@"src\OpenChoose\src\PPTChooser\bin\{cfg}\PPTChooser.exe")
            };
            openChoose.AddPage(PageSettings, "设置", "软件列表与打开方式的首选项");

            var autoNotice = new ModuleDefinition(AutoNoticeId, "AutoNotice", ModuleKind.Internal, string.Empty,
                "定时提醒：按星期与时间弹出系统通知，配置文件为 command.txt。")
            {
                Glyph = "\uE823",
                GlyphFallback = "◔"
            };
            autoNotice.AddPage(PageSettings, "设置", "提醒配置（command.txt）、开关与下次提醒");

            var videoDownload = new ModuleDefinition(VideoDownloadId, "视频下载", ModuleKind.ExternalProcess,
                @"VideoDownload\4KVideoDownloaderPlusPortable.exe",
                "内置视频下载工具：粘贴链接即可下载，独立窗口运行，不套用 LTFC 主题。")
            {
                Glyph = "\uE896",
                GlyphFallback = "⤓",
                AppliesTheme = false,
                SingleInstance = true,
                FallbackPaths = FindVideoDownloadPaths()
            };

            return new ModuleDefinition[] { classTell, openChoose, autoNotice, videoDownload };
        }

        /// <summary>视频下载工具的备用查找位置（发布时随 LTFC 内置到 VideoDownload 目录）。</summary>
        private static string[] FindVideoDownloadPaths()
        {
            var list = new List<string>();
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                {
                    foreach (string dir in Directory.GetDirectories(desktop, "4KVideoDownloader*"))
                    {
                        list.Add(Path.Combine(dir, "4KVideoDownloaderPlusPortable.exe"));
                        list.Add(Path.Combine(dir, "App", "4KVideoDownloaderPlus64", "4kvideodownloaderplus.exe"));
                    }
                }
            }
            catch (Exception)
            {
            }

            return list.ToArray();
        }

        private static string _repoRoot;
        private static bool _repoRootResolved;

        /// <summary>源码根目录（含 LTFC.sln）；发布版不在源码树里时返回 null。</summary>
        public static string RepoRoot
        {
            get
            {
                if (_repoRootResolved) return _repoRoot;
                _repoRootResolved = true;

                try
                {
                    var dir = new DirectoryInfo(AppPaths.ExeDir);
                    for (int depth = 0; depth < 8 && dir != null; depth++)
                    {
                        if (File.Exists(Path.Combine(dir.FullName, "LTFC.sln")))
                        {
                            _repoRoot = dir.FullName;
                            break;
                        }
                        dir = dir.Parent;
                    }
                }
                catch (Exception)
                {
                }

                return _repoRoot;
            }
        }

        /// <summary>
        /// 开发环境的备用查找路径：按 {cfg} = Release / Debug 依次尝试源码树里的编译输出。
        /// 发布版（dist）里这些路径不存在，会自动忽略。
        /// </summary>
        private static string[] DevFallbacks(string relativePattern)
        {
            var list = new List<string>();
            string root = RepoRoot;
            if (string.IsNullOrEmpty(root)) return list.ToArray();

            foreach (string cfg in new[] { "Release", "Debug" })
            {
                string relative = (relativePattern ?? string.Empty).Replace("{cfg}", cfg);
                if (relative.Length == 0) continue;
                list.Add(Path.Combine(root, relative));
            }

            return list.ToArray();
        }

        private static readonly Dictionary<string, ModuleProcess> ProcessMap =
            new Dictionary<string, ModuleProcess>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<ModuleProcess> ProcessList = new List<ModuleProcess>();

        /// <summary>全部模块定义（顺序 = 侧边栏顺序）。</summary>
        public static IReadOnlyList<ModuleDefinition> Definitions { get { return All; } }

        public static ModuleDefinition Find(string id)
        {
            foreach (ModuleDefinition definition in All)
            {
                if (string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase)) return definition;
            }
            return null;
        }

        /// <summary>
        /// 解析模块可执行文件的实际路径：设置里的自定义路径 → LTFC 目录 → modules 子目录 → 备用路径。
        /// 都不存在时返回最“标准”的位置，便于界面提示用户该把程序放到哪里。
        /// </summary>
        public static string ResolveExecutablePath(ModuleDefinition definition)
        {
            if (definition == null || definition.Kind != ModuleKind.ExternalProcess) return string.Empty;

            var candidates = new List<string>();
            string custom = Settings.Get("Path." + definition.Id, string.Empty);
            if (!string.IsNullOrEmpty(custom)) candidates.Add(custom);

            if (!string.IsNullOrEmpty(definition.FileName))
            {
                try { candidates.Add(Path.Combine(AppPaths.ExeDir, definition.FileName)); }
                catch (ArgumentException) { }
            }

            if (definition.FallbackPaths != null)
            {
                foreach (string path in definition.FallbackPaths)
                {
                    if (!string.IsNullOrEmpty(path)) candidates.Add(path);
                }
            }

            foreach (string path in candidates)
            {
                try
                {
                    if (File.Exists(path)) return path;
                }
                catch (Exception)
                {
                }
            }

            return candidates.Count > 0 ? candidates[0] : string.Empty;
        }

        /// <summary>
        /// “启动 / 显示窗口”：未运行时启动一个新窗口；已经在运行时直接显示它已有的窗口，不重复启动。
        /// </summary>
        public static bool ActivateOrStart(string id, out string message)
        {
            ModuleProcess process = Process(id);
            if (process == null)
            {
                message = "该模块没有独立进程窗口。";
                return false;
            }

            if (process.IsRunning) return process.ActivateWindow(out message);
            return process.Start(out message);
        }

        /// <summary>取（或创建）某个外部模块的进程管理对象。</summary>
        public static ModuleProcess Process(string id)
        {
            lock (ProcessMap)
            {
                ModuleProcess existing;
                if (ProcessMap.TryGetValue(id, out existing)) return existing;

                ModuleDefinition definition = Find(id);
                if (definition == null || definition.Kind != ModuleKind.ExternalProcess) return null;

                var process = new ModuleProcess(definition);
                ProcessMap[id] = process;
                ProcessList.Add(process);
                return process;
            }
        }

        /// <summary>所有外部模块的进程管理对象。</summary>
        public static IList<ModuleProcess> ExternalProcesses
        {
            get
            {
                lock (ProcessMap)
                {
                    return new List<ModuleProcess>(ProcessList);
                }
            }
        }

        /// <summary>状态文件路径（子程序可选地把自己的状态写到这里）。</summary>
        public static string StatusFileFor(string id)
        {
            return Path.Combine(AppPaths.DataDir, "status-" + id + ".ini");
        }

        /// <summary>启动所有启用的外部模块（开机或点击“全部启动”）。返回失败提示。</summary>
        public static IList<string> StartAllEnabled()
        {
            var failures = new List<string>();
            foreach (ModuleProcess process in ExternalProcesses)
            {
                if (!Settings.ModuleEnabled(process.Definition.Id)) continue;
                string message;
                if (!process.Start(out message)) failures.Add(message);
            }
            return failures;
        }

        /// <summary>停止所有在运行的外部模块。</summary>
        public static void StopAll()
        {
            foreach (ModuleProcess process in ExternalProcesses)
            {
                string message;
                process.Stop(out message);
            }
        }

        /// <summary>刷新所有外部模块状态。</summary>
        public static void RefreshAll()
        {
            foreach (ModuleProcess process in ExternalProcesses) process.Refresh();
        }

        /// <summary>释放进程对象。</summary>
        public static void DisposeAll()
        {
            foreach (ModuleProcess process in ExternalProcesses) process.Dispose();
        }
    }
}
