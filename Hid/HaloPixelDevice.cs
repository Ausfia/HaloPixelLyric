using HaloPixelLyric.Hid.Models;
using HidSharp;

namespace HaloPixelLyric.Hid;

public partial class HaloPixelDevice
{
    public HidDevice? CurrentDevice { get; set; }

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

    public void ShowText(string text)
    {
        using var stream = CurrentDevice?.Open();
        var data = HidPacketBuilder.BuildText(text);
        stream?.Write(data);
        stream?.Close();
    }

    public void SetTextLayout(HaloPixelTextLayout layout)
    {
        using var stream = CurrentDevice?.Open();
        var package = new byte[64];
        var data = HidPacketBuilder.Build(HidPacketBuilder.ConvertLayout(layout));
        Array.Copy(data, package, data.Length);
        stream?.Write(package);
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
        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            var name = string.Empty;
            try
            {
                name = device.GetFriendlyName();
            }
            catch { }
            if (device.GetMaxInputReportLength() == 64 && name.Contains("花再 Halo PixelBar"))
                yield return device;
        }
    }
}
