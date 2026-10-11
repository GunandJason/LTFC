using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LTFC
{
    internal enum UiButtonKind
    {
        Primary,
        Secondary,
        Danger,
        Ghost
    }

    /// <summary>
    /// 自绘按钮。高对比度模式下使用更粗的边框与更高的对比度配色。
    /// </summary>
    internal sealed class UiButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private bool _enabledVisual = true;

        public UiButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(120), Theme.S(38));
            Cursor = Cursors.Hand;
        }

        /// <summary>按钮使用的字体（每次绘制时从主题取，避免持有已释放的字体实例）。</summary>
        private static Font ButtonFont { get { return Theme.Font(0f, FontStyle.Regular); } }

        public UiButtonKind Kind { get; set; }

        /// <summary>按钮上的图标字形（可选）与兜底符号。</summary>
        public string Glyph { get; set; }

        public string GlyphFallback { get; set; }

        /// <summary>水平内边距（内容自适应宽度时使用）。</summary>
        public int PaddingX { get; set; }

        public bool IsEnabledVisual
        {
            get { return _enabledVisual; }
            set { _enabledVisual = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        /// <summary>以编程方式触发一次点击（自测与快捷键复用）。</summary>
        public void PerformClick()
        {
            if (!Enabled || !IsEnabledVisual) return;
            OnClick(EventArgs.Empty);
        }

        /// <summary>按文本与图标计算合适宽度。</summary>
        public int MeasureWidth(Graphics g, string text)
        {
            int pad = PaddingX > 0 ? Theme.S(PaddingX) : Theme.S(18);
            Font font = ButtonFont;
            float iconWidth = string.IsNullOrEmpty(Glyph) ? 0f : Painting.Measure(g, "M", Theme.IconFont(0.6f)).Width + Theme.S(6);
            float textWidth = string.IsNullOrEmpty(text) ? 0f : Painting.Measure(g, text, font).Width + Theme.S(2);
            return (int)Math.Round(pad * 2 + iconWidth + textWidth);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Theme.RadiusSmall;

            Color fill, border, textColor;
            ResolveColors(out fill, out border, out textColor);
            if (!_enabledVisual || !Enabled)
            {
                fill = Theme.WithAlpha(fill, 90);
                border = Theme.WithAlpha(border, 110);
                textColor = Theme.WithAlpha(textColor, 130);
            }

            Painting.FillRoundRect(g, r, radius, fill);
            if (Theme.HighContrast)
            {
                Painting.StrokeRoundRect(g, r, radius, border, Theme.BorderWidth + 0.8f);
            }
            else if (border.A > 0)
            {
                Painting.StrokeRoundRect(g, r, radius, border, 1f);
            }

            Rectangle content = new Rectangle(Theme.S(8), 0, Width - Theme.S(16), Height);
            int textLeft = content.Left;
            if (!string.IsNullOrEmpty(Glyph) || !string.IsNullOrEmpty(GlyphFallback))
            {
                int iconBox = Theme.S(22);
                var iconRect = new Rectangle(content.Left, content.Top + (Height - iconBox) / 2, iconBox, iconBox);
                Theme.DrawIcon(g, Glyph ?? string.Empty, GlyphFallback ?? string.Empty, 0.6f, textColor, iconRect, StringAlignment.Center);
                textLeft = iconRect.Right + Theme.S(4);
            }

            var textRect = new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - Theme.S(8)), Math.Max(1, Height));
            Painting.DrawText(g, Text, ButtonFont, textColor, textRect, StringAlignment.Center);

            if (Focused)
            {
                using (var pen = new Pen(Theme.Text, Math.Max(1f, Theme.BorderWidth)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    var focus = new Rectangle(r.X + Theme.S(3), r.Y + Theme.S(3), r.Width - Theme.S(6), r.Height - Theme.S(6));
                    using (GraphicsPath path = Painting.RoundRect(focus, Math.Max(2, radius - Theme.S(3))))
                    {
                        pen.Alignment = PenAlignment.Inset;
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        private void ResolveColors(out Color fill, out Color border, out Color textColor)
        {
            // 可互动按钮统一使用 Theme.Interactive*：高对比度模式下固定为 #f5abb9，
            // 普通 / 深色模式等价于原来的 Accent*，因此外观不会变化。
            switch (Kind)
            {
                case UiButtonKind.Primary:
                    fill = _pressed ? Theme.InteractivePressed : (_hover ? Theme.InteractiveHover : Theme.Interactive);
                    border = Theme.HighContrast ? Theme.Text : Color.Transparent;
                    textColor = Theme.InteractiveOn;
                    break;
                case UiButtonKind.Danger:
                    fill = _pressed ? Theme.Darken(Theme.Danger, 0.18) : (_hover ? Theme.Darken(Theme.Danger, 0.1) : Theme.Danger);
                    border = Theme.HighContrast ? Theme.Text : Color.Transparent;
                    textColor = Color.White;
                    break;
                case UiButtonKind.Ghost:
                    fill = _pressed ? Theme.InteractiveSoftStrong : (_hover ? Theme.InteractiveSoft : Color.Transparent);
                    border = Theme.HighContrast ? Theme.Text : Color.Transparent;
                    textColor = Theme.InteractiveText;
                    break;
                default:
                    fill = _pressed
                        ? (Theme.HighContrast ? Theme.InteractiveSoftStrong : Theme.SurfaceAlt)
                        : (_hover ? (Theme.HighContrast ? Theme.InteractiveSoft : Theme.SurfaceAlt) : Theme.Surface);
                    border = Theme.Outline;
                    textColor = Theme.Text;
                    break;
            }
        }
    }
}
