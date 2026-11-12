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

    /// <summary>
    /// 构造 ED 变体文字包（2026 新固件协议：设备类型字节 0xED，长度大端，校验和 = 从 AA 起求和）。
    /// 旧固件对 ED 帧静默忽略（实测），因此与 EC 帧同时发送可兼容两代固件。
    /// 参考实机验证记录：2E AA ED E8 | lenHi lenLo | 00 textLen utf8... | Σ(pkt[1:])&0xFF
    /// </summary>
    public static byte[] BuildTextEd(string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        // ED 规范按 UTF-8 字节数限 48（新固件文档：≤48 字节）
        if (textBytes.Length > 48)
        {
            var cut = new byte[48];
            Array.Copy(textBytes, cut, 48);
            textBytes = cut;
        }
        var textLen = (byte)textBytes.Length;
        var payloadLen = (ushort)(2 + textLen + 1); // 00 + textLen 字节 + Checksum

        var list = new List<byte> { 0x2E, 0xAA, 0xED, 0xE8 };
        // 长度字段：高字节在前（大端），与 BitConverter 小端相反
        list.Add((byte)(payloadLen >> 8));
        list.Add((byte)(payloadLen & 0xFF));
        list.Add(0x00);
        list.Add(textLen);
        list.AddRange(textBytes);
        // 校验和：从 AA（index 1）到文本末尾求和 mod 256
        var acc = 0;
        for (var i = 1; i < list.Count; i++)
            acc += list[i];
        list.Add((byte)(acc % 256));

        return Build(list);
    }

    /// <summary>
    /// 构造 ED 变体布局包（与 ConvertLayout 同 payload，ED 帧头/长度/校验规范）。
    /// </summary>
    public static byte[] ConvertLayoutEd(HaloPixelTextLayout haloPixelTextLayout)
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

        return BuildEdFrame(0xEF, payload);
    }

    /// <summary>
    /// 按 ED 规范组帧：2E AA ED cmd | lenHi lenLo | payload | Σ(pkt[1:])&0xFF
    /// </summary>
    private static byte[] BuildEdFrame(byte cmd, byte[] payload)
    {
        var totalLen = (ushort)(payload.Length + 1);
        var list = new List<byte> { 0x2E, 0xAA, 0xED, cmd };
        list.Add((byte)(totalLen >> 8));
        list.Add((byte)(totalLen & 0xFF));
        list.AddRange(payload);
        var acc = 0;
        for (var i = 1; i < list.Count; i++)
            acc += list[i];
        list.Add((byte)(acc % 256));
        return Build(list);
    }

    /// <summary>
    /// TempoHub 1.3.3 官方通用组帧（逆向自 EDIFIER TempoHub.exe 内 driver/edifier_driver.py）：
    /// COMMON=0x2E, HEADER=0xAA, DEVICE_TYPES=[EC,ED]，帧 = [2E AA type cmd] + 大端长度(payload 长度，不含 CRC) + payload + Σ(从 AA 起全部字节)&0xFF。
    /// 官方软件对 EC/ED 两代设备统一使用此组帧。
    /// </summary>
    public static byte[] BuildOfficialFrame(byte deviceType, byte cmd, byte[] payload)
    {
        var list = new List<byte> { 0x2E, 0xAA, deviceType, cmd };
        list.Add((byte)(payload.Length >> 8));
        list.Add((byte)(payload.Length & 0xFF));
        list.AddRange(payload);
        var acc = 0;
        for (var i = 1; i < list.Count; i++)
            acc += list[i];
        list.Add((byte)(acc & 0xFF));
        return Build(list);
    }

    /// <summary>
    /// 官方歌词文本帧（TempoHub hz_sound_box_qq_music_model._on_lyric_text_changed 逆向）：
    /// CM_E8(0xE8) 命令，payload = [source_id=0x0E, 全文总字节长, UTF-8 分片(≤55B/包)]。
    /// 日志原文："[QQMusic Model] CM_E8 发送歌词行, source_id=0x0E, 长度="。
    /// deviceType 传 0xEC（旧设备线格式）或 0xED（新设备线格式）。
    /// </summary>
    public const byte LyricSourceId = 0x0E;

    public static byte[] BuildLyricTextOfficial(byte deviceType, string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        if (textBytes.Length > 55)
        {
            var cut = new byte[55];
            Array.Copy(textBytes, cut, 55);
            textBytes = cut;
        }
        var payload = new byte[2 + textBytes.Length];
        payload[0] = LyricSourceId;
        payload[1] = (byte)textBytes.Length;
        Array.Copy(textBytes, 0, payload, 2, textBytes.Length);
        return BuildOfficialFrame(deviceType, 0xE8, payload);
    }

    /// <summary>
    /// TempoHub 基础信息查询帧（CM_65，官方代码中唯一免加密命令）。
    /// 响应 payload 含 [version, encryption, max_length]（driver/common.encrypt 逆向：
    /// encryption 0x10=XOR 0xA5、0x30=按位取反、0x31=XTEA、0=不加密）。
    /// </summary>
    public static byte[] BuildBaseQuery(byte deviceType) =>
        BuildOfficialFrame(deviceType, 0x65, []);

    /// <summary>
    /// 像素屏模式帧（TempoHub pixel_screen_set 逆向，命令 CM_EF）：
    /// payload = [action, r, g, b, mode_group_index, mode_index, opt0, opt1, opt2]。
    /// action=2 为开关/模式设置；mode_index=0 表示音乐/歌词（MUSIC），1 表示场景（SCENE）。
    /// 官方歌词路径（通用 sound_box 模块，2026 强化版 PRO 走此路径）在推文字前必须先发
    /// action=2 / mode_index=0 / opt0=0 的「音乐模式开启」帧，否则屏幕停在自己的场景（如律动波浪）。
    /// 旧款固件会应答该帧但不切换（其歌词模式走 ED 线 CM_ED02），因此对旧款无副作用。
    /// </summary>
    public const byte MusicModeIndex = 0x00;

    public static byte[] BuildPixelModeSet(byte deviceType, byte action, byte modeIndex,
        byte opt0, byte opt1, byte opt2)
    {
        var payload = new byte[]
        {
            action, CurrentColor.R, CurrentColor.G, CurrentColor.B,
            0x00, modeIndex, opt0, opt1, opt2,
        };
        return BuildOfficialFrame(deviceType, 0xEF, payload);
    }
}
