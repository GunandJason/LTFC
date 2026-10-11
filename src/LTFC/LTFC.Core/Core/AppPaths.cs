using System;
using System.IO;

namespace LTFC
{
    /// <summary>LTFC 的固定路径（数据目录、日志、各模块配置）。</summary>
    internal static class AppPaths
    {
        private static string _dataDir;

        /// <summary>程序数据目录：%APPDATA%\LTFC。</summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir == null)
                {
                    string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    _dataDir = Path.Combine(root, "LTFC");
                }
                return _dataDir;
            }
        }

        /// <summary>全局设置文件。</summary>
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.ini"); } }

        /// <summary>内置资源目录（赞赏码等，随程序一起发布）。</summary>
        public static string AssetsDir { get { return Path.Combine(ExeDir, "assets"); } }

        /// <summary>赞赏码图片路径（在“总设置 → 关于”中显示）。</summary>
        public static string RewardQrFile { get { return Path.Combine(AssetsDir, "赞赏码.jpg"); } }

        /// <summary>内置模块目录（子程序随 LTFC 一起发布，用户无需另外下载）。</summary>
        public static string ModulesDir { get { return Path.Combine(ExeDir, "modules"); } }

        private static string _autoNoticeConfig;

        /// <summary>
        /// AutoNotice 的配置文件（固定名称 command.txt）。解析顺序：
        /// 1) 设置里指定的 AutoNotice.ConfigPath；
        /// 2) 程序目录下的 AutoNotice\command.txt（发布后的内置位置）；
        /// 3) 源码目录 src\AutoNotice\command.txt（开发时直接使用仓库中的配置）；
        /// 4) %APPDATA%\LTFC\AutoNotice\command.txt（兜底，会按模板自动创建）。
        /// </summary>
        public static string AutoNoticeConfigFile
        {
            get
            {
                if (!string.IsNullOrEmpty(_autoNoticeConfig)) return _autoNoticeConfig;

                string custom = Settings.Get("AutoNotice.ConfigPath", string.Empty);
                if (!string.IsNullOrEmpty(custom)) return Cache(custom);

                string bundled = Path.Combine(ExeDir, "AutoNotice", "command.txt");
                if (File.Exists(bundled)) return Cache(bundled);

                string dev = FindSourceConfig();
                if (!string.IsNullOrEmpty(dev)) return Cache(dev);

                return Cache(Path.Combine(DataDir, "AutoNotice", "command.txt"));
            }
        }

        private static string Cache(string path)
        {
            _autoNoticeConfig = path;
            return path;
        }

        /// <summary>从程序目录向上查找仓库里的 src\AutoNotice\command.txt（仅开发环境会命中）。</summary>
        private static string FindSourceConfig()
        {
            try
            {
                var dir = new DirectoryInfo(ExeDir);
                for (int depth = 0; depth < 8 && dir != null; depth++)
                {
                    string candidate = Path.Combine(dir.FullName, "src", "AutoNotice", "command.txt");
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>AutoNotice 的窗口/设置页需要保证配置目录存在。</summary>
        public static void EnsureAutoNoticeDir()
        {
            try
            {
                string dir = Path.GetDirectoryName(AutoNoticeConfigFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>OpenChoose 的软件列表配置（与子程序共用）。</summary>
        public static string OpenChooseConfigFile { get { return Path.Combine(DataDir, "openchoose.ini"); } }

        /// <summary>本程序（LTFC.exe）所在目录，子程序默认也在此目录。</summary>
        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>日志目录。</summary>
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }

        /// <summary>确认数据目录存在。</summary>
        public static void EnsureDataDir()
        {
            try { Directory.CreateDirectory(DataDir); } catch (Exception) { }
            try { Directory.CreateDirectory(LogDir); } catch (Exception) { }
        }
    }
}
