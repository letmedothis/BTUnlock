using System.Collections.Concurrent;
using BTUnlock.Protocol;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace BTUnlock.Desktop.Core;

public readonly record struct AdvertObservation(ulong DeviceId, short Rssi, DateTimeOffset Timestamp);

/// <summary>
/// 封装 WinRT 的 BLE 广播监听器：只放行本协议的 Manufacturer Data，
/// 抛出携带设备 ID 与 RSSI 的观测事件。事件在系统线程池线程上触发，
/// 订阅方不得长时间阻塞。
/// </summary>
public sealed class BleScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher = new()
    {
        ScanningMode = BluetoothLEScanningMode.Active,
    };

    private readonly ConcurrentDictionary<ulong, AdvertObservation> _seen = new();

    /// <summary>收到一帧本协议的合法广播（不区分设备，绑定过滤由上层做）。</summary>
    public event Action<AdvertObservation>? AdvertReceived;

    public string StatusText { get; private set; } = "未启动";

    public BleScanner()
    {
        _watcher.Received += OnReceived;
        _watcher.Stopped += (_, _) => StatusText = "已停止";
    }

    public void Start()
    {
        try
        {
            _watcher.Start();
            StatusText = "监听中";
        }
        catch (Exception ex)
        {
            // 典型原因：蓝牙适配器被禁用 / 无蓝牙模块
            StatusText = $"启动失败：{ex.Message}";
        }
    }

    /// <summary>扫描到过的本协议设备快照（绑定界面用），按最近出现排序。</summary>
    public IReadOnlyList<AdvertObservation> SnapshotSeen() =>
        _seen.Values.OrderByDescending(o => o.Timestamp).ToList();

    public void ClearSeen() => _seen.Clear();

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var manufacturerData = args.Advertisement?.ManufacturerData;
        if (manufacturerData is null || manufacturerData.Count == 0) return;

        foreach (var md in manufacturerData)
        {
            if (md.CompanyId != ProtocolConstants.ManufacturerCompanyId) continue;

            var length = checked((int)md.Data.Length);
            if (length < AdvertPayload.Length) continue;

            var bytes = new byte[length];
            DataReader.FromBuffer(md.Data).ReadBytes(bytes);
            if (!AdvertPayload.TryParse(bytes, out var payload)) continue;

            var observation = new AdvertObservation(payload.DeviceId, args.RawSignalStrengthInDBm, DateTimeOffset.Now);
            _seen[observation.DeviceId] = observation;
            AdvertReceived?.Invoke(observation);
            return;
        }
    }

    public void Dispose()
    {
        _watcher.Received -= OnReceived;
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            _watcher.Stop();
    }
}
