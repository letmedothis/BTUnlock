using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using BTUnlock.Android.Prefs;
using BTUnlock.Protocol;
using Java.Util;

namespace BTUnlock.Android.Ble;

/// <summary>
/// BLE 广播控制器：构造协议负载、启停广播，并带看门狗——
/// 广播被系统静默停止（MIUI 常见）或协议栈僵死后自动恢复。
/// </summary>
public sealed class AdvertiseController
{
    private const long WatchdogIntervalMs = 60_000;
    private const long HardRestartMs = 15 * 60_000;

    private readonly Handler _watchdog = new(Looper.MainLooper!);
    private readonly Java.Lang.Runnable _watchdogRunnable;
    private BluetoothLeAdvertiser? _advertiser;
    private AdvertiseCallbackImpl? _callback;
    private bool _watchdogRunning;
    private DateTimeOffset _lastRestartUtc = DateTimeOffset.MinValue;

    public AdvertiseController() => _watchdogRunnable = new Java.Lang.Runnable(WatchdogTick);

    public bool IsAdvertising { get; private set; }
    public int RestartCount { get; private set; }

    /// <summary>供界面读取的运行状态（跨进程内共享，简单用静态字段）。</summary>
    public static volatile string SharedStatus = "未运行";

    public void EnsureStarted()
    {
        if (!_watchdogRunning)
        {
            _watchdogRunning = true;
            _watchdog.PostDelayed(_watchdogRunnable, WatchdogIntervalMs);
        }
        TryStart();
    }

    public void Stop()
    {
        _watchdogRunning = false;
        QuietStop();
        RestartCount = 0;
        SharedStatus = "未运行";
    }

    private void TryStart()
    {
        var adapter = ((BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService))?.Adapter;
        if (adapter is null || !adapter.IsEnabled)
        {
            IsAdvertising = false;
            SharedStatus = "蓝牙未开启";
            return;
        }
        var advertiser = adapter.BluetoothLeAdvertiser;
        if (advertiser is null)
        {
            IsAdvertising = false;
            SharedStatus = "设备不支持 BLE 广播";
            return;
        }

        QuietStop();
        _advertiser = advertiser;
        _callback = new AdvertiseCallbackImpl
        {
            Started = _ =>
            {
                IsAdvertising = true;
                SharedStatus = $"广播中（看门狗已重启 {RestartCount} 次）";
            },
            Failed = code =>
            {
                IsAdvertising = false;
                SharedStatus = $"广播失败：{code}";
            },
        };

        var settings = new AdvertiseSettings.Builder()
            .SetAdvertiseMode(AdvertiseSettings.AdvertiseModeBalanced) // ~250ms 间隔，响应与功耗的折中
            .SetTxPowerLevel(AdvertiseSettings.TxPowerHigh)
            .SetConnectable(false)
            .Build();

        var deviceId = DeviceIdentity.GetOrCreate(Application.Context);
        var data = new AdvertiseData.Builder()
            .AddServiceUuid(new ParcelUuid(UUID.FromString(ProtocolConstants.ServiceUuid.ToString())!))
            .AddManufacturerData(ProtocolConstants.ManufacturerCompanyId, new AdvertPayload(deviceId).ToBytes())
            .Build();

        advertiser.StartAdvertising(settings, data, _callback);
        _lastRestartUtc = DateTimeOffset.UtcNow;
    }

    private void QuietStop()
    {
        try
        {
            if (_advertiser is not null && _callback is not null)
                _advertiser.StopAdvertising(_callback);
        }
        catch (Exception)
        {
            // 蓝牙已被关闭等场景，忽略
        }
        IsAdvertising = false;
        _advertiser = null;
        _callback = null;
    }

    /// <summary>每 60 秒自检：广播挂了就重启；每 15 分钟无条件重启，防协议栈静默僵死。</summary>
    private void WatchdogTick()
    {
        if (!_watchdogRunning) return;

        if (BleAdvertiserService.IsRunning &&
            (!IsAdvertising || DateTimeOffset.UtcNow - _lastRestartUtc >= TimeSpan.FromMilliseconds(HardRestartMs)))
        {
            RestartCount++;
            TryStart();
        }
        _watchdog.PostDelayed(_watchdogRunnable, WatchdogIntervalMs);
    }

    private sealed class AdvertiseCallbackImpl : AdvertiseCallback
    {
        public Action<AdvertiseSettings>? Started { get; init; }
        public Action<AdvertiseFailure>? Failed { get; init; }

        public override void OnStartSuccess(AdvertiseSettings? settingsInEffect) => Started?.Invoke(settingsInEffect!);

        public override void OnStartFailure(AdvertiseFailure errorCode) => Failed?.Invoke(errorCode);
    }
}
