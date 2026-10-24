using Android.Content;
using BTUnlock.Android.Ble;
using BTUnlock.Android.Prefs;

namespace BTUnlock.Android.Boot;

/// <summary>
/// 开机自启。Android 12+ 从 BOOT_COMPLETED 拉起 connectedDevice 前台服务属于
/// 允许的例外；真正拦截它的是 MIUI 的"自启动"授权——所以 App 内有引导跳转，
/// 见 docs/03-miui-keepalive.md。
/// </summary>
[BroadcastReceiver(Name = "com.btunlock.android.BootReceiver", Exported = true, Enabled = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted) return;
        if (!DeviceIdentity.GetAutoService(context)) return; // 用户从未开启过服务则不自启

        try
        {
            context.StartForegroundService(new Intent(context, typeof(BleAdvertiserService)));
        }
        catch (Exception)
        {
            // MIUI 未授权自启动时被系统拦截，属预期；用户打开 App 后手动开启即可
        }
    }
}
