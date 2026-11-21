using System.IO;
using System.Text;
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

    /// <summary>TokenSecretHex 的 DPAPI 用户级密文（base64）。明文只留内存，不落盘（M6）。</summary>
    public string? TokenSecretProtected { get; set; }

    /// <summary>解锁失败熔断（M6.6）：连续 3 次失败后暂停自动解锁，重新录入密码恢复。</summary>
    public bool UnlockSuspended { get; set; }

    public ulong? DeviceId => AdvertPayload.TryParseFullId(DeviceIdHex, out var id) ? id : null;
}

public sealed class SettingsStore
{
    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BTUnlock");

    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (settings.TokenSecretProtected is { } b64)
                {
                    try
                    {
                        settings.TokenSecretHex = Encoding.UTF8.GetString(
                            MachineCrypto.UnprotectCurrentUser(Convert.FromBase64String(b64)));
                    }
                    catch (Exception)
                    {
                        settings.TokenSecretHex = null;
                        Log.Warn("滚动令牌密钥解密失败（DPAPI 用户上下文不匹配），已忽略");
                    }
                }
                // 旧版明文密钥迁移：转成 DPAPI 用户级密文并从 settings.json 移除明文
                if (settings.TokenSecretProtected is null && settings.TokenSecretHex is not null)
                    Save(settings);
                return settings;
            }
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
        // TokenSecretHex 只留在内存；落盘一律转 DPAPI 用户级密文（M6）
        var hex = settings.TokenSecretHex;
        settings.TokenSecretProtected = hex is null ? null
            : Convert.ToBase64String(MachineCrypto.ProtectCurrentUser(Encoding.UTF8.GetBytes(hex)));
        settings.TokenSecretHex = null;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        finally
        {
            settings.TokenSecretHex = hex;
        }
    }
}
