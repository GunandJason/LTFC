using System;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>开关控件（设置页用）。高对比度模式下使用更明显的开关样式。</summary>
    internal sealed class UiSwitch : Control
    {
        private bool _checked;
        private bool _hover;

        public UiSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(52), Theme.S(28));
            Cursor = Cursors.Hand;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Checked = !Checked;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = track.Height / 2;

            // 高对比度模式：开关（可互动控件）统一使用 Interactive(#f5abb9) 配色
            Color trackColor = _checked ? (_hover ? Theme.InteractiveHover : Theme.Interactive) : Theme.SurfaceAlt;
            Color knobColor = _checked ? Theme.InteractiveOn : Theme.TextMuted;
            Color borderColor = Theme.HighContrast ? Theme.Text : (_checked ? Theme.Interactive : Theme.Outline);

            Painting.FillRoundRect(g, track, radius, trackColor);
            Painting.StrokeRoundRect(g, track, radius, borderColor, Theme.BorderWidth);

            int knobSize = track.Height - Theme.S(6);
            int knobX = _checked ? track.Right - knobSize - Theme.S(3) : track.Left + Theme.S(3);
            var knob = new Rectangle(knobX, track.Top + Theme.S(3), knobSize, knobSize);
            Painting.FillRoundRect(g, knob, knobSize / 2, Theme.HighContrast ? (_checked ? Theme.Text : Color.White) : Color.White);
            Painting.StrokeRoundRect(g, knob, knobSize / 2, Theme.HighContrast ? Theme.Text : Theme.WithAlpha(knobColor, 90), Theme.BorderWidth);
        }
    }
}
