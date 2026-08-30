namespace HaloPixelLyric.Hid.Models;

public enum HaloPixelTextLayout
{
    ScrollLeftToRight = 0,
    ScrollRightToLeft = 1,
    Left = 2,
    Right = 3,
    Center = 4,
    Stretch = 5
}

/// <summary>
/// 花再像素UI模型
/// </summary>
public enum HaloPixelUIModel
{
    Clock = 0,
    Game = 1,
    Work = 2,
    Read = 3,
    Cats = 4,
    Dogs = 5,
    Memes = 6,
    Cyber = 7,
    Waves = 8
}

/// <summary>
/// 字幕颜色配置。当前设备文本协议未公开颜色字段，先作为上层样式参数保留。
/// </summary>
public record HaloPixelColor(byte Red, byte Green, byte Blue)
{
    public static HaloPixelColor White { get; } = new(255, 255, 255);
    public static HaloPixelColor RedColor { get; } = new(255, 64, 64);
    public static HaloPixelColor GreenColor { get; } = new(64, 255, 128);
    public static HaloPixelColor BlueColor { get; } = new(64, 160, 255);

    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

public enum AmbientLightBrightness
{
    Low = 1,
    Medium = 2,
    High = 3
}

public enum AmbientLightEffect
{
    Breathing = 1,
    ColorTide = 2,
    Static = 3,
    Ripple = 4,
    Flow = 5,
    Dynamic = 6
}

public sealed class AmbientLightOptions
{
    public bool IsEnabled { get; set; } = true;

    public AmbientLightEffect Effect { get; set; } = AmbientLightEffect.Static;

    public HaloPixelColor Color { get; set; } = HaloPixelColor.White;

    public AmbientLightBrightness Brightness { get; set; } = AmbientLightBrightness.High;

    public byte Speed { get; set; } = 10;
}
