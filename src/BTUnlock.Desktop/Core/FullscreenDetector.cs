using System.Runtime.InteropServices;

namespace BTUnlock.Desktop.Core;

/// <summary>前台全屏检测（游戏/全屏播放时挂起自动锁屏，防误锁）。</summary>
internal static class FullscreenDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    public static bool IsForegroundFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return false;
            if (!GetWindowRect(hwnd, out var r)) return false;
            var bounds = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            return r.Left <= bounds.Left && r.Top <= bounds.Top &&
                   r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
