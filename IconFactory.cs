namespace HaloPixelLyric;

/// <summary>
/// 应用图标工厂：自绘"像素声波条"图标（窗体图标 + 托盘图标），并提供 MDL2 图标字体。
/// </summary>
public static class IconFactory
{
    private static Bitmap? _iconBitmap;

    /// <summary>生成应用图标位图：圆角主色底 + 五根声波像素条。</summary>
    public static Bitmap CreateAppBitmap(int size = 64)
    {
        _iconBitmap ??= Draw(size);
        return _iconBitmap;
    }

    public static Icon CreateAppIcon(int size = 64)
    {
        var bmp = CreateAppBitmap(size);
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle); // 应用生命周期图标，句柄交由系统回收
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var accent = ColorTranslator.FromHtml("#89B4FA");
        using var bg = new SolidBrush(accent);
        g.FillRoundedRectangle(bg, 2, 2, size - 4, size - 4, size * 0.22f);

        // 声波条（五根，中轴对称高低起伏）
        float[] bars = [0.32f, 0.62f, 0.95f, 0.55f, 0.24f];
        var barW = size * 0.11f;
        var gap = size * 0.07f;
        var total = bars.Length * barW + (bars.Length - 1) * gap;
        var x = (size - total) / 2f;
        using var fg = new SolidBrush(Color.White);
        foreach (var h in bars)
        {
            var bh = size * h * 0.6f;
            var y = (size - bh) / 2f;
            g.FillRoundedRectangle(fg, x, y, barW, bh, barW / 2);
            x += barW + gap;
        }
        return bmp;
    }

    /// <summary>MDL2 图标字体（Windows 内置；缺失时回退 null，调用方降级为纯文字）。</summary>
    public static Font? GlyphFont(float size)
    {
        try { return new Font("Segoe MDL2 Assets", size); }
        catch { return null; }
    }
}
