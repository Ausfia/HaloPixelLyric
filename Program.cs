using HaloPixelLyric;
using HaloPixelLyric.Hid;
using HaloPixelLyric.Hid.Models;

// 命令行辅助模式（GUI 之外保留）
// --clock      仅把像素屏恢复为时钟模式后退出
// --calibrate  屏宽标定（汉字 4~14；--calibrate en 半角 20~40）
if (args.Length > 0 && args[0] == "--clock")
{
    var clockDevice = new HaloPixelDevice();
    if (clockDevice.Initialize()) { clockDevice.SetUIModel(HaloPixelUIModel.Clock); Console.WriteLine("[ok] 像素屏已恢复时钟模式"); }
    else Console.WriteLine("[错误] 未找到像素屏设备");
    return 0;
}

if (args.Length > 0 && args[0] == "--calibrate")
{
    var english = args.Length > 1 && args[1].Equals("en", StringComparison.OrdinalIgnoreCase);
    var cal = new HaloPixelDevice();
    if (!cal.Initialize()) { Console.WriteLine("[错误] 未找到像素屏设备"); return 1; }
    cal.SetTextLayout(HaloPixelTextLayout.Center);
    if (english)
    {
        Console.WriteLine("英文屏宽标定：依次显示 20~40 个小写字母（步进2），每个停 3.5 秒。");
        for (var n = 20; n <= 40; n += 2)
        {
            try { cal.ShowText(new string('n', n)); }
            catch (Exception ex) { Console.WriteLine($"[warn] 发送失败: {ex.Message}"); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 屏幕上现在是 {n} 个半角字符");
            Thread.Sleep(3500);
        }
    }
    else
    {
        Console.WriteLine("屏宽标定：依次显示 4~14 个汉字（居中静态），每个停 3.5 秒。");
        for (var n = 4; n <= 14; n++)
        {
            try { cal.ShowText(new string('测', n)); }
            catch (Exception ex) { Console.WriteLine($"[warn] 发送失败: {ex.Message}"); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 屏幕上现在是 {n} 个汉字");
            Thread.Sleep(3500);
        }
    }
    Console.WriteLine("标定结束。");
    return 0;
}

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());
return 0;
