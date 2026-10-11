using System;
using System.IO;

namespace OpenChoose
{
    /// <summary>
    /// 一个可用于打开 PPT 的程序（列表中的一行）。
    /// </summary>
    internal sealed class ProgramCandidate
    {
        public ProgramCandidate(string key, string displayName, string executablePath)
        {
            this.Key = key;
            this.DisplayName = displayName;
            this.ExecutablePath = executablePath;
        }

        /// <summary>内部标识（powerpoint / wps / libreoffice / other）。</summary>
        public string Key { get; private set; }

        /// <summary>显示名称，例如 “PowerPoint”。</summary>
        public string DisplayName { get; set; }

        /// <summary>可执行文件完整路径，null 表示未检测到。</summary>
        public string ExecutablePath { get; set; }

        /// <summary>是否为“选择其他程序...”这一项。</summary>
        public bool IsOther { get; set; }

        /// <summary>程序是否可用。</summary>
        public bool IsAvailable
        {
            get { return !string.IsNullOrEmpty(this.ExecutablePath) && File.Exists(this.ExecutablePath); }
        }

        /// <summary>列表中显示的文本。</summary>
        public string DisplayText
        {
            get
            {
                if (this.IsOther)
                {
                    return "选择其他程序...";
                }

                if (this.IsAvailable)
                {
                    return this.DisplayName;
                }

                return this.DisplayName + "（未检测到）";
            }
        }

        /// <summary>详细提示文本。</summary>
        public string ToolTipText
        {
            get
            {
                if (this.IsOther)
                {
                    return "浏览并选择其它可用于打开 PPT 的程序";
                }

                if (this.IsAvailable)
                {
                    return this.DisplayName + "\r\n" + this.ExecutablePath;
                }

                return "未在本机检测到 " + this.DisplayName + "，请使用“选择其他程序...”";
            }
        }
    }
}
