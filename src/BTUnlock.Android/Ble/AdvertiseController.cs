using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using BTUnlock.Android.Prefs;
using BTUnlock.Protocol;

namespace BTUnlock.Android.Ble;

/// <summary>
/// BLE 广播控制器：构造协议负载、启停广播，并带看门狗——
/// 广播被系统静默停止（MIUI 常见）或协议栈僵死后自动恢复。
/// </summary>
public sealed class AdvertiseController
{
    private const long WatchdogIntervalMs = 60_000;
    private const long HardRestartMs = 15 * 60_000;
    private const long RefreshIntervalMs = 10_000;

    private readonly Handler _watchdog = new(Looper.MainLooper!);
    private readonly Java.Lang.Runnable _watchdogRunnable;
    private readonly Java.Lang.Runnable _refreshRunnable;
    private BluetoothLeAdvertiser? _advertiser;
    private AdvertiseCallbackImpl? _callback;
    private bool _watchdogRunning;
    private bool _refreshRunning;
    private DateTimeOffset _lastRestartUtc = DateTimeOffset.MinValue;

    public AdvertiseController()
    {
        _watchdogRunnable = new Java.Lang.Runnable(WatchdogTick);
        _refreshRunnable = new Java.Lang.Runnable(RefreshTick);
    }

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
        if (!_refreshRunning)
        {
            _refreshRunning = true;
            _watchdog.PostDelayed(_refreshRunnable, RefreshIntervalMs);
        }
        TryStart();
    }

    public void Stop()
    {
        _watchdogRunning = false;
        _refreshRunning = false;
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
            .SetAdvertiseMode(AdvertiseMode.Balanced) // ~250ms 间隔，响应与功耗的折中
            .SetTxPowerLevel(AdvertiseTx.PowerHigh)
            .SetConnectable(false)
            .Build();

        var deviceId = DeviceIdentity.GetOrCreate(Application.Context);
        // v2 负载：ID + Unix 秒 + HMAC 签名（19 字节）。传统广播包上限 31 字节，
        // 不放 128 位 Service UUID；PC 端按 CompanyId+魔数识别并校验签名（抗重放）。
        var counter = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var secret = DeviceIdentity.SecretBytes(Application.Context);
        var mac = ComputeMac(secret, deviceId, counter);
        var data = new AdvertiseData.Builder()
            .AddManufacturerData(ProtocolConstants.ManufacturerCompanyId,
                AdvertPayload.ToBytesV2(deviceId, counter, mac))
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

    /// <summary>
    /// 每 10 秒带新时间戳重建广播（令牌刷新）：旧 API 广播内容固定，
    /// 只能 stop/start 换内容。PC 端接受 ±45 秒新鲜度，短暂间隙无影响。
    /// </summary>
    private void RefreshTick()
    {
        if (!_refreshRunning) return;
        if (BleAdvertiserService.IsRunning && IsAdvertising)
        {
            QuietStop();
            TryStart();
        }
        _watchdog.PostDelayed(_refreshRunnable, RefreshIntervalMs);
    }

    /// <summary>与 PC 端 TokenMath.ComputeMac 算法严格一致：
    /// HMAC-SHA256(secret, deviceId 大端 8B ‖ counter 小端 4B) 前 4 字节按小端读 uint。</summary>
    private static uint ComputeMac(byte[] secret, ulong deviceId, uint counter)
    {
        var mac = Javax.Crypto.Mac.GetInstance("HmacSHA256")!;
        mac.Init(new Javax.Crypto.Spec.SecretKeySpec(secret, "HmacSHA256"));
        var message = new byte[12];
        for (var i = 0; i < 8; i++)
            message[i] = (byte)(deviceId >> ((7 - i) * 8));
        message[8] = (byte)counter;
        message[9] = (byte)(counter >> 8);
        message[10] = (byte)(counter >> 16);
        message[11] = (byte)(counter >> 24);
        var digest = mac.DoFinal(message);
        return (uint)(digest[0] | (digest[1] << 8) | (digest[2] << 16) | (digest[3] << 24));
    }

    private sealed class AdvertiseCallbackImpl : AdvertiseCallback
    {
        public Action<AdvertiseSettings>? Started { get; init; }
        public Action<AdvertiseFailure>? Failed { get; init; }

        public override void OnStartSuccess(AdvertiseSettings? settingsInEffect) => Started?.Invoke(settingsInEffect!);

        public override void OnStartFailure(AdvertiseFailure errorCode) => Failed?.Invoke(errorCode);
    }
}
