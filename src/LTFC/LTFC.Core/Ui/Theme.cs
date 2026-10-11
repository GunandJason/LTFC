using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    internal enum ThemeMode
    {
        Dark,
        Light
    }

    /// <summary>一套强调色配色方案。</summary>
    internal sealed class ThemePalette
    {
        public ThemePalette(string key, string name, string hex, string textDarkHex)
        {
            Key = key;
            Name = name;
            Hex = hex;
            Accent = ColorTranslator.FromHtml(hex);
            AccentTextOnLight = ColorTranslator.FromHtml(textDarkHex);
        }

        public string Key { get; private set; }
        public string Name { get; private set; }
        public string Hex { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentTextOnLight { get; private set; }
    }

    /// <summary>
    /// 界面主题：配色、DPI 缩放、字体缓存。
    /// 高对比度模式下固定使用浅色背景方案，并且固定【同时】使用两个强调色：
    /// 粉色 #f5abb9（可交互区域的底色）与蓝色 #5bcffa（交互结果的呈现）。
    /// 两者是一套配色体系，不是让用户在“粉”和“蓝”之间二选一。
    /// </summary>
    internal static class Theme
    {
        public const string DefaultPaletteKey = "water";

        /// <summary>高对比度模式：蓝色（交互结果的呈现色）的来源，键固定。</summary>
        public const string HighContrastPaletteKey = "hc-a";

        /// <summary>高对比度模式：粉色（可交互区域的底色）的来源，键固定。</summary>
        public const string HighContrastPaletteKeyB = "hc-b";

        /// <summary>
        /// 高对比度两个颜色同时使用时的说明（设置页只做呈现，不做成选项）。
        /// </summary>
        public const string HighContrastPairHint =
            "两个颜色同时使用，不需要选择：粉色 #f5abb9 是可交互区域的底色，蓝色 #5bcffa 用来呈现交互结果。";

        private static readonly ThemePalette[] AllPalettes =
        {
            new ThemePalette("water",   "水蓝", "#7AB5D6", "#2E7CA8"),
            new ThemePalette("teal",    "青碧", "#48C0A3", "#12816A"),
            new ThemePalette("lilac",   "丁香", "#B57EDC", "#7B44B0"),
            new ThemePalette("apricot", "杏黄", "#FFA631", "#A66300"),
            new ThemePalette(HighContrastPaletteKey,  "高对比·蓝（交互结果）", "#5bcffa", "#0B5C7A"),
            new ThemePalette(HighContrastPaletteKeyB, "高对比·粉（可交互底色）", "#f5abb9", "#8E2F45")
        };

        /// <summary>普通模式下可选的颜色（高对比度专用的两个颜色不出现在这里，避免被当成“配色选择”）。</summary>
        private static readonly ThemePalette[] SelectablePaletteList = BuildSelectablePalettes();

        private static ThemePalette[] BuildSelectablePalettes()
        {
            var list = new List<ThemePalette>();
            foreach (ThemePalette palette in AllPalettes)
            {
                if (IsHighContrastKey(palette.Key)) continue;
                list.Add(palette);
            }
            return list.ToArray();
        }

        /// <summary>是否是高对比度专用的固定颜色键。</summary>
        public static bool IsHighContrastKey(string key)
        {
            return string.Equals(key, HighContrastPaletteKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, HighContrastPaletteKeyB, StringComparison.OrdinalIgnoreCase);
        }

        private static ThemePalette _palette = AllPalettes[0];
        private static ThemeMode _mode = ThemeMode.Light;
        private static bool _highContrast;
        private static float _fontSize = DefaultFontSize;
        private static float _dpiScale = 1f;
        private static float _density = 1f;
        private static float _logicalWidth = 1920f;
        private static float _logicalHeight = 1080f;

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        private static readonly Dictionary<string, Font> IconFontCache = new Dictionary<string, Font>();

        public const string FontFamilyName = "Microsoft YaHei UI";
        public const string IconFontName = "Segoe MDL2 Assets";
        public const float MaxFontSize = 16f;
        public const float MinFontSize = 8f;
        public const float DefaultFontSize = 10f;

        /// <summary>版本号文字（界面显示用）。</summary>
        public const string VersionText = "1.0.2_a";

        /// <summary>软件简称 / 正式名称。</summary>
        public const string ShortName = "LTFC";
        public const string ProductName = "Lots_Tools_For_Class";

        public static event Action Changed;

        // ---------- 状态 ----------
        public static bool Dark { get { return _mode == ThemeMode.Dark; } }
        public static ThemeMode Mode { get { return _mode; } }
        public static bool HighContrast { get { return _highContrast; } }
        public static ThemePalette Palette { get { return _palette; } }
        public static ThemePalette[] Palettes { get { return AllPalettes; } }
        public static float FontSize { get { return _fontSize; } }
        public static float DpiScale { get { return _dpiScale; } }
        public static float Density { get { return _density; } }
        public static float LogicalWidth { get { return _logicalWidth; } }
        public static float LogicalHeight { get { return _logicalHeight; } }

        public static ThemePalette FindPalette(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                foreach (ThemePalette p in AllPalettes)
                {
                    if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p;
                }
            }
            return AllPalettes[0];
        }

        /// <summary>高对比度可用的两个配色。</summary>
        public static ThemePalette HighContrastA { get { return FindPalette(HighContrastPaletteKey); } }
        public static ThemePalette HighContrastB { get { return FindPalette(HighContrastPaletteKeyB); } }

        // ---------- 颜色 ----------
        public static Color Window
        {
            get
            {
                if (_highContrast) return Color.FromArgb(0xFA, 0xFA, 0xFA);
                return _mode == ThemeMode.Dark ? Color.FromArgb(0x1B, 0x1F, 0x24) : Color.FromArgb(0xF4, 0xF6, 0xF9);
            }
        }

        public static Color Surface
        {
            get
            {
                if (_highContrast) return Color.White;
                return _mode == ThemeMode.Dark ? Color.FromArgb(0x24, 0x2A, 0x31) : Color.White;
            }
        }

        public static Color SurfaceAlt
        {
            get
            {
                if (_highContrast) return Color.FromArgb(0xF2, 0xF2, 0xF2);
                return _mode == ThemeMode.Dark ? Color.FromArgb(0x2C, 0x33, 0x3B) : Color.FromArgb(0xF7, 0xF9, 0xFB);
            }
        }

        public static Color NavBackground
        {
            get
            {
                if (_highContrast) return Color.FromArgb(0xEE, 0xF3, 0xF6);
                return _mode == ThemeMode.Dark ? Color.FromArgb(0x16, 0x19, 0x1D) : Color.FromArgb(0xFA, 0xFB, 0xFD);
            }
        }

        public static Color Outline
        {
            get
            {
                if (_highContrast) return Color.FromArgb(0x11, 0x11, 0x11);
                return _mode == ThemeMode.Dark ? Color.FromArgb(0x3A, 0x43, 0x4D) : Color.FromArgb(0xE1, 0xE6, 0xEC);
            }
        }

        /// <summary>边框线宽（高对比度下更粗）。</summary>
        public static float BorderWidth { get { return _highContrast ? 1.6f : 1f; } }

        public static Color Text
        {
            get
            {
                if (_highContrast) return Color.Black;
                return _mode == ThemeMode.Dark ? Color.FromArgb(0xEC, 0xF1, 0xF6) : Color.FromArgb(0x1F, 0x27, 0x30);
            }
        }

        public static Color TextMuted
        {
            get
            {
                if (_highContrast) return Color.FromArgb(0x20, 0x20, 0x20);
                return _mode == ThemeMode.Dark ? Color.FromArgb(0xB6, 0xC1, 0xCC) : Color.FromArgb(0x5A, 0x66, 0x72);
            }
        }

        public static Color TextFaint
        {
            get
            {
                if (_highContrast) return Color.FromArgb(0x30, 0x30, 0x30);
                return _mode == ThemeMode.Dark ? Color.FromArgb(0x88, 0x95, 0xA2) : Color.FromArgb(0x8A, 0x95, 0xA1);
            }
        }

        public static Color Accent { get { return _palette.Accent; } }
        public static Color AccentHover { get { return Darken(_palette.Accent, 0.12); } }
        public static Color AccentPressed { get { return Darken(_palette.Accent, 0.22); } }

        public static Color AccentText
        {
            get
            {
                if (_highContrast) return _palette.AccentTextOnLight;
                return _mode == ThemeMode.Dark ? Lighten(_palette.Accent, 0.16) : _palette.AccentTextOnLight;
            }
        }

        public static Color AccentSoft
        {
            get { return Color.FromArgb(_mode == ThemeMode.Dark && !_highContrast ? 46 : 34, _palette.Accent); }
        }

        public static Color AccentSoftStrong
        {
            get { return Color.FromArgb(_mode == ThemeMode.Dark && !_highContrast ? 76 : 54, _palette.Accent); }
        }

        public static Color AccentOn
        {
            get { return Luminance(_palette.Accent) > 0.5 ? Hex("#0D2430") : Color.White; }
        }

        /// <summary>
        /// 高对比度模式下“可互动按钮”固定使用的配色（#f5abb9）。
        /// 普通 / 深色模式仍跟随用户选择的主题色，因此下方 Interactive* 系列
        /// 在非高对比度时等价于 Accent* 系列，只有高对比度时才切换到固定粉色。
        /// </summary>
        public const string InteractiveHex = "#f5abb9";

        /// <summary>高对比度模式下的交互结果色（蓝）。</summary>
        public const string ResultHex = "#5bcffa";

        /// <summary>
        /// 可交互区域的底色（高对比度 = 粉 #f5abb9，其余模式 = 主题色）。
        /// 高对比度下它与 <see cref="Result"/>（蓝）同时出现：粉色是“可点的区域”，蓝色是“交互结果”。
        /// </summary>
        public static Color Interactive
        {
            get { return _highContrast ? Hex(InteractiveHex) : _palette.Accent; }
        }

        /// <summary>可互动按钮的悬停填充色。</summary>
        public static Color InteractiveHover { get { return Darken(Interactive, 0.1); } }

        /// <summary>可互动按钮的按下填充色。</summary>
        public static Color InteractivePressed { get { return Darken(Interactive, 0.2); } }

        /// <summary>可互动按钮的浅色底（悬停 / 选中背景）。</summary>
        public static Color InteractiveSoft
        {
            get { return Color.FromArgb(_mode == ThemeMode.Dark && !_highContrast ? 46 : 34, Interactive); }
        }

        /// <summary>可互动按钮的较深浅色底（按下时）。</summary>
        public static Color InteractiveSoftStrong
        {
            get { return Color.FromArgb(_mode == ThemeMode.Dark && !_highContrast ? 76 : 54, Interactive); }
        }

        /// <summary>填充为 <see cref="Interactive"/> 时使用的文字 / 图标色（保证对比度）。</summary>
        public static Color InteractiveOn
        {
            get { return Luminance(Interactive) > 0.5 ? Hex("#0D2430") : Color.White; }
        }

        /// <summary>
        /// 透明底 / 浅底上的可互动文字与描边色。
        /// 高对比度下用深玫红，既保留 #f5abb9 的色系又能保证在白底上的可读性。
        /// </summary>
        public static Color InteractiveText
        {
            get { return _highContrast ? Hex("#5A1B2B") : AccentText; }
        }

        /// <summary>
        /// 交互结果的呈现色（高对比度 = 蓝 #5bcffa；其余模式 = 主题强调色）。
        /// 用于“操作之后看到的结果”：状态、路径、数值、摘要等文字与指示。
        /// 高对比度下它与 <see cref="Interactive"/>（粉）同时使用，不是二选一。
        /// </summary>
        public static Color Result
        {
            get { return _highContrast ? Hex(ResultHex) : _palette.Accent; }
        }

        /// <summary>交互结果的浅色底（结果徽标 / 结果面板底色）。</summary>
        public static Color ResultSoft
        {
            get { return Color.FromArgb(_mode == ThemeMode.Dark && !_highContrast ? 46 : 34, Result); }
        }

        /// <summary>交互结果的较深浅色底（强调用）。</summary>
        public static Color ResultSoftStrong
        {
            get { return Color.FromArgb(_mode == ThemeMode.Dark && !_highContrast ? 76 : 54, Result); }
        }

        /// <summary>
        /// 透明底 / 浅底上的结果文字与描边色。
        /// 高对比度下用深蓝，既保留 #5bcffa 的色系又能保证在白底上的可读性。
        /// </summary>
        public static Color ResultText
        {
            get { return _highContrast ? Hex("#0B5C7A") : AccentText; }
        }

        /// <summary>填充为 <see cref="Result"/> 时使用的文字 / 图标色（保证对比度）。</summary>
        public static Color ResultOn
        {
            get { return Luminance(Result) > 0.5 ? Hex("#0D2430") : Color.White; }
        }

        /// <summary>固定高对比度配色 A = 交互结果色（蓝 #5bcffa）。</summary>
        public static Color MarkA { get { return Result; } }

        /// <summary>固定高对比度配色 B = 可交互区域底色（粉 #f5abb9）。</summary>
        public static Color MarkB { get { return Interactive; } }

        /// <summary>普通模式下可选的强调色（不含高对比度专用的两个固定色）。</summary>
        public static ThemePalette[] SelectablePalettes { get { return SelectablePaletteList; } }

        public static Color Ok { get { return Color.FromArgb(0x2E, 0x9E, 0x5B); } }
        public static Color Warn { get { return Color.FromArgb(0xD1, 0x7A, 0x00); } }
        public static Color Danger { get { return Color.FromArgb(0xC0, 0x39, 0x39); } }

        public static double ShadowScale { get { return _mode == ThemeMode.Dark && !_highContrast ? 1d : 0.55d; } }

        // ---------- 圆角 / 间距 ----------
        public static int RadiusCard { get { return S(14); } }
        public static int RadiusSmall { get { return S(8); } }
        public static int PagePadding { get { return S(24); } }
        public static int Gap { get { return S(16); } }
        /// <summary>侧边栏宽度（触摸友好：比旧版更宽，容纳更大的文字）。</summary>
        public static int NavWidth { get { return S(258); } }

        // ---------- 侧边栏度量（触摸友好） ----------
        /// <summary>顶部标题字体。</summary>
        public static Font NavTitleFont { get { return Font(4f, FontStyle.Bold); } }

        /// <summary>分组项字体（比正文大 2pt，便于触摸与远距离阅读）。</summary>
        public static Font NavGroupFont { get { return Font(2f, FontStyle.Bold); } }

        /// <summary>子项字体（比正文大 1pt）。</summary>
        public static Font NavChildFont { get { return Font(1f, FontStyle.Regular); } }

        /// <summary>选中的子项字体。</summary>
        public static Font NavChildFontBold { get { return Font(1f, FontStyle.Bold); } }

        public static int NavTitleHeight { get { return S(66); } }
        public static int NavGroupHeight { get { return S(52); } }
        public static int NavChildHeight { get { return S(46); } }

        /// <summary>子项相对分组项的缩进。</summary>
        public static int NavChildIndent { get { return S(32); } }

        // ---------- 颜色工具 ----------
        public static Color Hex(string hex) { return ColorTranslator.FromHtml(hex); }
        public static Color Darken(Color c, double t) { return Mix(c, Color.Black, t); }
        public static Color Lighten(Color c, double t) { return Mix(c, Color.White, t); }
        public static Color WithAlpha(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }

        public static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255d;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0d, Math.Min(1d, t));
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // ---------- 字体 ----------
        public static Font Body { get { return Font(0f, FontStyle.Regular); } }
        public static Font BodyBold { get { return Font(0f, FontStyle.Bold); } }
        public static Font Small { get { return Font(-1f, FontStyle.Regular); } }

        public static float PointToPixel(float pt)
        {
            return Math.Max(8f, (float)Math.Round(pt * _dpiScale * 96f / 72f, 1));
        }

        public static Font Font(float deltaPt, FontStyle style)
        {
            string key = deltaPt.ToString("0.##") + "|" + (int)style;
            Font cached;
            if (FontCache.TryGetValue(key, out cached)) return cached;
            float px = PointToPixel(ClampFontSize(_fontSize + deltaPt));
            Font f;
            try { f = new Font(FontFamilyName, px, style, GraphicsUnit.Pixel); }
            catch (Exception) { f = new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
            FontCache[key] = f;
            return f;
        }

        public static Font IconFont(float deltaPt)
        {
            string key = deltaPt.ToString("0.##");
            Font cached;
            if (IconFontCache.TryGetValue(key, out cached)) return cached;
            float px = PointToPixel(ClampFontSize(_fontSize + deltaPt));
            Font f;
            try { f = new Font(IconFontAvailable ? IconFontName : FontFamilyName, px, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch (Exception) { f = new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel); }
            IconFontCache[key] = f;
            return f;
        }

        private static bool? _iconFontAvailable;

        public static bool IconFontAvailable
        {
            get
            {
                if (!_iconFontAvailable.HasValue)
                {
                    try
                    {
                        using (var family = new FontFamily(IconFontName))
                        {
                            _iconFontAvailable = string.Equals(family.Name, IconFontName, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception) { _iconFontAvailable = false; }
                }
                return _iconFontAvailable.Value;
            }
        }

        /// <summary>图标字体不可用时返回兜底符号，避免出现乱码方块。</summary>
        public static string IconOr(string glyph, string fallback)
        {
            return IconFontAvailable ? glyph : fallback;
        }

        /// <summary>画图标：有图标字体用字形，否则用普通字体画兜底符号。</summary>
        public static void DrawIcon(Graphics g, string glyph, string fallback, float deltaPt, Color color, Rectangle bounds, StringAlignment align)
        {
            if (IconFontAvailable) Painting.DrawGlyph(g, glyph, IconFont(deltaPt), color, bounds, align);
            else Painting.DrawGlyph(g, fallback, Font(deltaPt + 1f, FontStyle.Bold), color, bounds, align);
        }

        private static float ClampFontSize(float pt)
        {
            return Math.Max(MinFontSize, Math.Min(MaxFontSize, pt));
        }

        /// <summary>单行文字所需高度（像素，含行距），用于按字体度量排布，避免高 DPI 下文字重叠。</summary>
        public static int Line(Font font)
        {
            if (font == null) return S(20);
            return (int)Math.Ceiling(font.Height * 1.2f);
        }

        /// <summary>单行文字高度。</summary>
        public static float LineHeight(Graphics g, Font font)
        {
            return font.GetHeight(g) * 1.28f;
        }

        /// <summary>递归重绘整棵控件树（主题切换后消除残留旧色）。</summary>
        public static void InvalidateTree(Control root)
        {
            if (root == null) return;
            try
            {
                root.Invalidate(true);
                foreach (Control child in root.Controls) InvalidateTree(child);
            }
            catch (Exception) { }
        }

        // ---------- DPI / 主题应用 ----------
        public static void UpdateDpi(float dpiScale, Size physicalWorkArea)
        {
            if (dpiScale <= 0f) dpiScale = 1f;
            float density = ComputeDensity(dpiScale, physicalWorkArea);
            bool dirty = Math.Abs(dpiScale - _dpiScale) > 0.001f || Math.Abs(density - _density) > 0.001f;
            _dpiScale = dpiScale;
            _density = density;
            _logicalWidth = physicalWorkArea.Width / dpiScale;
            _logicalHeight = physicalWorkArea.Height / dpiScale;
            if (!dirty) return;
            ClearFonts();
            RaiseChanged();
        }

        private static float ComputeDensity(float dpiScale, Size physicalWorkArea)
        {
            if (physicalWorkArea.Width <= 0 || physicalWorkArea.Height <= 0) return 1f;
            float lw = physicalWorkArea.Width / dpiScale;
            float lh = physicalWorkArea.Height / dpiScale;
            if (lw < 1366f || lh < 800f) return 0.84f;
            if (lw < 1680f || lh < 1000f) return 0.92f;
            return 1f;
        }

        public static float RecommendedFontSize
        {
            get
            {
                float h = _logicalHeight > 0f ? _logicalHeight : 900f;
                if (h < 700f) return 9f;
                if (h < 900f) return 9.5f;
                if (h < 1200f) return 10f;
                return 10.5f;
            }
        }

        /// <summary>
        /// 应用主题。高对比度模式下忽略深浅设置，固定使用浅色背景方案。
        /// </summary>
        public static void Apply(float fontSize, bool dark, string paletteKey, bool highContrast)
        {
            _highContrast = highContrast;
            ThemeMode mode = (dark && !highContrast) ? ThemeMode.Dark : ThemeMode.Light;
            string key = paletteKey;

            // 高对比度：粉（可交互区域底色）+ 蓝（交互结果）是固定的一套，不跟随用户选择的强调色；
            // 用户选的强调色只在非高对比度下生效，因此离开高对比度时要把遗留的固定键换回普通配色。
            if (highContrast) key = HighContrastPaletteKey;
            else if (IsHighContrastKey(key)) key = DefaultPaletteKey;

            ThemePalette palette = FindPalette(key ?? DefaultPaletteKey);
            float size = ClampFontSize(fontSize);
            bool dirty = Math.Abs(size - _fontSize) > 0.001f || mode != _mode || palette != _palette;
            _fontSize = size;
            _mode = mode;
            _palette = palette;
            if (!dirty) return;
            ClearFonts();
            RaiseChanged();
        }

        public static void RaiseChanged()
        {
            Action h = Changed;
            if (h != null) h();
        }

        private static readonly List<Font> RetiredFonts = new List<Font>();
        private static System.Threading.Timer _retireTimer;

        /// <summary>
        /// 主题变化时清空字体缓存。旧字体不立即销毁，而是延迟 60 秒后统一释放，
        /// 避免控件在上一次绘制中临时持有的字体被提前释放。
        /// </summary>
        private static void ClearFonts()
        {
            lock (RetiredFonts)
            {
                RetiredFonts.AddRange(FontCache.Values);
                RetiredFonts.AddRange(IconFontCache.Values);
                FontCache.Clear();
                IconFontCache.Clear();

                // 极端情况下（频繁切换主题）避免无限堆积
                if (RetiredFonts.Count > 400)
                {
                    int remove = RetiredFonts.Count / 2;
                    for (int i = 0; i < remove; i++)
                    {
                        try { RetiredFonts[i].Dispose(); } catch (Exception) { }
                    }
                    RetiredFonts.RemoveRange(0, remove);
                }
            }

            if (_retireTimer == null)
            {
                _retireTimer = new System.Threading.Timer(_ => DisposeRetiredFonts(), null, 60000, 60000);
            }
        }

        private static void DisposeRetiredFonts()
        {
            try
            {
                lock (RetiredFonts)
                {
                    foreach (Font font in RetiredFonts)
                    {
                        try { font.Dispose(); } catch (Exception) { }
                    }
                    RetiredFonts.Clear();
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------- 缩放辅助（DPI × 屏幕紧凑系数） ----------
        public static int S(int px) { return (int)Math.Round(px * _dpiScale * _density); }
        public static int Sim(float px) { return (int)Math.Round(px); }
        public static float SF(float px) { return px * _dpiScale * _density; }
    }
}
