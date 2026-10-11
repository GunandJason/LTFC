using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LTFC.SelfTest
{
    /// <summary>
    /// LTFC 自测程序（不依赖第三方测试框架）。
    /// 用法：
    ///   LTFC.SelfTest.exe                   运行全部自测
    ///   LTFC.SelfTest.exe --snapshot 目录    生成界面快照（普通 / 高对比度）
    ///   LTFC.SelfTest.exe --sleep 3000      作为占位子程序运行（模块管理测试用）
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> Failures = new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppOptions.Parse(new[] { "--no-modules" });
            // 自测与界面快照需要稳定画面：关掉打开动效（动效行为由专门用例覆盖）
            Motion.Enabled = false;

            if (HasFlag(args, "--sleep"))
            {
                Thread.Sleep(GetIntArg(args, "--sleep", 2000));
                return 0;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine(" LTFC 自测程序（" + Theme.ProductName + " V" + Theme.VersionText + "）");
            Console.WriteLine("==================================================");
            Console.WriteLine("程序目录：" + AppContext.BaseDirectory);
            Console.WriteLine();

            string settingsBackup = BackupSettings();

            try
            {
                Console.WriteLine("--- 主题与高对比度 ---");
                Run("默认主题为浅色", TestThemeDefaults);
                Run("高对比度：粉(可交互区域底色) + 蓝(交互结果) 固定同时使用", TestHighContrastTheme);
                Run("高对比度：不跟随用户选择（内部强制固定双色）", TestHighContrastPaletteCorrection);
                Run("高对比度：两个颜色不是可选项（不可选、不可点）", TestHighContrastPaletteNotSelectable);
                Run("高对比度：粉 / 蓝两色实际画出", TestHighContrastPairRendering);
                Run("普通模式：强调色按设置生效", TestNormalPalette);
                Run("高对比度：可交互区域底色固定 #f5abb9", TestInteractiveColors);

                Console.WriteLine();
                Console.WriteLine("--- 模块定义与侧边栏顺序 ---");
                Run("侧边栏顺序 = ClassTell / OpenChoose / AutoNotice / 视频下载", TestModuleOrder);
                Run("侧边栏结构：ClassTell(消息/过期)，其余(设置)", TestNavigationPages);
                Run("外部子程序为独立进程模块", TestExternalModules);
                Run("模块定义含说明与图标", TestModuleMetadata);
                Run("视频下载：内置路径 + 不套用 LTFC 主题", TestVideoDownloadModule);
                Run("侧边栏文字为触摸友好的大字号", TestNavFontsForTouch);
                Run("侧边栏箭头：只展开分组，不切换页面", TestNavArrowClick);

                Console.WriteLine();
                Console.WriteLine("--- 定时提醒（AutoNotice · command.txt） ---");
                Run("command.txt 解析：有效行 / 注释 / 错误行", TestAutoNoticeParsing);
                Run("command.txt 解析：每天 / 中文星期 / 紧凑时间", TestAutoNoticeFriendlyFormats);
                Run("下次提醒时间计算", TestAutoNoticeNextDue);
                Run("配置文件默认指向 AutoNotice\\command.txt", TestAutoNoticeConfigPath);

                Console.WriteLine();
                Console.WriteLine("--- ClassTell 数据桥 ---");
                Run("消息 / 过期 数据文件解析", TestClassTellFeedParsing);

                Console.WriteLine();
                Console.WriteLine("--- 启动项 ---");
                Run("自启动命令行包含 --tray 与引号", TestStartupCommand);

                Console.WriteLine();
                Console.WriteLine("--- 设置读写 ---");
                Run("设置往返（写入后读回一致）", TestSettingsRoundTrip);

                Console.WriteLine();
                Console.WriteLine("--- 子程序进程管理 ---");
                Run("子程序：文件不存在时给出明确提示", TestModuleMissingFile);
                Run("子程序：启动失败可被识别", TestModuleStartFailure);
                Run("子程序：启动 / 停止（独立进程）", TestModuleStartStop);
                Run("子程序：重复点击只显示已有窗口（不重复启动）", TestModuleSingleInstance);

                Console.WriteLine();
                Console.WriteLine("--- 关于 / 赞赏码 ---");
                Run("赞赏码图片随程序发布且可显示", TestRewardQrImage);

                Console.WriteLine();
                Console.WriteLine("--- 启动动效 ---");
                Run("窗口打开动效：结束时回到终态且回调只执行一次", TestOpenAnimation);

                Console.WriteLine();
                Console.WriteLine("--- 界面 ---");
                Run("主窗口可创建（标题含 LTFC 与版本）", TestShellPages);

                int snapshotIndex = Array.IndexOf(args, "--snapshot");
                if (snapshotIndex >= 0)
                {
                    string dir = snapshotIndex + 1 < args.Length
                        ? args[snapshotIndex + 1]
                        : Path.Combine(Path.GetTempPath(), "ltfc-snapshot");
                    Console.WriteLine();
                    Console.WriteLine("--- 界面快照 ---");
                    Run("生成界面快照（普通模式）", delegate { Snapshot(dir, false); });
                    Run("生成界面快照（高对比度模式）", delegate { Snapshot(dir, true); });
                }
            }
            finally
            {
                RestoreSettings(settingsBackup);
            }

            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine("总计 " + (_passed + _failed) + " 项，通过 " + _passed + "，失败 " + _failed);
            if (_failed > 0)
            {
                Console.WriteLine("失败项：");
                foreach (string failure in Failures) Console.WriteLine("  × " + failure);
            }
            Console.WriteLine("==================================================");
            return _failed == 0 ? 0 : 1;
        }

        // ---------- 主题 / 高对比度 ----------

        private static void TestThemeDefaults()
        {
            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);
            Check(!Theme.Dark, "默认应为浅色模式");
            Check(!Theme.HighContrast, "默认不应是高对比度");
            Check(Theme.Accent == Theme.FindPalette(Theme.DefaultPaletteKey).Accent, "强调色应为默认配色");
        }

        /// <summary>
        /// 高对比度：固定浅色背景 + 固定的粉 / 蓝两色（粉 = 可交互区域底色，蓝 = 交互结果），
        /// 两者同时使用，不是让用户在两个颜色之间选择。
        /// </summary>
        private static void TestHighContrastTheme()
        {
            Theme.Apply(10f, true, Theme.DefaultPaletteKey, true);
            Check(!Theme.Dark, "高对比度模式下必须固定浅色（即使设置为深色）");
            Check(Theme.HighContrast, "高对比度开关应生效");
            Check(Theme.Accent == Theme.HighContrastA.Accent, "高对比度使用固定配色（蓝）");
            Check(Theme.Accent == Theme.Hex("#5bcffa"), "固定蓝应为 #5bcffa");
            Check(Theme.HighContrastB.Accent == Theme.Hex("#f5abb9"), "固定粉应为 #f5abb9");
            Check(Theme.Result == Theme.Hex(Theme.ResultHex), "交互结果色 = 蓝 #5bcffa");
            Check(Theme.Interactive == Theme.Hex(Theme.InteractiveHex), "可交互区域底色 = 粉 #f5abb9");
            Check(Theme.Result != Theme.Interactive, "粉与蓝应同时存在（不是二选一）");
            Check(Theme.MarkA == Theme.Result && Theme.MarkB == Theme.Interactive, "两个固定色：A=蓝(结果) / B=粉(交互)");
            Check(Theme.BorderWidth > 1f, "高对比度下边框应更明显");
            Check(Theme.Text == Color.Black, "高对比度下正文应为纯黑");
            Check(Theme.Window.GetBrightness() > 0.9f, "高对比度下背景应为浅色");
        }

        /// <summary>
        /// 高对比度下不存在“配色选择”：无论设置里保存的是什么键，都固定同时使用
        /// 粉（可交互区域底色）+ 蓝（交互结果）；离开高对比度后固定键回落默认配色。
        /// </summary>
        private static void TestHighContrastPaletteCorrection()
        {
            Color pink = Theme.Hex(Theme.InteractiveHex);
            Color blue = Theme.Hex(Theme.ResultHex);

            Theme.Apply(10f, false, "teal", true);
            Check(Theme.Accent == Theme.HighContrastA.Accent, "高对比度下非固定配色应被纠正为固定配色");
            Check(Theme.Result == blue, "高对比度下结果色固定为蓝（传 teal）");
            Check(Theme.Interactive == pink, "高对比度下可交互区域底色固定为粉（传 teal）");

            Theme.Apply(10f, false, Theme.HighContrastPaletteKeyB, true);
            Check(Theme.Result == blue && Theme.Interactive == pink,
                "高对比度下即使保存过“粉”也要同时使用粉 + 蓝（不再二选一）");

            Theme.Apply(10f, false, Theme.HighContrastPaletteKeyB, false);
            Check(Theme.Accent == Theme.FindPalette(Theme.DefaultPaletteKey).Accent,
                "离开高对比度后遗留的固定键应回落为默认配色，而不是把粉色当强调色");
        }

        /// <summary>
        /// 高对比度的两个颜色不能出现在“可选强调色”里，也不能做成可点击的色块：
        /// 用户不需要、也不应该去选高对比度的配色。
        /// </summary>
        private static void TestHighContrastPaletteNotSelectable()
        {
            Check(Theme.IsHighContrastKey(Theme.HighContrastPaletteKey) && Theme.IsHighContrastKey(Theme.HighContrastPaletteKeyB),
                "两个固定色应被标记为高对比度专用键");
            Check(!Theme.IsHighContrastKey(Theme.DefaultPaletteKey), "普通配色不应被标记为高对比度专用");

            foreach (ThemePalette palette in Theme.SelectablePalettes)
            {
                Check(!Theme.IsHighContrastKey(palette.Key), "可选强调色列表里不应出现高对比度专用色：" + palette.Key);
            }

            foreach (string key in new[] { Theme.HighContrastPaletteKeyB, Theme.HighContrastPaletteKey })
            {
                using (var swatch = new UiSwatch { Palette = Theme.FindPalette(key), Selectable = false })
                {
                    int clicks = 0;
                    swatch.Click += delegate { clicks++; };
                    Check(!swatch.PerformClick(), "说明用的色块应不可点击：" + key);
                    Check(clicks == 0, "说明用的色块不应触发点击事件：" + key);
                    Check(swatch.Cursor == Cursors.Default, "说明用的色块不应显示手型光标：" + key);
                }
            }

            using (var swatch = new UiSwatch { Palette = Theme.FindPalette("lilac") })
            {
                int clicks = 0;
                swatch.Click += delegate { clicks++; };
                Check(swatch.PerformClick() && clicks == 1, "普通模式下的强调色色块仍应可点击选择");
            }

            Check(Theme.HighContrastPairHint.Contains("#f5abb9") && Theme.HighContrastPairHint.Contains("#5bcffa"),
                "高对比度说明文字应同时写明两个颜色");
            Check(Theme.HighContrastPairHint.Contains("不需要选择"),
                "高对比度说明文字应写明两个颜色同时使用、不需要选择");
        }

        /// <summary>粉 / 蓝两个颜色在界面上真的画得出来（不是只改了常量）。</summary>
        private static void TestHighContrastPairRendering()
        {
            Theme.Apply(10f, true, Theme.DefaultPaletteKey, true);
            Check(PixelAtSwatch(Theme.HighContrastPaletteKeyB) == Theme.Hex(Theme.InteractiveHex).ToArgb(),
                "高对比度下的粉色色块应画出 #f5abb9（可交互区域底色）");
            Check(PixelAtSwatch(Theme.HighContrastPaletteKey) == Theme.Hex(Theme.ResultHex).ToArgb(),
                "高对比度下的蓝色色块应画出 #5bcffa（交互结果）");
            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);
        }

        /// <summary>渲染一个“说明用”的色块，取它中心的一个像素。</summary>
        private static int PixelAtSwatch(string paletteKey)
        {
            using (var swatch = new UiSwatch { Palette = Theme.FindPalette(paletteKey), Selectable = false })
            {
                using (var bitmap = new Bitmap(Math.Max(1, swatch.Width), Math.Max(1, swatch.Height)))
                {
                    swatch.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).ToArgb();
                }
            }
        }

        private static void TestNormalPalette()
        {
            Theme.Apply(10f, false, "lilac", false);
            Check(Theme.Accent == Theme.FindPalette("lilac").Accent, "普通模式应使用所选的强调色");
            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);
        }

        /// <summary>
        /// 可互动控件（按钮 / 开关）的配色：
        /// 高对比度固定为 #f5abb9（并按需派生悬停 / 按下 / 文字色），普通模式仍跟随主题强调色。
        /// </summary>
        private static void TestInteractiveColors()
        {
            Color expected = Theme.Hex(Theme.InteractiveHex);
            Check(expected == Theme.Hex("#f5abb9"), "可互动色常量应为 #f5abb9");

            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);
            Check(Theme.Interactive == Theme.Accent, "普通模式下可互动色应等于主题强调色");
            Check(Theme.Interactive == Theme.InteractiveHover || Theme.InteractiveHover != Theme.Interactive,
                "普通模式下也应有悬停 / 按下反馈色");
            Check(Theme.InteractiveOn.GetBrightness() < 0.5f || Theme.Interactive.GetBrightness() < 0.5f,
                "填充色上必须有可读的文字色");

            Theme.Apply(10f, false, Theme.HighContrastPaletteKey, true);
            Check(Theme.Interactive == expected, "高对比度下可互动色应固定为 #f5abb9：" + Theme.Interactive.Name);
            Check(Theme.InteractiveHover != Theme.Interactive, "高对比度下悬停色应不同于常态色");
            Check(Theme.InteractivePressed != Theme.Interactive, "高对比度下按下色应不同于常态色");

            // 控件实际画出来的颜色必须是这个固定色（不是只改了常量）
            Check(PixelAtCheckedSwitch() == expected.ToArgb(),
                "高对比度下已打开的开关应画出 #f5abb9 轨道");

            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);
            Check(PixelAtCheckedSwitch() == Theme.Accent.ToArgb(), "普通模式下开关应使用主题强调色");
        }

        /// <summary>渲染一个“已打开”的开关，取它左侧轨道里的一个像素（避开滑块）。</summary>
        private static int PixelAtCheckedSwitch()
        {
            using (var toggle = new UiSwitch { Checked = true })
            {
                using (var bitmap = new Bitmap(Math.Max(1, toggle.Width), Math.Max(1, toggle.Height)))
                {
                    toggle.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    return bitmap.GetPixel(Theme.S(6), bitmap.Height / 2).ToArgb();
                }
            }
        }

        // ---------- 模块定义 ----------

        private static void TestModuleOrder()
        {
            var ids = new List<string>();
            foreach (ModuleDefinition definition in Modules.Definitions) ids.Add(definition.Id);
            string actual = string.Join(",", ids.ToArray());
            Check(actual == "classtell,openchoose,autonotice,videodownload",
                "侧边栏顺序应为 classtell,openchoose,autonotice,videodownload，实际：" + actual);
            Check(Modules.Definitions.Count == 4, "应恰好有 4 个模块，实际：" + Modules.Definitions.Count);
        }

        /// <summary>侧边栏结构：ClassTell → 消息 / 过期；其他模块 → 设置；视频下载没有子项。</summary>
        private static void TestNavigationPages()
        {
            ModuleDefinition classTell = Modules.Find(Modules.ClassTellId);
            Check(classTell.Pages.Count == 2, "ClassTell 应有 2 个分页，实际：" + classTell.Pages.Count);
            Check(classTell.Pages[0].Key == Modules.PageMessages, "ClassTell 第一个分页应为消息");
            Check(classTell.Pages[1].Key == Modules.PageExpired, "ClassTell 第二个分页应为过期");
            Check(classTell.Pages[0].FullKey(Modules.ClassTellId) == "classtell.messages", "分页键应为 模块Id.后缀");

            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                if (definition.Id == Modules.ClassTellId || definition.Id == Modules.VideoDownloadId) continue;
                Check(definition.Pages.Count == 1 && definition.Pages[0].Key == Modules.PageSettings,
                    definition.DisplayName + " 应有 1 个“设置”分页");
            }

            Check(Modules.Find(Modules.VideoDownloadId).Pages.Count == 0, "视频下载在侧边栏里不应有子项");

            using (var nav = new SideNav())
            {
                nav.AddTitle("LTFC", "home");
                nav.AddGroup(classTell.DisplayName, classTell.Id);
                nav.AddChild(classTell.Id, "消息", "classtell.messages");
                nav.AddChild(classTell.Id, "过期", "classtell.expired");
                nav.AddSettings("总设置", "settings");

                Check(!nav.IsGroupExpanded(Modules.ClassTellId), "分组默认应为收起状态");
                nav.ExpandGroup(Modules.ClassTellId, true);
                Check(nav.IsGroupExpanded(Modules.ClassTellId), "展开后应为展开状态");
                nav.SelectByKey("classtell.expired", false);
                Check(nav.IsGroupExpanded(Modules.ClassTellId), "选中子项时应自动展开所属分组");

                nav.SetFooter("V" + Theme.VersionText, "自测");
                nav.LayoutEntries();
                nav.SelectByKey("home", false);
            }
        }

        private static void TestExternalModules()
        {
            Check(Modules.Find(Modules.ClassTellId).Kind == ModuleKind.ExternalProcess, "ClassTell 应为独立进程模块（独立窗口）");
            Check(Modules.Find(Modules.OpenChooseId).Kind == ModuleKind.ExternalProcess, "OpenChoose 应为独立进程模块");
            Check(Modules.Find(Modules.VideoDownloadId).Kind == ModuleKind.ExternalProcess, "视频下载应为独立进程模块");
            Check(Modules.Find(Modules.AutoNoticeId).Kind == ModuleKind.Internal, "AutoNotice 应为内置模块（窗口由主程序提供）");
        }

        private static void TestModuleMetadata()
        {
            foreach (ModuleDefinition definition in Modules.Definitions)
            {
                Check(!string.IsNullOrEmpty(definition.DisplayName), "模块应有显示名：" + definition.Id);
                Check(!string.IsNullOrEmpty(definition.Description), "模块应有说明：" + definition.Id);
                if (definition.Kind == ModuleKind.ExternalProcess)
                {
                    Check(definition.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "外部模块应为 .exe：" + definition.Id);
                    Check(definition.SingleInstance, "外部模块默认应为单实例（重复点击只显示窗口）：" + definition.Id);
                }
            }
        }

        private static void TestVideoDownloadModule()
        {
            ModuleDefinition video = Modules.Find(Modules.VideoDownloadId);
            Check(video != null, "应存在视频下载模块");
            Check(!video.AppliesTheme, "视频下载不应套用 LTFC 主题");
            Check(video.FileName.IndexOf("VideoDownload", StringComparison.OrdinalIgnoreCase) >= 0,
                "视频下载应内置在 VideoDownload 目录：" + video.FileName);
            Check(video.FileName.EndsWith("4KVideoDownloaderPlusPortable.exe", StringComparison.OrdinalIgnoreCase),
                "视频下载应指向便携版程序：" + video.FileName);
            Check(!string.IsNullOrEmpty(Modules.ResolveExecutablePath(video)), "视频下载应能解析出程序路径");
        }

        private static void TestNavFontsForTouch()
        {
            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);
            Check(Theme.NavGroupFont.Size > Theme.Body.Size, "分组字体应大于正文字体（触摸友好）");
            Check(Theme.NavChildFont.Size > Theme.Body.Size, "子项字体应大于正文字体（触摸友好）");
            Check(Theme.NavTitleFont.Size > Theme.NavGroupFont.Size, "标题字体应为最大");
            Check(Theme.NavGroupHeight > Theme.S(42), "分组行高应比旧版更高：实际 " + Theme.NavGroupHeight);
            Check(Theme.NavChildHeight > Theme.S(38), "子项行高应比旧版更高：实际 " + Theme.NavChildHeight);
        }

        /// <summary>
        /// 侧边栏折叠箭头：点箭头只展开 / 收起分组，绝不触发页面切换（也就不会带出切页的耗时逻辑）；
        /// 点条目文字仍然正常切换页面。
        /// </summary>
        private static void TestNavArrowClick()
        {
            Theme.Apply(10f, false, Theme.DefaultPaletteKey, false);

            using (var group = new NavButton { IsExpandable = true, Width = Theme.NavWidth, Height = Theme.NavGroupHeight })
            {
                int clicks = 0;
                int arrows = 0;
                group.Click += (s, e) => clicks++;
                group.ArrowClick += (s, e) => arrows++;

                Rectangle arrow = group.ArrowBounds;
                Check(!arrow.IsEmpty, "可展开分组应有箭头命中区");
                Check(group.HitArrow(new Point(arrow.Right - 2, arrow.Top + arrow.Height / 2)), "箭头区域内应命中箭头");
                Check(!group.HitArrow(new Point(Math.Max(0, arrow.Left - Theme.S(20)), arrow.Top + arrow.Height / 2)),
                    "箭头左侧的文字区域不应命中箭头");

                InvokeMouseUp(group, new Point(arrow.Left + arrow.Width / 2, arrow.Top + arrow.Height / 2));
                Check(arrows == 1, "点箭头应触发展开 / 收起");
                Check(clicks == 0, "点箭头不应触发条目点击（因此不会切换页面）");

                InvokeMouseUp(group, new Point(Theme.S(12), arrow.Top + arrow.Height / 2));
                Check(clicks == 1, "点条目文字区域应正常切换页面");
            }

            using (var plain = new NavButton { Width = Theme.NavWidth, Height = Theme.NavGroupHeight })
            {
                Check(plain.ArrowBounds.IsEmpty, "没有子项的分组不应显示箭头");
                Check(!plain.HitArrow(new Point(plain.Width - Theme.S(10), Theme.S(10))), "没有箭头时命中判断应为否");
            }

            using (var nav = new SideNav())
            {
                int switches = 0;
                nav.SelectionChanged += key => switches++;
                nav.AddGroup("模块", "m");
                nav.AddChild("m", "设置", "m.settings");
                nav.ExpandGroup("m", true);

                nav.ToggleGroupOnly("m");
                Check(!nav.IsGroupExpanded("m"), "箭头收起分组应生效");
                Check(switches == 0, "箭头收起分组不应触发页面切换");
            }
        }

        /// <summary>用反射模拟一次鼠标左键抬起（不依赖真实鼠标）。</summary>
        private static void InvokeMouseUp(Control control, Point point)
        {
            System.Reflection.MethodInfo method = control.GetType().GetMethod("OnMouseUp",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check(method != null, "应能找到鼠标抬起处理方法");
            if (method != null)
            {
                method.Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
            }
        }

        /// <summary>
        /// 打开动效：关闭时立即给终态；开启时经过一段时间后一定回到终态（透明度 1 / 原位），回调只执行一次。
        /// 因此“动效不会把界面卡在中间状态”是可验证的。
        /// </summary>
        private static void TestOpenAnimation()
        {
            bool enabled = Motion.Enabled;
            try
            {
                using (var form = new Form { StartPosition = FormStartPosition.Manual, ShowInTaskbar = false })
                {
                    form.Location = new Point(-4000, -4000);
                    form.Show();

                    int top = form.Top;

                    Motion.Enabled = false;
                    int done = 0;
                    Motion.PlayOpen(form, delegate { done++; });
                    Check(done == 1, "关闭动效时应立即完成（回调仍会执行一次）");
                    Check(form.Opacity == 1d && form.Top == top, "关闭动效时不应改动透明度 / 位置");

                    Motion.Enabled = true;
                    done = 0;
                    Motion.PlayOpen(form, delegate { done++; });

                    if (Motion.SystemReducedMotion)
                    {
                        Check(done == 1 && form.Opacity == 1d && form.Top == top, "系统禁用动画时应直接给终态");
                    }
                    else
                    {
                        long deadline = Environment.TickCount + 4000;
                        while (done == 0 && Environment.TickCount < deadline)
                        {
                            Application.DoEvents();
                            Thread.Sleep(10);
                        }

                        Check(done == 1, "打开动效应在动效时长内结束");
                        Check(Math.Abs(form.Opacity - 1d) < 0.001d, "动效结束后透明度应恢复为 1");
                        Check(form.Top == top, "动效结束后窗口应回到最终位置");

                        Application.DoEvents();
                        Thread.Sleep(60);
                        Application.DoEvents();
                        Check(done == 1, "动效回调不应被重复执行");
                    }

                    form.Hide();
                }
            }
            finally
            {
                Motion.Enabled = enabled;
            }
        }

        // ---------- 定时提醒（AutoNotice · command.txt） ----------

        private static void TestAutoNoticeParsing()
        {
            string path = Path.Combine(Path.GetTempPath(), "ltfc-selftest-command.txt");
            File.WriteAllLines(path, new[]
            {
                "# 注释行",
                "; 另一种注释",
                string.Empty,
                "1 08:00 早读开始",
                "1 12:00 午休 时间",
                "这一行是错的",
                "9 08:00 星期超范围",
                "2 25:00 时间超范围"
            }, new System.Text.UTF8Encoding(true));

            AutoNoticeConfig config = AutoNoticeConfig.Load(path);
            Check(config.Exists, "应读取到配置文件");
            Check(config.Entries.Count == 2, "应解析出 2 条有效提醒，实际：" + config.Entries.Count);
            Check(config.Errors.Count == 3, "应记录 3 条错误，实际：" + config.Errors.Count);
            Check(config.Entries[0].Day == DayOfWeek.Monday, "1 应解析为周一");
            Check(config.Entries[0].Hour == 8 && config.Entries[0].Minute == 0, "08:00 应解析为 8 时 0 分");
            Check(config.Entries[1].Text == "午休 时间", "提醒内容应保留空格：" + config.Entries[1].Text);
            Check(config.Entries[0].TimeText == "08:00", "时间应格式化为 08:00");
            Check(config.Entries[0].DayText == "周一", "星期应显示为中文");

            File.Delete(path);
        }

        private static void TestAutoNoticeFriendlyFormats()
        {
            var config = new AutoNoticeConfig();
            AutoNoticeConfig.ParseLine(config, "* 12:30 午休", 1);
            AutoNoticeConfig.ParseLine(config, "周日 9:05 值日", 2);
            AutoNoticeConfig.ParseLine(config, "Mon 1200 英语课", 3);
            AutoNoticeConfig.ParseLine(config, "7 23:59 最后一条", 4);

            Check(config.Entries.Count == 4, "应解析出 4 条，实际：" + config.Entries.Count);
            Check(config.Entries[0].Day == null, "* 应表示每天");
            Check(config.Entries[0].DayText == "每天", "每天的显示文字应为“每天”");
            Check(config.Entries[1].Day == DayOfWeek.Sunday, "“周日”应解析为星期日");
            Check(config.Entries[1].Minute == 5, "9:05 应解析出 5 分");
            Check(config.Entries[2].Day == DayOfWeek.Monday && config.Entries[2].Hour == 12, "Mon 1200 应解析为周一 12:00");
            Check(config.Entries[3].Day == DayOfWeek.Sunday, "7 应表示星期日");
            Check(config.Errors.Count == 0, "这些写法都不应报错：" + string.Join(" / ", new List<string>(config.Errors).ToArray()));
        }

        private static void TestAutoNoticeNextDue()
        {
            // 2026-10-01 是星期四
            var now = new DateTime(2026, 10, 1, 7, 30, 0);

            var config = new AutoNoticeConfig();
            AutoNoticeConfig.ParseLine(config, "1 08:00 早读", 1);
            DateTime? next = config.NextDue(now);
            Check(next.HasValue, "应能算出下一次提醒");
            Check(next.Value.DayOfWeek == DayOfWeek.Monday, "下一次应为周一，实际：" + next.Value.DayOfWeek);
            Check(next.Value.Hour == 8 && next.Value.Minute == 0, "下一次应为 08:00");
            Check(next.Value > now, "下一次提醒必须在未来");

            var todayConfig = new AutoNoticeConfig();
            AutoNoticeConfig.ParseLine(todayConfig, "4 09:00 今天稍后", 1);
            DateTime? today = todayConfig.NextDue(now);
            Check(today.HasValue && today.Value.Date == now.Date, "当天稍后的提醒应算在当天");
        }

        private static void TestAutoNoticeConfigPath()
        {
            string path = AppPaths.AutoNoticeConfigFile;
            Check(path.EndsWith("command.txt", StringComparison.OrdinalIgnoreCase), "配置文件必须叫 command.txt：" + path);
            Check(path.IndexOf("AutoNotice", StringComparison.OrdinalIgnoreCase) >= 0, "配置文件应位于 AutoNotice 目录：" + path);
            Check(File.Exists(path), "开发环境应直接使用仓库里的 src\\AutoNotice\\command.txt：" + path);

            AutoNoticeConfig config = AutoNoticeConfig.Load(path);
            Check(config.Entries.Count > 0, "仓库里的 command.txt 至少应有一条提醒");
        }

        // ---------- ClassTell 数据桥 ----------

        private static void TestClassTellFeedParsing()
        {
            string path = ClassTellBridge.FeedFile;
            string backup = File.Exists(path) ? File.ReadAllText(path) : null;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(path,
                    "updated=2026-10-01 12:30:00" + Environment.NewLine +
                    "account=test@example.com" + Environment.NewLine +
                    "[messages]" + Environment.NewLine +
                    "2026-10-01 12:30:00\t呼叫\tC 类邮件 1" + Environment.NewLine +
                    "2026-10-01 12:31:00\t呼叫\tC 类邮件 2" + Environment.NewLine +
                    "[expired]" + Environment.NewLine +
                    "2026-09-30 08:05:00\t未读\t旧邮件" + Environment.NewLine,
                    new System.Text.UTF8Encoding(true));

                ClassTellFeed feed = ClassTellBridge.Read();
                Check(feed.Exists, "应读到数据文件");
                Check(feed.Messages.Count == 2, "消息应为 2 条，实际：" + feed.Messages.Count);
                Check(feed.Expired.Count == 1, "过期应为 1 条，实际：" + feed.Expired.Count);
                Check(feed.Updated == "2026-10-01 12:30:00", "应解析 updated");
                Check(feed.Messages[0].Text == "C 类邮件 1", "应解析消息内容：" + feed.Messages[0].Text);
                Check(ClassTellBridge.Describe(feed.Messages, "空").IndexOf("C 类邮件 2", StringComparison.Ordinal) >= 0,
                    "Describe 应包含全部消息");
            }
            finally
            {
                if (backup == null) File.Delete(path);
                else File.WriteAllText(path, backup);
            }
        }

        private static void TestStartupCommand()
        {
            string command = Startup.DesiredCommand;
            Check(command.IndexOf("--tray", StringComparison.Ordinal) > 0, "自启动命令行应带 --tray：" + command);
            Check(command.StartsWith("\"", StringComparison.Ordinal), "自启动命令行路径应带引号：" + command);
            Check(command.IndexOf("LTFC.exe", StringComparison.OrdinalIgnoreCase) > 0, "自启动命令行应指向 LTFC.exe");
        }

        private static void TestSettingsRoundTrip()
        {
            bool original = Settings.HighContrast;
            Settings.HighContrast = !original;
            Check(Settings.HighContrast == !original, "设置写入后应立即读回新值");
            Check(Settings.GetBool("HighContrast", !original) == !original, "GetBool 应返回写入值");
            Settings.HighContrast = original;
            Check(Settings.HighContrast == original, "设置可恢复");
        }

        // ---------- 子程序进程管理 ----------

        private static void TestModuleMissingFile()
        {
            const string id = "selftest-missing";
            var definition = new ModuleDefinition(id, "缺失程序", ModuleKind.ExternalProcess, "definitely-not-exists-1234.exe", "自测用");
            using (var process = new ModuleProcess(definition))
            {
                string message;
                bool ok = process.Start(out message);
                Check(!ok, "文件不存在时启动应失败");
                Check(message.IndexOf("找不到", StringComparison.Ordinal) >= 0, "失败提示应说明找不到文件：" + message);
            }
        }

        private static void TestModuleStartFailure()
        {
            const string id = "selftest-fastfail";
            Settings.SetModulePath(id, Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            var definition = new ModuleDefinition(id, "立即退出", ModuleKind.ExternalProcess, "cmd.exe", "自测用");
            definition.Arguments = "/c exit 3";
            using (var process = new ModuleProcess(definition))
            {
                string message;
                bool ok = process.Start(out message);
                Check(!ok, "启动后立即退出的程序应判定为启动失败");
                Check(message.IndexOf("退出", StringComparison.Ordinal) >= 0, "失败提示应提到退出：" + message);
            }
            Settings.SetModulePath(id, string.Empty);
        }

        private static void TestModuleStartStop()
        {
            const string id = "selftest-sleep";
            string self = Path.Combine(AppContext.BaseDirectory, "LTFC.SelfTest.exe");
            Check(File.Exists(self), "应能找到自测程序自身作为占位子程序");
            Settings.SetModulePath(id, self);

            var definition = new ModuleDefinition(id, "占位子程序", ModuleKind.ExternalProcess, "LTFC.SelfTest.exe", "自测用");
            definition.Arguments = "--sleep 4000";
            using (var process = new ModuleProcess(definition))
            {
                string message;
                bool started = process.Start(out message);
                Check(started, "占位子程序应能启动：" + message);
                Check(process.IsRunning, "启动后应处于运行状态");

                bool stopped = process.Stop(out message);
                Check(stopped, "占位子程序应能停止：" + message);
                Check(!process.IsRunning, "停止后不应处于运行状态");
                Check(process.State == ModuleState.Stopped, "停止后状态应为 Stopped，实际：" + process.State);
            }

            Settings.SetModulePath(id, string.Empty);
        }

        /// <summary>重复点击“启动”时只显示已有窗口，不会开出第二个进程。</summary>
        private static void TestModuleSingleInstance()
        {
            const string id = "selftest-single";
            string self = Path.Combine(AppContext.BaseDirectory, "LTFC.SelfTest.exe");
            Settings.SetModulePath(id, self);

            var definition = new ModuleDefinition(id, "单实例占位", ModuleKind.ExternalProcess, "LTFC.SelfTest.exe", "自测用");
            definition.Arguments = "--sleep 4000";

            using (var process = new ModuleProcess(definition))
            {
                string message;
                Check(process.Start(out message), "第一次应能启动：" + message);
                int before = CountSelfTestProcesses();

                bool second = process.Start(out message);
                Check(second, "重复启动应返回“已在运行”而不是失败：" + message);
                Check(message.IndexOf("已在运行", StringComparison.Ordinal) >= 0, "提示应说明已在运行：" + message);
                Check(process.HasExternalInstance(), "应能发现已经在运行的实例");
                Check(process.IsRunning, "重复点击后应仍然是同一个进程（没有被重启）");

                int after = CountSelfTestProcesses();
                Check(after <= before, "重复点击不应产生新的进程：之前 " + before + " 个，之后 " + after + " 个");

                process.Stop(out message);
                Check(!process.IsRunning, "停止后不应处于运行状态");
            }

            Settings.SetModulePath(id, string.Empty);
        }

        /// <summary>系统里同名的自测进程数量（用于确认没有重复启动）。</summary>
        private static int CountSelfTestProcesses()
        {
            int count = 0;
            try
            {
                foreach (System.Diagnostics.Process running in System.Diagnostics.Process.GetProcessesByName("LTFC.SelfTest"))
                {
                    count++;
                }
            }
            catch (Exception)
            {
            }
            return count;
        }

        private static void TestShellPages()
        {
            using (var shell = new ShellForm())
            {
                Check(shell.Text.IndexOf("LTFC", StringComparison.OrdinalIgnoreCase) >= 0, "窗口标题应包含 LTFC：" + shell.Text);
                Check(shell.Text.IndexOf(Theme.VersionText, StringComparison.Ordinal) >= 0, "窗口标题应包含版本号：" + shell.Text);
            }
        }

        /// <summary>赞赏码：随程序发布，并能在界面上真正显示出来（不是空占位）。</summary>
        private static void TestRewardQrImage()
        {
            Check(File.Exists(AppPaths.RewardQrFile), "赞赏码图片应随程序发布：" + AppPaths.RewardQrFile);

            // 真实图片与“图片缺失占位”渲染出来的画面必须明显不同，才能证明图片真的画出来了
            int realDiff = RenderDiff(AppPaths.RewardQrFile);
            Check(realDiff > 0, "赞赏码应画出图片内容（与占位显示不同的像素点 " + realDiff + " 个）");
        }

        /// <summary>把一张图渲染到控件上，返回它与“图片缺失占位”的不同像素点数。</summary>
        private static int RenderDiff(string imagePath)
        {
            using (var bitmap = Render(imagePath))
            using (var placeholder = Render(Path.Combine(Path.GetTempPath(), "definitely-missing-1234.jpg")))
            {
                int diff = 0;
                for (int x = 0; x < bitmap.Width; x += 3)
                {
                    for (int y = 0; y < bitmap.Height; y += 3)
                    {
                        Color a = bitmap.GetPixel(x, y);
                        Color b = placeholder.GetPixel(x, y);
                        if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 24) diff++;
                    }
                }
                return diff;
            }
        }

        private static Bitmap Render(string imagePath)
        {
            using (var image = new UiImage
            {
                ImagePath = imagePath,
                Caption = "扫码赞赏",
                EmptyText = "图片缺失"
            })
            {
                image.Size = new Size(Theme.S(240), Theme.S(240));
                var bitmap = new Bitmap(image.Width, image.Height);
                image.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                return bitmap;
            }
        }

        // ---------- 界面快照 ----------

        private static void Snapshot(string dir, bool highContrast)
        {
            Directory.CreateDirectory(dir);
            bool dark = Settings.DarkMode;
            string accent = Settings.AccentKey;
            float fontSize = Settings.FontSize;

            try
            {
                Settings.DarkMode = false;
                Settings.HighContrast = highContrast;
                Settings.AccentKey = highContrast ? Theme.HighContrastPaletteKey : Theme.DefaultPaletteKey;
                Theme.Apply(fontSize, false, Settings.AccentKey, highContrast);

                using (var shell = new ShellForm())
                {
                    shell.StartPosition = FormStartPosition.Manual;
                    shell.Location = new Point(-6000, -6000);
                    shell.Show();
                    Application.DoEvents();

                    string prefix = highContrast ? "hc-" : "normal-";
                    string[] pages =
                    {
                        "home",
                        Modules.ClassTellId,
                        Modules.ClassTellId + "." + Modules.PageMessages,
                        Modules.ClassTellId + "." + Modules.PageExpired,
                        Modules.OpenChooseId,
                        Modules.OpenChooseId + "." + Modules.PageSettings,
                        Modules.AutoNoticeId,
                        Modules.AutoNoticeId + "." + Modules.PageSettings,
                        Modules.VideoDownloadId,
                        "settings"
                    };
                    foreach (string page in pages)
                    {
                        InvokeSwitch(shell, page);
                        Application.DoEvents();
                        Thread.Sleep(150);
                        Application.DoEvents();

                        string path = Path.Combine(dir, prefix + page + ".png");
                        using (var bitmap = new Bitmap(Math.Max(100, shell.ClientSize.Width), Math.Max(100, shell.ClientSize.Height)))
                        {
                            shell.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(path, ImageFormat.Png);
                        }
                        Check(File.Exists(path) && new FileInfo(path).Length > 2000, "快照应写入且非空：" + Path.GetFileName(path));
                    }

                    // 页尾快照：验证“关于（赞赏码）”等需要滚动才能看到的内容
                    string[] bottomPages = { "settings", Modules.AutoNoticeId + "." + Modules.PageSettings };
                    foreach (string page in bottomPages)
                    {
                        InvokeSwitch(shell, page);
                        Application.DoEvents();
                        shell.ScrollCurrentToBottom();
                        Application.DoEvents();
                        Thread.Sleep(120);
                        Application.DoEvents();

                        string path = Path.Combine(dir, prefix + page + "-bottom.png");
                        using (var bitmap = new Bitmap(Math.Max(100, shell.ClientSize.Width), Math.Max(100, shell.ClientSize.Height)))
                        {
                            shell.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(path, ImageFormat.Png);
                        }
                        Check(File.Exists(path) && new FileInfo(path).Length > 2000, "页尾快照应写入且非空：" + Path.GetFileName(path));
                    }

                    shell.Hide();
                }
            }
            finally
            {
                Settings.DarkMode = dark;
                Settings.HighContrast = false;
                Settings.AccentKey = accent;
                Theme.Apply(fontSize, dark, accent, false);
            }
        }

        private static void InvokeSwitch(ShellForm shell, string pageKey)
        {
            System.Reflection.MethodInfo method = typeof(ShellForm).GetMethod("SwitchPage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check(method != null, "应能找到页面切换方法");
            if (method != null) method.Invoke(shell, new object[] { pageKey });
        }

        // ---------- 设置备份 / 恢复 ----------

        private static string BackupSettings()
        {
            try
            {
                string path = AppPaths.SettingsFile;
                if (!File.Exists(path)) return null;
                string backup = path + ".selftest-backup";
                File.Copy(path, backup, true);
                return backup;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void RestoreSettings(string backup)
        {
            try
            {
                if (string.IsNullOrEmpty(backup) || !File.Exists(backup)) return;
                File.Copy(backup, AppPaths.SettingsFile, true);
                File.Delete(backup);
            }
            catch (Exception)
            {
            }
        }

        // ---------- 测试框架 ----------

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Check(true, name);
            }
            catch (Exception ex)
            {
                _failed++;
                Failures.Add(name + " → " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine("  [失败] " + name + " → " + ex.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  [通过] " + message);
            }
            else
            {
                _failed++;
                Failures.Add(message);
                Console.WriteLine("  [失败] " + message);
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int GetIntArg(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(args[i + 1], out value)) return value;
                }
            }
            return fallback;
        }
    }
}
