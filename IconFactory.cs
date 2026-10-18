namespace HaloPixelLyric;

/// <summary>应用图标：使用设计稿生成的 ico（exe 内嵌资源），托盘与窗口共用。</summary>
public static class IconFactory
{
    public static Icon CreateAppIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app-icon.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream) return new Icon(stream);
            }
        }
        catch { }
        return SystemIcons.Application;
    }
}
