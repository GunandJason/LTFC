using System;
using System.Collections.Generic;

namespace LTFC
{
    internal enum ModuleKind
    {
        /// <summary>独立 EXE 子程序（独立进程、独立窗口）。</summary>
        ExternalProcess,

        /// <summary>主程序内的功能模块（不单独开进程）。</summary>
        Internal
    }

    /// <summary>模块在侧边栏里的一个子项（点击直接进入该模块的分页）。</summary>
    internal sealed class ModulePageDefinition
    {
        public ModulePageDefinition(string key, string title, string subtitle)
        {
            Key = key;
            Title = title;
            Subtitle = subtitle;
            ActivatesModule = false;
        }

        /// <summary>页面键后缀（完整键 = 模块 Id + "." + Key）。</summary>
        public string Key { get; private set; }

        public string Title { get; private set; }

        public string Subtitle { get; private set; }

        /// <summary>进入该分页时是否顺便显示模块窗口（例如 ClassTell 的“消息 / 过期”）。</summary>
        public bool ActivatesModule { get; set; }

        /// <summary>完整的页面键。</summary>
        public string FullKey(string moduleId) { return moduleId + "." + Key; }
    }

    /// <summary>一个功能模块的定义（侧边栏入口 + 启动方式 + 分页）。</summary>
    internal sealed class ModuleDefinition
    {
        public ModuleDefinition(string id, string displayName, ModuleKind kind, string fileName, string description)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            FileName = fileName;
            Description = description;
            Arguments = string.Empty;
            Pages = new List<ModulePageDefinition>();
            FallbackPaths = new string[0];
            AppliesTheme = true;
            SingleInstance = true;
        }

        /// <summary>内部标识（配置键、日志名使用）。</summary>
        public string Id { get; private set; }

        /// <summary>侧边栏显示名称。</summary>
        public string DisplayName { get; private set; }

        public ModuleKind Kind { get; private set; }

        /// <summary>可执行文件相对路径（相对 LTFC.exe 目录；可含子目录，例如 VideoDownload\xxx.exe）。</summary>
        public string FileName { get; private set; }

        public string Description { get; private set; }

        /// <summary>启动参数（可含占位符 {parent} = LTFC 进程 ID）。</summary>
        public string Arguments { get; set; }

        /// <summary>模块把自己的状态写到这个文件（可选，用于跨进程状态查询）。</summary>
        public string StatusFile { get; set; }

        /// <summary>打开该模块“设置界面”时使用的启动参数（为空表示该模块没有独立设置入口）。</summary>
        public string SettingsArguments { get; set; }

        /// <summary>该模块支持“用指定文件启动”（例如打开方式选择器）。</summary>
        public bool SupportsFileArgument { get; set; }

        /// <summary>侧边栏图标字形与兜底符号。</summary>
        public string Glyph { get; set; }

        public string GlyphFallback { get; set; }

        /// <summary>侧边栏子项（点击直接进入该模块的分页）。</summary>
        public IList<ModulePageDefinition> Pages { get; private set; }

        /// <summary>备用查找路径（开发环境 / 便携版目录；按顺序尝试）。</summary>
        public string[] FallbackPaths { get; set; }

        /// <summary>
        /// 是否套用 LTFC 主题。为 false 表示该程序使用自己的界面风格（例如视频下载），
        /// LTFC 不会对它做任何主题处理，只负责启动与显示窗口。
        /// </summary>
        public bool AppliesTheme { get; set; }

        /// <summary>单实例：重复点击只显示已有窗口，不再启动第二个进程。</summary>
        public bool SingleInstance { get; set; }

        /// <summary>模块日志文件名（不含扩展名）。</summary>
        public string LogName { get { return "module-" + Id; } }

        /// <summary>添加一个侧边栏子项。</summary>
        public ModulePageDefinition AddPage(string key, string title, string subtitle, bool activatesModule = false)
        {
            var page = new ModulePageDefinition(key, title, subtitle) { ActivatesModule = activatesModule };
            Pages.Add(page);
            return page;
        }
    }
}
