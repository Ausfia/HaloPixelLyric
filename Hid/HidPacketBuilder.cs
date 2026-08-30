using HaloPixelLyric.Hid.Models;
using System.Text;

namespace HaloPixelLyric.Hid;

public class HidPacketBuilder
{
    /// <summary>
    /// 文本头
    /// </summary>
    public static readonly byte[] TextHeader =
    [
        0x2E, 0xAA, 0xEC, 0xE8, 0x00
    ];

    /// <summary>
    /// 布局头
    /// </summary>
    public static readonly byte[] LayoutHeader =
    [
        0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xf0, 0xb4, 0xc8, 0x00, 0x02, 0x00
    ];

    /// <summary>
    /// 固定包长度（64 bytes）
    /// </summary>
    private const int FixedPacketLength = 64;

    /// <summary>
    /// 构造 HID 协议包
    /// </summary>
    public static byte[] BuildText(string text)
    {
        if (text.Length > 50)
            text = text.Substring(0, 50);
        var textBytes = Encoding.UTF8.GetBytes(text);
        var textLen = (byte)textBytes.Length;
        // 有效载荷长度 = TextLen(1) + Text(N) + Checksum(1)
        var totalLen = (ushort)(1 + textLen + 1);

        var list = TextHeader.ToList();

        // TotalLen (2 bytes, little-endian)
        list.AddRange(BitConverter.GetBytes(totalLen));

        // TextLen (1 byte)
        list.Add(textLen);

        // Text bytes
        list.AddRange(textBytes);

        // Checksum (1 byte)
        list.Add(byte.Parse(Checksum(textBytes).ToString()));

        return Build(list);
    }

    /// <summary>
    /// 转换布局枚举到对应的 HID 包字节数组
    /// </summary>
    public static (byte R, byte G, byte B) CurrentColor { get; set; } = (0xf0, 0xb4, 0xc8);

    /// <summary>
    /// 转换布局枚举到对应的 HID 包字节数组
    /// </summary>
    public static byte[] ConvertLayout(HaloPixelTextLayout haloPixelTextLayout)
    {
        byte[] payload = haloPixelTextLayout switch
        {
            HaloPixelTextLayout.Left => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x00, 0xff],
            HaloPixelTextLayout.Center => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x01, 0xff],
            HaloPixelTextLayout.Right => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x02, 0xff],
            HaloPixelTextLayout.Stretch => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x03, 0xff],
            HaloPixelTextLayout.ScrollLeftToRight => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x01, 0x00, 0xff],
            HaloPixelTextLayout.ScrollRightToLeft => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x01, 0x01, 0xff],
            _ => [],
        };

        if (payload.Length == 0) return [];

        var list = new List<byte> { 0x2e, 0xaa, 0xec, 0xef, 0x00, 0x09 };
        list.AddRange(payload);

        int acc = 124;
        foreach (var b in payload)
        {
            acc += b + 2;
        }
        list.Add((byte)(acc % 256));
        list.Add(0x00);

        return Build(list);
    }

    public static byte[] ConvertUIModel(HaloPixelUIModel haloPixelUIModel)
    {
        byte[] payload = haloPixelUIModel switch
        {
            HaloPixelUIModel.Clock => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x00, 0xff, 0xff],
            HaloPixelUIModel.Game => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x01, 0xff, 0xff],
            HaloPixelUIModel.Work => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x02, 0xff, 0xff],
            HaloPixelUIModel.Read => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x03, 0xff, 0xff],
            HaloPixelUIModel.Cats => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x04, 0xff, 0xff],
            HaloPixelUIModel.Dogs => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x05, 0xff, 0xff],
            HaloPixelUIModel.Memes => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x06, 0xff, 0xff],
            HaloPixelUIModel.Cyber => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x07, 0xff, 0xff],
            HaloPixelUIModel.Waves => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x08, 0xff, 0xff],
            _ => [],
        };

        if (payload.Length == 0) return [];

        var list = new List<byte> { 0x2e, 0xaa, 0xec, 0xef, 0x00, 0x09 };
        list.AddRange(payload);

        int acc = 124;
        foreach (var b in payload)
        {
            acc += b + 2;
        }
        list.Add((byte)(acc % 256));
        list.Add(0x00);

        return Build(list);
    }

    /// <summary>
    /// 构造 HID 协议包
    /// </summary>
    public static byte[] Build(byte[] bytes) => Build(bytes.ToList());

    /// <summary>
    /// 构造 HID 协议包
    /// </summary>
    public static byte[] Build(List<byte> bytes)
    {
        // Padding 补 0 到固定长度（64 字节）
        while (bytes.Count < FixedPacketLength)
            bytes.Add(0x00);

        return [.. bytes];
    }

    /// <summary>
    /// 校验算法
    /// </summary>
    public static int Checksum(byte[] textBytes)
    {
        var acc = 128;
        foreach (var ch in textBytes.Select(v => (char)v))
        {
            acc += ch + 2;
        }
        return acc % 256;
    }
}
