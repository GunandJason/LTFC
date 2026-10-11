using System;
using System.Collections.Generic;
using System.IO;

namespace OpenChoose
{
    /// <summary>
    /// ppt / pptx 等文件的“记住的选择”，保存在 %AppData%\OpenChoose\settings.ini。
    /// </summary>
    internal sealed class AppConfig
    {
        private const string FolderName = "OpenChoose";
        private const string FileName = "settings.ini";

        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private AppConfig()
        {
        }

        /// <summary>配置文件所在目录。</summary>
        public static string DirectoryPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, FolderName);
            }
        }

        /// <summary>配置文件完整路径。</summary>
        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, FileName); }
        }

        /// <summary>是否记住用户的选择（以后直接用该程序打开）。</summary>
        public bool RememberChoice
        {
            get { return this.GetBool("RememberChoice", false); }
            set { this.SetValue("RememberChoice", value ? "1" : "0"); }
        }

        /// <summary>加载配置（文件不存在时返回空配置）。</summary>
        public static AppConfig Load()
        {
            AppConfig config = new AppConfig();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        string text = line.Trim();
                        if (text.Length == 0 || text[0] == ';' || text[0] == '#')
                        {
                            continue;
                        }

                        int index = text.IndexOf('=');
                        if (index <= 0)
                        {
                            continue;
                        }

                        string key = text.Substring(0, index).Trim();
                        string value = text.Substring(index + 1).Trim();
                        config._values[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取配置失败: " + ex.Message);
            }

            return config;
        }

        /// <summary>保存配置。</summary>
        public void Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                List<string> lines = new List<string>();
                lines.Add("; OpenChoose / PPTChooser 配置文件");
                lines.Add("; 删除本文件即可恢复默认设置。");
                foreach (KeyValuePair<string, string> pair in this._values)
                {
                    lines.Add(pair.Key + "=" + pair.Value);
                }

                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception ex)
            {
                Log.Write("保存配置失败: " + ex.Message);
            }
        }

        /// <summary>读取字符串值。</summary>
        public string GetValue(string key, string defaultValue)
        {
            string value;
            if (this._values.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            return defaultValue;
        }

        /// <summary>写入字符串值。</summary>
        public void SetValue(string key, string value)
        {
            this._values[key] = value == null ? string.Empty : value;
        }

        /// <summary>按扩展名取得已记住的程序路径。</summary>
        public string GetSavedProgram(string extension)
        {
            return this.GetValue(GetProgramKey(extension), null);
        }

        /// <summary>按扩展名取得已记住的程序显示名。</summary>
        public string GetSavedProgramName(string extension)
        {
            return this.GetValue(GetNameKey(extension), null);
        }

        /// <summary>记住某个扩展名使用的程序。</summary>
        public void SetSavedProgram(string extension, string executablePath, string displayName)
        {
            this.SetValue(GetProgramKey(extension), executablePath);
            this.SetValue(GetNameKey(extension), displayName);
        }

        /// <summary>清除所有已记住的程序。</summary>
        public void ClearSavedPrograms()
        {
            List<string> keys = new List<string>();
            foreach (string key in this._values.Keys)
            {
                if (key.StartsWith("Open.", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Name.", StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(key);
                }
            }

            foreach (string key in keys)
            {
                this._values.Remove(key);
            }
        }

        private bool GetBool(string key, bool defaultValue)
        {
            string value = this.GetValue(key, null);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return value == "1" ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetProgramKey(string extension)
        {
            return "Open." + NormalizeExtension(extension);
        }

        private static string GetNameKey(string extension)
        {
            return "Name." + NormalizeExtension(extension);
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return "default";
            }

            return extension.TrimStart('.').ToLowerInvariant();
        }
    }
}
