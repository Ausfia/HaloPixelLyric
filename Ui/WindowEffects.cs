using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HaloPixelLyric.Ui;

/// <summary>Windows 11 原生窗口效果：圆角与深色边框。</summary>
public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));

        var darkMode = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
    }
}
