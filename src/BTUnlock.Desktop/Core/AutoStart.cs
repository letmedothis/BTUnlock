using Microsoft.Win32;

namespace BTUnlock.Desktop.Core;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BTUnlock";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>开机自启注册到 HKCU Run 键，静默参数 /tray 表示启动后直接缩到托盘。</summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey)!;
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\" /tray");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
