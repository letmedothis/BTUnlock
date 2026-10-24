using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Android.Content;
using BTUnlock.Protocol;

namespace BTUnlock.Android.Prefs;

/// <summary>
/// 设备身份：首次启动生成 8 字节随机 ID 与 16 字节滚动令牌密钥，持久化不变。
/// 密钥需抄录到 PC 端「滚动令牌」输入框（清除数据/重装会重新生成，PC 端需更新）。
/// </summary>
public static class DeviceIdentity
{
    private const string PrefsName = "btunlock";
    private const string KeyDeviceId = "device_id";
    private const string KeySecret = "device_secret";
    private const string KeyAutoService = "auto_service";

    public static ulong GetOrCreate(Context context)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        var hex = prefs.GetString(KeyDeviceId, null);
        if (hex is { Length: 16 } &&
            ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return id;

        ulong newId = 0;
        foreach (var b in RandomNumberGenerator.GetBytes(8))
            newId = (newId << 8) | b;
        prefs.Edit()!.PutString(KeyDeviceId, AdvertPayload.ToFullId(newId)).Apply();
        return newId;
    }

    /// <summary>滚动令牌密钥（16 字节 → 32 位十六进制小写），广播签名用，PC 端粘贴同值。</summary>
    public static string GetOrCreateSecretHex(Context context)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        var hex = prefs.GetString(KeySecret, null);
        if (hex is { Length: 32 }) return hex;

        var sb = new StringBuilder(32);
        foreach (var b in RandomNumberGenerator.GetBytes(16))
            sb.Append(b.ToString("x2"));
        hex = sb.ToString();
        prefs.Edit()!.PutString(KeySecret, hex).Apply();
        return hex;
    }

    public static byte[] SecretBytes(Context context)
    {
        var hex = GetOrCreateSecretHex(context);
        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber);
        return bytes;
    }

    /// <summary>用户是否开启过广播服务（决定开机自启是否拉起）。</summary>
    public static bool GetAutoService(Context context) =>
        context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetBoolean(KeyAutoService, false);

    public static void SetAutoService(Context context, bool value) =>
        context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutBoolean(KeyAutoService, value).Apply();
}
