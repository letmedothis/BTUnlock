using Android.App;
using Android.Content;
using Android.OS;

namespace BTUnlock.Android.Ble;

/// <summary>
/// 前台服务：常驻通知 + BLE 广播。foregroundServiceType 用 connectedDevice，
/// 这是 Android 12+ 从后台/开机拉起此类服务所要求的类型。
/// </summary>
// 注意：本项目命名空间 BTUnlock.Android 会遮蔽系统的 Android.*，
// 凡以 Android. 开头的引用必须写全 global:: 前缀。
[Service(Name = "com.btunlock.android.BleAdvertiserService", Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeConnectedDevice)]
public sealed class BleAdvertiserService : Service
{
    public const string ChannelId = "btunlock_advertise";
    public const int NotificationId = 0x4201;

    /// <summary>界面与 BootReceiver 读取的运行标志。</summary>
    public static volatile bool IsRunning;

    private AdvertiseController? _controller;

    public override void OnCreate()
    {
        base.OnCreate();
        _controller = new AdvertiseController();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartAsForeground();
        _controller?.EnsureStarted();
        return StartCommandResult.Sticky; // 被 MIUI 杀掉后系统会尝试重建
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        _controller?.Stop();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private void StartAsForeground()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        var channel = new NotificationChannel(ChannelId, "蓝牙广播", NotificationImportance.Low);
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("BTUnlock 正在广播")
            .SetContentText("保持本通知与后台运行，PC 端依赖此信号判断人走/人回")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo) // TODO: 替换正式图标
            .SetOngoing(true)
            .Build();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification,
                (int)global::Android.Content.PM.ForegroundService.TypeConnectedDevice);
        else
            StartForeground(NotificationId, notification);

        IsRunning = true;
    }
}
