using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>程序图标（取自 exe 自身，找不到时用系统图标兜底）。</summary>
    internal static class IconFactory
    {
        private static Icon _appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    _appIcon = LoadFromExecutable() ?? SystemIcons.Application;
                }
                return _appIcon;
            }
        }

        /// <summary>从指定 exe 读取图标。</summary>
        public static Icon LoadFrom(string executablePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(executablePath) && System.IO.File.Exists(executablePath))
                {
                    Icon icon = Icon.ExtractAssociatedIcon(executablePath);
                    if (icon != null) return icon;
                }
            }
            catch (Exception)
            {
            }
            return SystemIcons.Application;
        }

        private static Icon LoadFromExecutable()
        {
            try
            {
                string path = Assembly.GetEntryAssembly() != null ? Assembly.GetEntryAssembly().Location : null;
                if (!string.IsNullOrEmpty(path)) return LoadFrom(path);
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>把图标转成可放到托盘/控件上的位图。</summary>
        public static Bitmap ToBitmap(Icon icon, int size)
        {
            if (icon == null) icon = SystemIcons.Application;
            var bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.DrawIcon(new Icon(icon, new Size(size, size)), new Rectangle(0, 0, size, size));
            }
            return bitmap;
        }
    }
}
