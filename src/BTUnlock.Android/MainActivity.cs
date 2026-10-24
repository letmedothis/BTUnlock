using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using BTUnlock.Android.Ble;
using BTUnlock.Android.KeepAlive;
using BTUnlock.Android.Prefs;
using BTUnlock.Protocol;

namespace BTUnlock.Android;

[Activity(Name = "com.btunlock.android.MainActivity", Label = "BTUnlock", MainLauncher = true,
    Theme = "@android:style/Theme.Material.Light")]
public sealed class MainActivity : Activity
{
    private const int RequestCodePermissions = 0xB7;
    private const long RefreshMs = 2000;

    private const string PermAdvertise = "android.permission.BLUETOOTH_ADVERTISE";
    private const string PermConnect = "android.permission.BLUETOOTH_CONNECT";
    private const string PermPostNotifications = "android.permission.POST_NOTIFICATIONS";

    private Button _toggleButton = null!;
    private TextView _statusText = null!;
    private Handler? _refreshHandler;
    private Java.Lang.Runnable? _refreshRunnable;
    private bool _resumed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        var deviceId = DeviceIdentity.GetOrCreate(this);
        FindViewById<TextView>(Resource.Id.tvDeviceId)!.Text =
            $"设备 ID：{AdvertPayload.ToFullId(deviceId)}";
        FindViewById<TextView>(Resource.Id.tvPairCode)!.Text =
            $"配对码：{AdvertPayload.ToPairingCode(deviceId)}（绑定 PC 时与之比对）";
        FindViewById<TextView>(Resource.Id.tvSecret)!.Text =
            $"安全密钥（PC 端「滚动令牌」粘贴此项）：\n{DeviceIdentity.GetOrCreateSecretHex(this)}";

        _statusText = FindViewById<TextView>(Resource.Id.tvStatus)!;
        _toggleButton = FindViewById<Button>(Resource.Id.btnToggle)!;
        _toggleButton.Click += OnToggleClick;

        FindViewById<Button>(Resource.Id.btnAutostart)!.Click += (_, _) => MiuiIntents.OpenAutoStart(this);
        FindViewById<Button>(Resource.Id.btnPowerKeeper)!.Click += (_, _) => MiuiIntents.OpenPowerKeeper(this);
        FindViewById<Button>(Resource.Id.btnBattery)!.Click += (_, _) => MiuiIntents.OpenBatteryOptimization(this);
        FindViewById<Button>(Resource.Id.btnNotification)!.Click += (_, _) => MiuiIntents.OpenNotificationSettings(this);
        FindViewById<Button>(Resource.Id.btnLockRecents)!.Click += (_, _) => ShowRecentsLockHint();

        _refreshHandler = new Handler(Looper.MainLooper!);
        _refreshRunnable = new Java.Lang.Runnable(RefreshStatus);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        RefreshStatus();
        _refreshHandler!.PostDelayed(_refreshRunnable!, RefreshMs);
    }

    protected override void OnPause()
    {
        base.OnPause();
        _resumed = false;
        if (_refreshRunnable is not null) _refreshHandler?.RemoveCallbacks(_refreshRunnable);
    }

    private void OnToggleClick(object? sender, EventArgs e)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S && !HasRequiredPermissions())
        {
            RequestPermissions(new[] { PermAdvertise, PermConnect, PermPostNotifications },
                RequestCodePermissions);
            return;
        }

        var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
        if (adapter is null || !adapter.IsEnabled)
        {
            StartActivity(new Intent(BluetoothAdapter.ActionRequestEnable));
            return;
        }

        if (BleAdvertiserService.IsRunning)
        {
            StopService(new Intent(this, typeof(BleAdvertiserService)));
            DeviceIdentity.SetAutoService(this, false);
        }
        else
        {
            StartForegroundService(new Intent(this, typeof(BleAdvertiserService)));
            DeviceIdentity.SetAutoService(this, true);
        }
        RefreshStatus();
    }

    private bool HasRequiredPermissions() =>
        CheckSelfPermission(PermAdvertise) == Permission.Granted &&
        CheckSelfPermission(PermConnect) == Permission.Granted;

    /// <summary>界面自刷新（2 秒）：把服务的静态状态拉到 UI 上，并续订下一次刷新。</summary>
    private void RefreshStatus()
    {
        if (!_resumed) return;
        var running = BleAdvertiserService.IsRunning;
        var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter;
        if (adapter is null || !adapter.IsEnabled)
        {
            _statusText.Text = "○ 蓝牙未开启，请先打开蓝牙";
        }
        else if (running)
        {
            _statusText.Text = $"● {AdvertiseController.SharedStatus}";
        }
        else
        {
            _statusText.Text = $"○ {AdvertiseController.SharedStatus}";
        }
        _toggleButton.Text = running ? "停止广播" : "开始广播";
        _refreshHandler?.PostDelayed(_refreshRunnable!, RefreshMs);
    }

    private void ShowRecentsLockHint()
    {
        new AlertDialog.Builder(this)
            .SetTitle("⑤ 锁定最近任务（需手动，一次性）")
            .SetMessage("打开「最近任务」界面 → 找到 BTUnlock 卡片 → 长按（或下拉）选择「锁定」。\n" +
                        "锁定后卡片出现锁图标，一键清理后台时不会被杀。此操作系统不允许 App 代劳，只能手动完成。")
            .SetPositiveButton("知道了", (IDialogInterface, _) => { })
            .Show();
    }
}
