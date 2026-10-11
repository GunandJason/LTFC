using System;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>页面基类：标题 / 副标题 + 内容区（支持滚动）。</summary>
    internal class PageBase : Control
    {
        private int _scroll;

        public PageBase()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Window;
            // 页面由主窗口用 SetBounds 精确定位（不使用 Dock，避免高 DPI 下的二次缩放与位置覆盖）
            Dock = DockStyle.None;
        }

        /// <summary>页面主标题。</summary>
        public string Title { get; set; }

        /// <summary>页面副标题（说明文字）。</summary>
        public string Subtitle { get; set; }

        /// <summary>页面键（与侧边栏对应）。</summary>
        public string Key { get; set; }

        /// <summary>是否允许滚动。</summary>
        protected bool WantsScroll { get; set; }

        protected int ScrollOffset { get { return _scroll; } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!WantsScroll)
            {
                base.OnMouseWheel(e);
                return;
            }

            int max = Math.Max(0, ContentHeight - ContentArea.Height);
            int next = Math.Max(0, Math.Min(max, _scroll - e.Delta));
            if (next != _scroll)
            {
                _scroll = next;
                LayoutChildren();
                Invalidate();
            }
        }

        /// <summary>内容总高度（由派生类提供，用于滚动范围）。</summary>
        protected virtual int ContentHeight { get { return ContentArea.Height; } }

        /// <summary>内容区（标题之下）。标题高度按字体度量计算，不依赖 Graphics / 窗口句柄。</summary>
        protected Rectangle ContentArea
        {
            get
            {
                int top = Theme.S(16) + Theme.Line(Theme.Font(3f, FontStyle.Bold)) + Theme.S(28);
                return new Rectangle(Theme.PagePadding, top, Math.Max(Theme.S(120), Width - Theme.PagePadding * 2),
                    Math.Max(0, Height - top - Theme.S(12)));
            }
        }

        /// <summary>把子控件按“滚动偏移”放到内容区中。</summary>
        protected void ApplyScroll(Control child, int y, int height)
        {
            Rectangle area = ContentArea;
            child.SetBounds(area.Left, area.Top + y - _scroll, area.Width, height);
        }

        /// <summary>重新排布页面内的子控件（子类重写）。</summary>
        protected virtual void LayoutChildren()
        {
        }

        /// <summary>由外部在尺寸变化后调用。</summary>
        public void Relayout()
        {
            if (IsDisposed) return;

            try
            {
                LayoutChildren();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("页面布局失败：" + (Title ?? Key), ex);
            }
            Invalidate();
        }

        /// <summary>
        /// 取一个用于测量文字的 Graphics。
        /// 控件已释放或句柄不可用时改用屏幕 Graphics，避免在关闭 / 释放阶段抛 ObjectDisposedException。
        /// </summary>
        protected Graphics SafeGraphics()
        {
            if (!IsDisposed)
            {
                try { return CreateGraphics(); }
                catch (Exception) { }
            }

            try { return Graphics.FromHwnd(IntPtr.Zero); }
            catch (Exception) { return null; }
        }

        /// <summary>页面被显示时调用（刷新状态）。</summary>
        public virtual void RefreshState()
        {
        }

        /// <summary>把页面滚动到最底部（自测快照与定位页尾时使用）。</summary>
        public void ScrollToBottom()
        {
            if (!WantsScroll) return;
            _scroll = Math.Max(0, ContentHeight - ContentArea.Height);
            LayoutChildren();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated && !IsDisposed) Relayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            using (var brush = new SolidBrush(Theme.Window))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            Rectangle area = ContentArea;
            int titleHeight = (int)Math.Round(Theme.LineHeight(g, Theme.Font(3f, FontStyle.Bold)));
            Painting.DrawText(g, Title ?? string.Empty, Theme.Font(3f, FontStyle.Bold), Theme.Text,
                new Rectangle(Theme.PagePadding, Theme.S(10), Width - Theme.PagePadding * 2, titleHeight), StringAlignment.Near);

            if (!string.IsNullOrEmpty(Subtitle))
            {
                Painting.DrawText(g, Subtitle, Theme.Small, Theme.TextMuted,
                    new Rectangle(Theme.PagePadding, Theme.S(12) + titleHeight, Width - Theme.PagePadding * 2, Theme.Line(Theme.Small)),
                    StringAlignment.Near);
            }

            OnPaintContent(g, area);
        }

        /// <summary>绘制内容（子类重写）。</summary>
        protected virtual void OnPaintContent(Graphics g, Rectangle area)
        {
        }

        /// <summary>绘制一张卡片底。</summary>
        protected static void DrawCard(Graphics g, Rectangle bounds)
        {
            Painting.FillCard(g, bounds, Theme.RadiusCard, Theme.Surface, Theme.Outline, Theme.BorderWidth, Theme.ShadowScale);
        }

        /// <summary>绘制小节标题。</summary>
        protected static void DrawSectionTitle(Graphics g, string text, Rectangle bounds)
        {
            Painting.DrawText(g, text, Theme.Font(0.5f, FontStyle.Bold), Theme.TextMuted, bounds, StringAlignment.Near);
        }

        /// <summary>创建一个主题风格的按钮。</summary>
        protected UiButton CreateButton(string text, UiButtonKind kind, EventHandler onClick)
        {
            var button = new UiButton { Text = text, Kind = kind, PaddingX = 16 };
            if (onClick != null) button.Click += onClick;
            Controls.Add(button);
            return button;
        }

        /// <summary>创建一个开关。</summary>
        protected UiSwitch CreateSwitch(bool value, EventHandler onChanged)
        {
            var toggle = new UiSwitch { Checked = value };
            if (onChanged != null) toggle.CheckedChanged += onChanged;
            Controls.Add(toggle);
            return toggle;
        }
    }
}
