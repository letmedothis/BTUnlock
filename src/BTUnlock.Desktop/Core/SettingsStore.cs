using System.IO;
using System.Text.Json;
using BTUnlock.Protocol;

namespace BTUnlock.Desktop.Core;

public sealed class AppSettings
{
    public string? DeviceIdHex { get; set; }

    // RSSI 双阈值（dBm）：低于锁屏阈值 → 人在离开；高于解锁阈值 → 人已回来。
    // 中间为迟滞区间，两侧均不触发，避免在阈值附近来回抖动。
    public int LockThresholdDbm { get; set; } = -70;
    public int UnlockThresholdDbm { get; set; } = -60;
    public int LockDwellSeconds { get; set; } = 5;
    public int UnlockDwellSeconds { get; set; } = 3;
    public int MissingTimeoutSeconds { get; set; } = 12;
    public bool EnableAutoLock { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool EnableUnlock { get; set; }
    public string? TokenSecretHex { get; set; }

    public ulong? DeviceId => AdvertPayload.TryParseFullId(DeviceIdHex, out var id) ? id : null;
}

public sealed class SettingsStore
{
    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BTUnlock");

    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 配置损坏时回退默认值，不让工具因配置问题起不来
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
