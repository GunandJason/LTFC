using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>绘图辅助（圆角矩形、文字对齐绘制）。</summary>
    internal static class Painting
    {
        /// <summary>圆角矩形路径。</summary>
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            if (d <= 2)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundRect(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath path = RoundRect(r, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void StrokeRoundRect(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            using (GraphicsPath path = RoundRect(r, radius))
            using (var pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>带投影的卡片。</summary>
        public static void FillCard(Graphics g, Rectangle r, int radius, Color fill, Color border, float borderWidth, double shadowScale)
        {
            int offset = Math.Max(1, (int)Math.Round(2 * shadowScale));
            Color shadow = Color.FromArgb((int)Math.Round(28 * shadowScale), 0, 0, 0);
            FillRoundRect(g, new Rectangle(r.X, r.Y + offset, r.Width, r.Height), radius, shadow);
            FillRoundRect(g, r, radius, fill);
            StrokeRoundRect(g, r, radius, border, borderWidth);
        }

        /// <summary>按对齐方式绘制单行文字（自动裁剪）。</summary>
        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, StringAlignment align)
        {
            if (string.IsNullOrEmpty(text) || font == null) return;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.Alignment = align;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, font, brush, bounds, format);
            }
        }

        public static void DrawTextLeft(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            DrawText(g, text, font, color, bounds, StringAlignment.Near);
        }

        /// <summary>绘制多行文字（左对齐、自动换行）。</summary>
        public static void DrawTextWrapped(Graphics g, string text, Font font, Color color, Rectangle bounds, int lineGap)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var brush = new SolidBrush(color))
            {
                var area = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                using (var format = new StringFormat(StringFormatFlags.LineLimit))
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;
                    format.Trimming = StringTrimming.EllipsisWord;
                    g.DrawString(text, font, brush, area, format);
                }
            }
        }

        /// <summary>绘制图标字形（居中于 bounds）。</summary>
        public static void DrawGlyph(Graphics g, string glyph, Font font, Color color, Rectangle bounds, StringAlignment align)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.Alignment = align;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(glyph, font, brush, bounds, format);
            }
        }

        /// <summary>测量文本宽度。</summary>
        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            try
            {
                if (g == null) return Approximate(text, font);
                return g.MeasureString(text, font, 100000, StringFormat.GenericTypographic);
            }
            catch (Exception)
            {
                // 兜底：按字号粗略估算，避免个别字体/文本导致测量异常
                return Approximate(text, font);
            }
        }

        private static SizeF Approximate(string text, Font font)
        {
            float size = font != null ? font.Size : 10f;
            float height = font != null ? font.Height : 16f;
            return new SizeF(text.Length * size * 0.62f, height);
        }

        /// <summary>高质量绘制设置。</summary>
        public static void SetupQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        /// <summary>把图像等比缩放到指定最大宽度/高度以内（不放大）。</summary>
        public static Size FitSize(Size source, int maxWidth, int maxHeight)
        {
            if (source.Width <= 0 || source.Height <= 0) return Size.Empty;
            double scale = Math.Min(1d, Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height));
            return new Size(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        }
    }
}
