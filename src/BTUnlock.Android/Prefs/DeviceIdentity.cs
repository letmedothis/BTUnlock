using System.Globalization;
using System.Security.Cryptography;
using Android.Content;
using BTUnlock.Protocol;

namespace BTUnlock.Android.Prefs;

/// <summary>设备身份：首次启动生成 8 字节随机 ID 并持久化，之后跨重启保持不变。</summary>
public static class DeviceIdentity
{
    private const string PrefsName = "btunlock";
    private const string KeyDeviceId = "device_id";
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

    /// <summary>用户是否开启过广播服务（决定开机自启是否拉起）。</summary>
    public static bool GetAutoService(Context context) =>
        context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetBoolean(KeyAutoService, false);

    public static void SetAutoService(Context context, bool value) =>
        context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutBoolean(KeyAutoService, value).Apply();
}
