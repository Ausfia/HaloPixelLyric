using System.Windows;
using HaloPixelLyric.Hid;
using HaloPixelLyric.Hid.Models;
using HaloPixelLyric.Ui;
using Application = System.Windows.Application;

namespace HaloPixelLyric;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ===== 命令行辅助模式（GUI 之外保留） =====
        // --clock      仅把像素屏恢复为时钟模式后退出
        // --calibrate  屏宽标定（汉字 4~14；--calibrate en 半角 20~40）
        if (e.Args.Length > 0 && e.Args[0] == "--clock")
        {
            var device = new HaloPixelDevice();
            if (device.Initialize())
            {
                device.SetUIModel(HaloPixelUIModel.Clock);
                Console.WriteLine("[ok] 像素屏已恢复时钟模式");
            }
            else
            {
                Console.WriteLine("[错误] 未找到像素屏设备");
            }
            Shutdown(0);
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--calibrate")
        {
            RunCalibration(e.Args.Length > 1 && e.Args[1].Equals("en", StringComparison.OrdinalIgnoreCase));
            Shutdown(0);
            return;
        }

        var settings = DisplaySettings.Load();
        var dark = ThemeManager.Resolve(settings.Theme);
        ThemeManager.Apply(dark);

        var window = new MainWindow(settings, dark);
        MainWindow = window;
        window.Show();
    }

    private static void RunCalibration(bool english)
    {
        var device = new HaloPixelDevice();
        if (!device.Initialize())
        {
            Console.WriteLine("[错误] 未找到像素屏设备");
            return;
        }

        device.SetTextLayout(HaloPixelTextLayout.Center);
        if (english)
        {
            Console.WriteLine("英文屏宽标定：依次显示 20~40 个小写字母（步进2），每个停 3.5 秒。");
            for (var n = 20; n <= 40; n += 2)
            {
                try { device.ShowText(new string('n', n)); }
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
                try { device.ShowText(new string('测', n)); }
                catch (Exception ex) { Console.WriteLine($"[warn] 发送失败: {ex.Message}"); }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 屏幕上现在是 {n} 个汉字");
                Thread.Sleep(3500);
            }
        }

        Console.WriteLine("标定结束。");
    }
}
