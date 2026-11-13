using System.Threading;
using HaloPixelLyric.Hid.Models;
using HidSharp;

namespace HaloPixelLyric.Hid;

public partial class HaloPixelDevice
{
    public HidDevice? CurrentDevice { get; set; }

    /// <summary>设备 PID（旧款 0xA106，强化版 PRO 0xA160，AI 版 0xA147），用于日志与支持排查。</summary>
    public int ProductId => CurrentDevice?.ProductID ?? 0;

    /// <summary>型号名（依据 TempoHub 官方枚举），用于日志显示。</summary>
    public string ModelName => ProductId switch
    {
        PidLegacy => "旧款",
        PidPro => "强化版PRO",
        PidAi => "AI版",
        _ => "未知型号",
    };

    /// <summary>
    /// 设备型号常量（TempoHub 1.3.3 官方 ProductOptionPid 枚举逆向）。
    /// </summary>
    public const int PidLegacy = 0xA106;  // HALO_PIXEL_BAR 旧款：文字帧直显，无需模式帧
    public const int PidPro = 0xA160;     // HALO_PIXEL_BAR_PRO 强化版：需 CM_EF 开歌词模式（实测有效）
    public const int PidAi = 0xA147;      // HALO_PIXEL_BAR_AI：模式帧语义不同（opt0=1+选项索引），
                                          // PRO 的模式帧会导致灭屏/固件复位（两位 AI 版用户实测），
                                          // 文字帧有效。只发 EC 官方文字帧，其余全部禁发。

    /// <summary>上次发送「音乐/歌词模式」帧的时间，用于 30 秒节流重发。</summary>
    private DateTime _lastMusicModeUtc = DateTime.MinValue;

    public bool Initialize()
    {
        if (GetPixelDevice().FirstOrDefault() is HidDevice device)
        {
            CurrentDevice = device;
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// 声明进入音乐/歌词模式（TempoHub pixel_screen_set，CM_EF）。
    /// 仅强化版 PRO（0xA160）需要且实测有效：payload opt0=0 为「歌词开」。
    /// 旧款（A106）应答但不执行，无需发送；AI 版（A147）该帧语义完全不同
    /// （opt0 应为 1 且后跟选项索引），PRO 帧会导致其灭屏/复位——严格禁发。
    /// </summary>
    private void EnsureMusicMode(HidStream stream)
    {
        if (ProductId != PidPro)
            return;
        if ((DateTime.UtcNow - _lastMusicModeUtc).TotalSeconds < 30)
            return;
        // 官方代码为 EC 线 CM_EF（TempoHub sound_box_pixel_music_lyric_model），只发 EC 版
        stream.Write(HidPacketBuilder.BuildPixelModeSet(
            0xEC, 0x02, HidPacketBuilder.MusicModeIndex, 0x00, 0xFF, 0xFF));
        Thread.Sleep(5);
        _lastMusicModeUtc = DateTime.UtcNow;
    }

    public void ShowText(string text)
    {
        using var stream = CurrentDevice?.Open();
        if (stream != null)
            EnsureMusicMode(stream);
        List<byte[]> frames;
        if (ProductId == PidAi)
        {
            // AI 版（A147）：官方 EC 歌词帧实测有效（用户反馈"官方打开后出现几秒歌词"即此帧）。
            // 其余帧（旧 EC/社区 ED/官方 ED/模式帧）语义不明且有灭屏/复位风险，禁发。
            frames = [HidPacketBuilder.BuildLyricTextOfficial(0xEC, text)];
        }
        else
        {
            // 旧款/PRO/未知型号：四帧覆盖策略（依据 TempoHub 1.3.3 官方协议逆向 + 社区实机验证）：
            // 1. 旧 EC 帧（本机旧款实机验证有效）
            // 2. 官方 EC 歌词通道帧 source_id=0x0E（TempoHub 推 PixelBar 歌词的确切发法）
            // 3. 社区 ED 帧 source_id=0x00（Seraph310 实机验证格式）
            // 4. 官方 ED 歌词通道帧 source_id=0x0E（新设备线格式 + 官方载荷结构）
            // 不认识的帧被固件静默忽略，多帧无副作用；若设备启用加密（encryption≠0）则全部无效，
            // 需用诊断包 PixelBarProbe 的 CM_65 基础查询确认。
            frames =
            [
                HidPacketBuilder.BuildText(text),
                HidPacketBuilder.BuildLyricTextOfficial(0xEC, text),
                HidPacketBuilder.BuildTextEd(text),
                HidPacketBuilder.BuildLyricTextOfficial(0xED, text),
            ];
        }
        foreach (var frame in frames)
        {
            stream?.Write(frame);
            Thread.Sleep(5); // HID 写间隔，避免连续 64B 报文挤占设备缓冲
        }
        stream?.Close();
    }

    public void SetTextLayout(HaloPixelTextLayout layout)
    {
        if (ProductId == PidAi)
            return; // AI 版布局帧语义不明（SPACE 类载荷），实测会造成异常，禁发
        using var stream = CurrentDevice?.Open();
        var package = new byte[64];
        var data = HidPacketBuilder.Build(HidPacketBuilder.ConvertLayout(layout));
        Array.Copy(data, package, data.Length);
        stream?.Write(package);

        var edData = HidPacketBuilder.ConvertLayoutEd(layout);
        if (edData.Length > 0)
        {
            var edPackage = new byte[64];
            Array.Copy(edData, edPackage, edData.Length);
            stream?.Write(edPackage);
        }
        stream?.Close();
    }

    public void SetUIModel(HaloPixelUIModel haloPixelUIModel)
    {
        if (ProductId == PidAi)
            return; // AI 版场景帧语义不同，禁发（其"开屏"行为待实测确认后再考虑利用）
        using var stream = CurrentDevice?.Open();
        var package = new byte[64];
        var data = HidPacketBuilder.Build(HidPacketBuilder.ConvertUIModel(haloPixelUIModel));
        Array.Copy(data, package, data.Length);
        stream?.Write(package);
        stream?.Close();
    }

    public static IEnumerable<HidDevice> GetPixelDevice()
    {
        var byName = new List<HidDevice>();
        var byVendor = new List<HidDevice>();
        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            // 漫步者/花再 USB VID（社区生态全部项目一致使用 0x2D99）
            if (device.VendorID != 0x2D99)
                continue;
            try
            {
                if (device.GetMaxInputReportLength() == 64)
                {
                    byVendor.Add(device);
                    if (device.GetFriendlyName().Contains("花再 Halo PixelBar"))
                        byName.Add(device);
                }
            }
            catch { }
        }
        // 优先名称匹配；名称变化（如新硬件型号）时回退到 VID+64字节报告匹配
        return byName.Count > 0 ? byName : byVendor;
    }
}
