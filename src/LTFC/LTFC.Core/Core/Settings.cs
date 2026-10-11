using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LTFC
{
    /// <summary>
    /// 全局设置，保存在 %APPDATA%\LTFC\settings.ini（key=value 文本，便于手工修改）。
    /// </summary>
    internal static class Settings
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static event Action Changed;

        // ---------- 界面 ----------
        public static float FontSize
        {
            get { return GetFloat("FontSize", Theme.DefaultFontSize); }
            set { Set("FontSize", value.ToString("0.##", CultureInfo.InvariantCulture)); }
        }

        public static bool DarkMode
        {
            get { return GetBool("DarkMode", false); }
            set { Set("DarkMode", value ? "1" : "0"); }
        }

        /// <summary>高对比度模式：固定浅色背景方案 + 固定高对比度配色。</summary>
        public static bool HighContrast
        {
            get { return GetBool("HighContrast", false); }
            set { Set("HighContrast", value ? "1" : "0"); }
        }

        public static string AccentKey
        {
            get { return Get("AccentKey", Theme.DefaultPaletteKey); }
            set { Set("AccentKey", value); }
        }

        // ---------- 运行 ----------
        /// <summary>LTFC 开机自启动。</summary>
        public static bool RunAtStartup
        {
            get { return GetBool("RunAtStartup", false); }
            set { Set("RunAtStartup", value ? "1" : "0"); }
        }

        /// <summary>启动后直接驻留托盘。</summary>
        public static bool StartMinimized
        {
            get { return GetBool("StartMinimized", false); }
            set { Set("StartMinimized", value ? "1" : "0"); }
        }

        /// <summary>随主程序启动子程序。</summary>
        public static bool AutoStartModules
        {
            get { return GetBool("AutoStartModules", false); }
            set { Set("AutoStartModules", value ? "1" : "0"); }
        }

        /// <summary>关闭主窗口时驻留托盘。</summary>
        public static bool CloseToTray
        {
            get { return GetBool("CloseToTray", true); }
            set { Set("CloseToTray", value ? "1" : "0"); }
        }

        /// <summary>记录详细日志。</summary>
        public static bool VerboseLog
        {
            get { return GetBool("VerboseLog", false); }
            set { Set("VerboseLog", value ? "1" : "0"); }
        }

        // ---------- 子程序 ----------
        /// <summary>记录某个模块的自定义可执行文件路径（空字符串表示清除）。</summary>
        public static void SetModulePath(string moduleId, string path)
        {
            Set("Path." + moduleId, path ?? string.Empty);
        }

        /// <summary>子程序是否启用（禁用后入口仍可见，但不可启动）。</summary>
        public static bool ModuleEnabled(string moduleId, bool fallback = true)
        {
            return GetBool("Enabled." + moduleId, fallback);
        }

        public static void SetModuleEnabled(string moduleId, bool value)
        {
            Set("Enabled." + moduleId, value ? "1" : "0");
        }

        // ---------- AutoNotice ----------
        public static bool AutoNoticeEnabled
        {
            get { return GetBool("AutoNotice.Enabled", true); }
            set { Set("AutoNotice.Enabled", value ? "1" : "0"); }
        }

        /// <summary>AutoNotice 是否播放提示音。</summary>
        public static bool AutoNoticeSound
        {
            get { return GetBool("AutoNotice.Sound", false); }
            set { Set("AutoNotice.Sound", value ? "1" : "0"); }
        }

        // ---------- 窗口 ----------
        public static int LastPageIndex
        {
            get { return GetInt("LastPageIndex", 0); }
            set { Set("LastPageIndex", value.ToString(CultureInfo.InvariantCulture)); }
        }

        // ---------- 基础读写 ----------
        public static string Get(string key, string fallback)
        {
            EnsureLoaded();
            string value;
            if (Map.TryGetValue(key, out value)) return value;
            return fallback;
        }

        public static bool GetBool(string key, bool fallback)
        {
            string value = Get(key, null);
            if (string.IsNullOrEmpty(value)) return fallback;
            value = value.Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes" || value == "on";
        }

        public static int GetInt(string key, int fallback)
        {
            string value = Get(key, null);
            int parsed;
            if (!string.IsNullOrEmpty(value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        public static float GetFloat(string key, float fallback)
        {
            string value = Get(key, null);
            float parsed;
            if (!string.IsNullOrEmpty(value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        /// <summary>写入设置并立即保存；值未变化时不写盘（避免无意义的 IO）。</summary>
        public static void Set(string key, string value)
        {
            EnsureLoaded();
            string current;
            value = value ?? string.Empty;
            if (Map.TryGetValue(key, out current) && string.Equals(current, value, StringComparison.Ordinal)) return;
            Map[key] = value;
            Save();
            RaiseChanged();
        }

        public static bool HasSetting(string key)
        {
            EnsureLoaded();
            return Map.ContainsKey(key);
        }

        /// <summary>首次运行时补齐默认值。</summary>
        public static void EnsureDefaults()
        {
            EnsureLoaded();
            if (!HasSetting("FontSize")) FontSize = Theme.RecommendedFontSize;
        }

        private static void RaiseChanged()
        {
            Action h = Changed;
            if (h == null) return;
            try { h(); }
            catch (Exception ex) { AppLog.Exception_("设置变更通知失败", ex); }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = AppPaths.SettingsFile;
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text[0] == ';' || text[0] == '#') continue;
                    int index = text.IndexOf('=');
                    if (index <= 0) continue;
                    string key = text.Substring(0, index).Trim();
                    string value = text.Substring(index + 1).Trim();
                    Map[key] = value;
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取设置失败", ex);
            }
        }

        /// <summary>保存设置（先写临时文件再替换，避免写入中断导致配置损坏）。</summary>
        public static void Save()
        {
            try
            {
                AppPaths.EnsureDataDir();
                var lines = new List<string>();
                lines.Add("; LTFC 设置文件（key=value；删除本文件可恢复默认）");
                lines.Add("; 保存时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                List<string> keys = new List<string>(Map.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string key in keys)
                {
                    lines.Add(key + "=" + Map[key]);
                }

                string path = AppPaths.SettingsFile;
                string temp = path + ".tmp";
                File.WriteAllLines(temp, lines.ToArray(), Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("保存设置失败", ex);
            }
        }
    }
}
