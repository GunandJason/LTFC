using System;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>配色色块（选择强调色用；也用于不可点击地“呈现”高对比度的两个固定色）。</summary>
    internal sealed class UiSwatch : Control
    {
        private bool _hover;
        private bool _selectable = true;

        public UiSwatch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(40), Theme.S(40));
            Cursor = Cursors.Hand;
        }

        /// <summary>该色块代表的配色。</summary>
        public ThemePalette Palette { get; set; }

        public bool Selected { get; set; }

        /// <summary>是否可点击选择。纯说明用的色块设为 false：不响应点击、不显示手型光标、不显示悬停态。</summary>
        public bool Selectable
        {
            get { return _selectable; }
            set
            {
                _selectable = value;
                if (!value) _hover = false;
                Cursor = value ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = _selectable; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) PerformClick();
            base.OnMouseUp(e);
        }

        /// <summary>以编程方式触发一次点击（自测复用）。不可点击的说明色块不会触发，返回 false。</summary>
        public bool PerformClick()
        {
            if (!_selectable) return false;
            OnClick(EventArgs.Empty);
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            Color color = Palette != null ? Palette.Accent : Theme.Accent;
            var box = new Rectangle(Theme.S(4), Theme.S(4), Width - Theme.S(9), Height - Theme.S(9));
            Painting.FillRoundRect(g, box, Theme.RadiusSmall, color);

            Color border = Selected ? Theme.Text : (_hover && _selectable ? Theme.TextMuted : Theme.Outline);
            float width = Selected ? Theme.BorderWidth + 1f : 1f;
            Painting.StrokeRoundRect(g, box, Theme.RadiusSmall, border, width);

            if (Selected)
            {
                // 选中态：内部再描一圈对比色，保证在浅色/高对比度下都清晰可见
                var inner = new Rectangle(box.X + Theme.S(3), box.Y + Theme.S(3), box.Width - Theme.S(6), box.Height - Theme.S(6));
                Painting.StrokeRoundRect(g, inner, Math.Max(2, Theme.RadiusSmall - Theme.S(3)),
                    Theme.Luminance(color) > 0.6 ? Theme.Text : Color.White, Math.Max(1.4f, Theme.BorderWidth));
            }
        }
    }
}
