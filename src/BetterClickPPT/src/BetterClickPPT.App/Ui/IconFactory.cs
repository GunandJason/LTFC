using System.Drawing.Drawing2D;
using BetterClickPPT.Core;

namespace BetterClickPPT.App.Ui;

/// <summary>
/// 运行时绘制界面图标（仓库中不保存二进制图片）。
/// </summary>
public static class IconFactory
{
    private static Icon? _enabledIcon;
    private static Icon? _disabledIcon;
    private static Icon? _appIcon;

    /// <summary>品牌主色。</summary>
    public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);

    /// <summary>品牌主色（浅）。</summary>
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);

    /// <summary>托盘图标：已启用。</summary>
    public static Icon EnabledIcon => _enabledIcon ??= CreateIcon(accent: true);

    /// <summary>托盘图标：已暂停。</summary>
    public static Icon DisabledIcon => _disabledIcon ??= CreateIcon(accent: false);

    /// <summary>窗口图标。</summary>
    public static Icon AppIcon => _appIcon ??= CreateIcon(accent: true);

    /// <summary>绘制指定尺寸的图标位图。</summary>
    public static Bitmap CreateBitmap(int size, bool accent)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var baseColor = accent ? Accent : Color.FromArgb(148, 163, 184);
        var darkColor = accent ? AccentDark : Color.FromArgb(100, 116, 139);

        using (var path = CreateRoundedRectangle(new Rectangle(0, 0, size - 1, size - 1), size * 0.22f))
        using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(size, size), baseColor, darkColor))
        {
            graphics.FillPath(brush, path);
        }

        // 轻触波纹
        var penWidth = Math.Max(1f, size * 0.075f);
        using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), penWidth))
        {
            var ring = size * 0.30f;
            graphics.DrawEllipse(pen, size * 0.10f, (size - ring) / 2f, ring, ring);
        }

        // 下一页三角
        var points = new[]
        {
            new PointF(size * 0.46f, size * 0.27f),
            new PointF(size * 0.46f, size * 0.73f),
            new PointF(size * 0.78f, size * 0.50f)
        };
        using (var brush = new SolidBrush(Color.White))
        {
            graphics.FillPolygon(brush, points);
        }

        if (!accent)
        {
            // 暂停状态：加一条斜杠
            using var pen = new Pen(Color.FromArgb(240, 239, 68, 68), Math.Max(1.5f, size * 0.12f));
            graphics.DrawLine(pen, size * 0.22f, size * 0.78f, size * 0.78f, size * 0.22f);
        }

        return bitmap;
    }

    /// <summary>绘制指定尺寸的图标。</summary>
    public static Icon CreateIcon(int size = 32, bool accent = true)
    {
        using var bitmap = CreateBitmap(size, accent);
        var handle = bitmap.GetHicon();
        try
        {
            // 复制一份，避免依赖位图句柄的生命周期。
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>创建圆角矩形路径。</summary>
    public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
