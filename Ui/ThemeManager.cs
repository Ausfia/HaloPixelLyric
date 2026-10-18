using Microsoft.Win32;
using System.Windows;
using Application = System.Windows.Application;

namespace HaloPixelLyric.Ui;

/// <summary>
/// 主题切换：浅色 / 深色 / 跟随系统。
/// 主题资源是独立的 ResourceDictionary，切换时整体替换，DynamicResource 自动重绘。
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _light;
    private static ResourceDictionary? _dark;

    public static bool IsDark { get; private set; }

    /// <summary>把设置里的主题值解析成实际明暗（auto 跟随 Windows 应用主题）。</summary>
    public static bool Resolve(string? theme) => theme switch
    {
        "light" => false,
        "dark" => true,
        _ => SystemPrefersDark(),
    };

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(IsThemeDictionary);
        var target = dark ? _dark ??= Load("Dark") : _light ??= Load("Light");
        if (ReferenceEquals(existing, target)) return;

        var index = existing is null ? 0 : dictionaries.IndexOf(existing);
        if (existing is not null) dictionaries.Remove(existing);
        dictionaries.Insert(index, target);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString ?? "";
        return source.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
            || source.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceDictionary Load(string name) => new()
    {
        Source = new Uri($"pack://application:,,,/HaloPixelLyric;component/Themes/{name}.xaml", UriKind.Absolute),
    };

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
