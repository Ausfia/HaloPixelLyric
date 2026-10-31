using System.IO;
using System.Text.Json;

namespace HaloPixelLyric;

public enum PlaybackSource
{
    Cdp = 0,
    Smtc = 1
}

/// <summary>可调参数（界面可编辑，持久化到 exe 同目录 settings.json）。</summary>
public sealed class DisplaySettings
{
    /// <summary>播放信息来源；旧设置未包含此项时继续使用 CDP。</summary>
    public PlaybackSource PlaybackSource { get; set; } = PlaybackSource.Cdp;

    /// <summary>网易云 CDP 调试端口，仅 CDP 模式使用。</summary>
    public int CdpPort { get; set; } = 9222;

    /// <summary>屏幕容量：全角单位（半角=0.5）。实测半角 32 字符 = 16 单位</summary>
    public double StaticMaxUnits { get; set; } = 16.0;

    /// <summary>静态句基础驻留毫秒</summary>
    public int BaseHoldMs { get; set; } = 1800;

    /// <summary>静态句每字符附加驻留毫秒</summary>
    public int PerCharHoldMs { get; set; } = 80;

    /// <summary>静态句驻留上限毫秒</summary>
    public int StaticMaxHoldMs { get; set; } = 3200;

    /// <summary>翻页提前量毫秒：抵消链路固有延迟（轮询+HID+固件动画），硬件动画时长有差异可调</summary>
    public int PageLeadMs { get; set; } = 280;

    /// <summary>预推提前量毫秒：提前选择下一句歌词，补偿设备显示延迟。</summary>
    public int PrePushLeadMs { get; set; } = 350;

    /// <summary>间奏钳制倍数：官方句长超过估算值 N 倍判为间奏拉伸</summary>
    public double InterludeFactor { get; set; } = 3.0;

    /// <summary>界面外观：auto（跟随系统）/ light / dark</summary>
    public string Theme { get; set; } = "auto";

    private static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static DisplaySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
