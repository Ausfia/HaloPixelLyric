using System.IO;
using System.Text.Json;

namespace HaloPixelLyric;

/// <summary>可调参数（界面可编辑，持久化到 exe 同目录 settings.json）。</summary>
public sealed class DisplaySettings
{
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

    /// <summary>预推提前量毫秒：下一句第一页的独立提前量。锚点是 data-time 时间轴外推，
    /// 相比翻页多一层"编排时间 vs 实际演唱"的系统偏差，通常需比翻页提前量略大</summary>
    public int PrePushLeadMs { get; set; } = 350;

    /// <summary>间奏钳制倍数：官方句长超过估算值 N 倍判为间奏拉伸</summary>
    public double InterludeFactor { get; set; } = 3.0;

    /// <summary>界面外观：auto（跟随系统）/ light / dark</summary>
    public string Theme { get; set; } = "auto";

    /// <summary>CDP 调试端口</summary>
    public int CdpPort { get; set; } = 9222;

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
