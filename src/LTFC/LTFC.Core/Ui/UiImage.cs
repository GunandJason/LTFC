using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// 显示一张本地图片（等比缩放到控件内、不放大），用于“关于 → 赞赏码”。
    /// 图片文件缺失时显示提示文字，不会抛异常。
    /// </summary>
    internal sealed class UiImage : Control
    {
        private Image _image;
        private bool _loaded;

        public UiImage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(240), Theme.S(200));
        }

        /// <summary>图片文件路径。</summary>
        public string ImagePath { get; set; }

        /// <summary>图片下方的说明文字。</summary>
        public string Caption { get; set; }

        /// <summary>图片缺失时的提示文字。</summary>
        public string EmptyText { get; set; }

        private void EnsureImage()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
                {
                    // 用副本加载，避免文件被外部程序占用时无法访问
                    using (var stream = new FileStream(ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        _image = Image.FromStream(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("加载图片失败：" + ImagePath, ex);
                _image = null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureImage();
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            bool hasCaption = !string.IsNullOrEmpty(Caption);
            int captionHeight = hasCaption ? Theme.Line(Theme.Small) : 0;
            var imageArea = new Rectangle(0, 0, Width, Math.Max(Theme.S(24), Height - captionHeight - Theme.S(6)));

            if (_image == null)
            {
                Painting.FillRoundRect(g, imageArea, Theme.RadiusSmall, Theme.SurfaceAlt);
                Painting.StrokeRoundRect(g, imageArea, Theme.RadiusSmall, Theme.Outline, Theme.BorderWidth);
                Painting.DrawText(g, string.IsNullOrEmpty(EmptyText) ? "（图片缺失）" : EmptyText,
                    Theme.Small, Theme.TextFaint, imageArea, StringAlignment.Center);
            }
            else
            {
                Size fit = Painting.FitSize(_image.Size, imageArea.Width, imageArea.Height);
                var target = new Rectangle(
                    imageArea.Left + (imageArea.Width - fit.Width) / 2,
                    imageArea.Top + (imageArea.Height - fit.Height) / 2,
                    fit.Width, fit.Height);

                using (System.Drawing.Drawing2D.GraphicsPath path = Painting.RoundRect(target, Theme.RadiusSmall))
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    g.DrawImage(_image, target);
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }

                Painting.StrokeRoundRect(g, target, Theme.RadiusSmall, Theme.Outline, Theme.BorderWidth);
            }

            if (hasCaption)
            {
                Painting.DrawText(g, Caption, Theme.Small, Theme.TextMuted,
                    new Rectangle(0, imageArea.Bottom + Theme.S(4), Width, captionHeight), StringAlignment.Center);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                try { _image.Dispose(); } catch (Exception) { }
                _image = null;
            }

            base.Dispose(disposing);
        }
    }
}
