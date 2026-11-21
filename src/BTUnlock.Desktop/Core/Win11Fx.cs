using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// Windows 11 视觉效果：圆角（DWMWA_WINDOW_CORNER_PREFERENCE，22000+）与
/// 系统背板 Mica/Acrylic（DWMWA_SYSTEMBACKDROP_TYPE，22621+）。
/// 一切调用失败都静默降级：Win10 / 旧系统/ 关闭透明效果的机器保持普通外观。
/// </summary>
internal static class Win11Fx
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;     // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// 尝试为窗口启用 Mica（acrylic=true 时为亚克力）。
    /// 成功时把窗口背景设为透明以透出系统背板；失败返回 false，窗口外观不变。
    /// </summary>
    public static bool TryEnableBackdrop(Window window, bool acrylic = false)
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22621) return false;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            var backdrop = acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0)
                return false;
            window.Background = System.Windows.Media.Brushes.Transparent;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Win10 上补 DWM 圆角（Win11 由 Fluent 主题处理）。失败静默。</summary>
    public static void TryRoundCorners(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch (Exception)
        {
            // 圆角只是锦上添花
        }
    }
}
