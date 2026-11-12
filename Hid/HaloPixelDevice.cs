using System.Threading;
using HaloPixelLyric.Hid.Models;
using HidSharp;

namespace HaloPixelLyric.Hid;

public partial class HaloPixelDevice
{
    public HidDevice? CurrentDevice { get; set; }

    /// <summary>设备 PID（旧款 0xA106，2026 强化版/PRO 0xA160），用于日志与支持排查。</summary>
    public int ProductId => CurrentDevice?.ProductID ?? 0;

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
    /// 2026 强化版（PRO）必须先进入该模式才渲染文字，否则屏幕停在自己的场景（律动波浪）；
    /// 旧款固件应答但不切换，无副作用。30 秒节流，避免每行歌词重复发送。
    /// </summary>
    private void EnsureMusicMode(HidStream stream)
    {
        if ((DateTime.UtcNow - _lastMusicModeUtc).TotalSeconds < 30)
            return;
        foreach (var deviceType in new byte[] { 0xEC, 0xED })
        {
            stream.Write(HidPacketBuilder.BuildPixelModeSet(
                deviceType, 0x02, HidPacketBuilder.MusicModeIndex, 0x00, 0xFF, 0xFF));
            Thread.Sleep(5);
        }
        _lastMusicModeUtc = DateTime.UtcNow;
    }

    public void ShowText(string text)
    {
        using var stream = CurrentDevice?.Open();
        if (stream != null)
            EnsureMusicMode(stream);
        // 四帧覆盖策略（依据 TempoHub 1.3.3 官方协议逆向 + 社区实机验证）：
        // 1. 旧 EC 帧（本机旧款实机验证有效）
        // 2. 官方 EC 歌词通道帧 source_id=0x0E（TempoHub 推 PixelBar 歌词的确切发法）
        // 3. 社区 ED 帧 source_id=0x00（Seraph310 实机验证格式）
        // 4. 官方 ED 歌词通道帧 source_id=0x0E（新设备线格式 + 官方载荷结构）
        // 不认识的帧被固件静默忽略，多帧无副作用；若设备启用加密（encryption≠0）则全部无效，
        // 需用诊断包 PixelBarProbe 的 CM_65 基础查询确认。
        var frames = new List<byte[]>
        {
            HidPacketBuilder.BuildText(text),
            HidPacketBuilder.BuildLyricTextOfficial(0xEC, text),
            HidPacketBuilder.BuildTextEd(text),
            HidPacketBuilder.BuildLyricTextOfficial(0xED, text),
        };
        foreach (var frame in frames)
        {
            stream?.Write(frame);
            Thread.Sleep(5); // HID 写间隔，避免连续 64B 报文挤占设备缓冲
        }
        stream?.Close();
    }

    public void SetTextLayout(HaloPixelTextLayout layout)
    {
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
