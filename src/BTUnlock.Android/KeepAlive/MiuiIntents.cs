using Android.Content;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace BTUnlock.Android.KeepAlive;

/// <summary>
/// 小米/红米（MIUI/HyperOS）保活相关设置页的跳转 Intent 与降级链。
/// 这些入口是社区验证的私有能力，随 MIUI 版本可能变化——统一 try/catch，
/// 失败时降级到系统应用详情页，由用户手动找到对应开关。
/// 详见 docs/03-miui-keepalive.md。
/// </summary>
public static class MiuiIntents
{
    /// <summary>① 自启动管理（安全中心）。</summary>
    public static void OpenAutoStart(Context context) =>
        TryStart(context, "com.miui.securitycenter",
            "com.miui.permcenter.autostart.AutoStartManagementActivity", "自启动管理");

    /// <summary>② 省电策略（电量和性能 → 应用智能省电 → 无限制）。</summary>
    public static void OpenPowerKeeper(Context context) =>
        TryStart(context, "com.miui.powerkeeper",
            "com.miui.powerkeeper.ui.HiddenAppsConfigActivity", "省电策略",
            intent => intent
                .PutExtra("package_name", context.PackageName)
                .PutExtra("package_label", "BTUnlock"));

    /// <summary>③ 系统级电池优化白名单。</summary>
    public static void OpenBatteryOptimization(Context context)
    {
        try
        {
            context.StartActivity(new Intent(
                Settings.ActionRequestIgnoreBatteryOptimizations,
                Uri.FromParts("package", context.PackageName, null)));
        }
        catch (Exception)
        {
            OpenAppDetails(context);
        }
    }

    /// <summary>④ 通知权限（Android 13+ 前台服务通知需要）。</summary>
    public static void OpenNotificationSettings(Context context)
    {
        try
        {
            context.StartActivity(new Intent(Settings.ActionAppNotificationSettings)
                .PutExtra(Settings.ExtraAppPackage, context.PackageName));
        }
        catch (Exception)
        {
            OpenAppDetails(context);
        }
    }

    public static void OpenAppDetails(Context context) =>
        context.StartActivity(new Intent(
            Settings.ActionApplicationDetailsSettings,
            Uri.FromParts("package", context.PackageName, null)));

    private static void TryStart(Context context, string pkg, string cls, string what,
        Func<Intent, Intent>? decorate = null)
    {
        try
        {
            var intent = new Intent().SetComponent(new ComponentName(pkg, cls))
                .AddFlags(ActivityFlags.NewTask);
            if (decorate is not null) intent = decorate(intent);
            context.StartActivity(intent);
        }
        catch (Exception)
        {
            OpenAppDetails(context);
        }
    }
}
