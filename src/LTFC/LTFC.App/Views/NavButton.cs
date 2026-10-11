using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// 侧边栏里的一个可点击条目。
    /// 层级：0 = 模块分组项（更大字号），1 = 分组下的子项（点击直接进入该模块的分页）。
    /// 触摸友好：字体比正文更大、行高更高。
    /// 侧边栏不显示任何图标，只保留文字与右侧折叠箭头（箭头有独立的命中区域）。
    /// </summary>
    internal sealed class NavButton : Control
    {
        private bool _hover;
        private bool _selected;
        private bool _expanded;
        private bool _arrowHover;
        private bool _pressed;

        public NavButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = Theme.NavGroupHeight;
        }

        /// <summary>
        /// 点击右侧折叠箭头时触发（只展开 / 收起分组，不切换页面）。
        /// 与普通点击分开处理，避免箭头点击走到页面切换逻辑上造成卡顿。
        /// </summary>
        public event EventHandler ArrowClick;

        /// <summary>层级：0 = 分组项，1 = 子项。</summary>
        public int Level { get; set; }

        /// <summary>是否是折叠/展开分组（显示折叠箭头）。</summary>
        public bool IsExpandable { get; set; }

        /// <summary>分组是否展开。</summary>
        public bool Expanded
        {
            get { return _expanded; }
            set { if (_expanded == value) return; _expanded = value; Invalidate(); }
        }

        /// <summary>次级说明文字（可选，显示在名称右侧）。</summary>
        public string Hint { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected == value) return; _selected = value; Invalidate(); }
        }

        public bool IsTitle { get; set; }

        /// <summary>
        /// 折叠箭头的命中 / 绘制区域（右对齐）。
        /// 绘制与命中共用同一个矩形，保证“看到哪里能点，哪里就真能点到”。
        /// </summary>
        public Rectangle ArrowBounds
        {
            get
            {
                if (!IsExpandable) return Rectangle.Empty;
                int size = Math.Min(Width, Math.Max(Theme.S(30), Theme.S(34)));
                return new Rectangle(Math.Max(0, Width - size), Theme.S(4), Math.Min(Width, size), Math.Max(1, Height - Theme.S(8)));
            }
        }

        /// <summary>指定坐标是否落在折叠箭头上。</summary>
        public bool HitArrow(Point point)
        {
            return IsExpandable && ArrowBounds.Contains(point);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _arrowHover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool overArrow = HitArrow(e.Location);
            if (overArrow != _arrowHover)
            {
                _arrowHover = overArrow;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = e.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool inside = ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();

            if (e.Button == MouseButtons.Left && inside)
            {
                if (HitArrow(e.Location))
                {
                    // 箭头：只切换展开状态，立刻返回（不切换页面，避免触发页面切换的耗时逻辑）
                    EventHandler handler = ArrowClick;
                    if (handler != null) handler(this, EventArgs.Empty);
                    base.OnMouseUp(e);
                    return;
                }

                PerformClick();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // 鼠标停在侧边栏任意条目上都能滚动
            var nav = Parent as SideNav;
            if (nav != null)
            {
                nav.ScrollByWheel(e.Delta);
                return;
            }

            base.OnMouseWheel(e);
        }

        public void PerformClick() { OnClick(EventArgs.Empty); }

        /// <summary>按层级取字体（每次绘制时从主题取，避免持有已释放的字体实例）。</summary>
        private Font ResolveFont()
        {
            if (IsTitle) return Theme.NavTitleFont;
            if (Level <= 0) return Theme.NavGroupFont;
            return Selected ? Theme.NavChildFontBold : Theme.NavChildFont;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color textColor = Selected
                ? (Theme.HighContrast ? Theme.Text : Theme.AccentText)
                : (_hover ? Theme.Text : Theme.TextMuted);
            Color background = Color.Transparent;

            if (_pressed && _hover)
            {
                background = Theme.HighContrast ? Theme.InteractiveSoftStrong : Theme.SurfaceAlt;
            }
            else if (Selected)
            {
                background = Theme.HighContrast ? Theme.InteractiveSoft : Theme.AccentSoft;
            }
            else if (_hover)
            {
                background = Theme.HighContrast ? Theme.InteractiveSoft : Theme.SurfaceAlt;
            }

            if (background.A > 0)
            {
                Painting.FillRoundRect(g, r, Theme.RadiusSmall, background);
                if (Theme.HighContrast)
                {
                    Painting.StrokeRoundRect(g, r, Theme.RadiusSmall, Theme.Outline, Theme.BorderWidth);
                }
            }

            Font font = ResolveFont();
            Rectangle arrow = ArrowBounds;

            // 侧边栏不绘制任何图标：只有文字与右侧折叠箭头
            int left = Level > 0 ? Theme.NavChildIndent : Theme.S(16);
            int right = IsExpandable ? arrow.Left - Theme.S(6) : Width - Theme.S(10);
            var textRect = new Rectangle(left, 0, Math.Max(Theme.S(20), right - left), Height);
            Painting.DrawText(g, Text, font, textColor, textRect, StringAlignment.Near);

            if (!string.IsNullOrEmpty(Hint))
            {
                Color hintColor = Selected ? Theme.InteractiveText : Theme.TextFaint;
                Painting.DrawText(g, Hint, Theme.Small, hintColor,
                    new Rectangle(textRect.Left, 0, textRect.Width, Height), StringAlignment.Far);
            }

            if (IsExpandable)
            {
                // 折叠箭头（▾ / ▸）：箭头是唯一保留的图形，命中区域与这里绘制的矩形完全一致
                if (_arrowHover)
                {
                    Painting.FillRoundRect(g, arrow, Theme.RadiusSmall, Theme.InteractiveSoft);
                }

                Color arrowColor = _arrowHover
                    ? Theme.InteractiveText
                    : textColor;
                string arrowGlyph = Expanded ? "\uE70D" : "\uE76C";
                string arrowFallback = Expanded ? "▾" : "▸";
                Theme.DrawIcon(g, arrowGlyph, arrowFallback, 0f, arrowColor, arrow, StringAlignment.Center);
            }

            if (Selected && Theme.HighContrast)
            {
                using (var pen = new Pen(Theme.Interactive, Theme.BorderWidth + 0.8f))
                {
                    g.DrawLine(pen, Theme.S(4), r.Y + Theme.S(6), Theme.S(4), r.Bottom - Theme.S(6));
                }
            }
        }
    }

    /// <summary>侧边栏分隔线（仅视觉分隔，不是菜单项）。</summary>
    internal sealed class NavSeparator : Control
    {
        public NavSeparator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Theme.S(13);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Theme.Outline, Theme.BorderWidth))
            {
                int y = Height / 2;
                e.Graphics.DrawLine(pen, Theme.S(14), y, Width - Theme.S(14), y);
            }
        }
    }
}
