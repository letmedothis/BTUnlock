using System.IO;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BTUnlock.Desktop.Core;

/// <summary>只观察系统连接状态，不发起连接、不驱动在场判定或凭据提交。</summary>
public sealed class BluetoothConnectionProbe : IDisposable
{
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    // 鼠标/键盘可能使用 BLE HID，不能只枚举经典蓝牙手机。
    private static readonly string Selector =
        $"({BluetoothDevice.GetDeviceSelectorFromPairingState(true)}) OR " +
        $"({BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)})";
    private static readonly string[] Properties = { ConnectedProperty, AddressProperty };
    private readonly object _gate = new();
    private readonly Queue<string> _recent = new();
    private DeviceWatcher? _watcher;
    private System.Threading.Timer? _heartbeat;
    private string? _deviceId;
    private bool? _connected;
    private string _session = "未知（等待锁定/解锁事件）";
    private string _status = "未开始";
    private string? _logPath;
    private string? _writeError;
    private int _connections;
    private int _disconnections;

    public sealed record PairedDevice(string Id, string Name, string Address, bool? Connected)
    {
        public string Display => $"{Name} | {Address} | {ConnectionText(Connected)}";
    }

    public sealed record ProbeSnapshot(bool Running, string Status, string Session,
        int Connections, int Disconnections, string? LogPath, string? WriteError, string[] Lines);

    public static string ConnectionText(bool? connected) => connected switch
    {
        true => "已连接",
        false => "未连接",
        _ => "未知",
    };

    public static async Task<IReadOnlyList<PairedDevice>> FindPairedDevicesAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(Selector, Properties,
            DeviceInformationKind.AssociationEndpoint);
        return devices.Select(d => new PairedDevice(d.Id, d.Name,
                d.Properties.TryGetValue(AddressProperty, out var address) ? address?.ToString() ?? "" : "",
                ReadConnection(d.Properties)))
            .OrderBy(d => d.Name).ThenBy(d => d.Address).ToList();
    }

    public bool IsRunning { get { lock (_gate) return _watcher is not null; } }

    public ProbeSnapshot Snapshot()
    {
        lock (_gate)
            return new(_watcher is not null, _status, _session, _connections, _disconnections,
                _logPath, _writeError, _recent.ToArray());
    }

    public void Start(PairedDevice device)
    {
        lock (_gate)
        {
            if (_watcher is not null) throw new InvalidOperationException("请先停止当前记录。");
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BTUnlock", "connection-probes");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
            // 首次写入必须成功，避免界面显示已记录但实际没有证据文件。
            File.WriteAllText(path, "BTUnlock Bluetooth connection probe\r\n");
            _logPath = path;
            _recent.Clear();
            _writeError = null;
            _deviceId = device.Id;
            _connected = null;
            _connections = _disconnections = 0;
            _session = "未知（等待锁定/解锁事件）";
            _status = "等待系统枚举所选设备";
            var watcher = DeviceInformation.CreateWatcher(Selector, Properties,
                DeviceInformationKind.AssociationEndpoint);
            watcher.Added += OnAdded;
            watcher.Updated += OnUpdated;
            watcher.Removed += OnRemoved;
            watcher.EnumerationCompleted += OnEnumerationCompleted;
            watcher.Stopped += OnStopped;
            _watcher = watcher;
            Write($"START name={device.Name}; address={device.Address}; id={device.Id}");
            Write("只观察连接，不主动连接；初始连接状态不计为重连。记录期间暂停 BTUnlock 自动锁屏/解锁。");
            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                watcher.Start();
                _heartbeat = new System.Threading.Timer(_ => Heartbeat(watcher), null,
                    TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }

    private static bool? ReadConnection(IReadOnlyDictionary<string, object> properties) =>
        properties.TryGetValue(ConnectedProperty, out var value) && value is bool connected ? connected : null;

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher) || device.Id != _deviceId) return;
            SetConnection(ReadConnection(device.Properties), "ADDED");
        }
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher) || update.Id != _deviceId) return;
            // Updated 只包含变化的属性，缺少 IsConnected 不代表断开。
            if (update.Properties.ContainsKey(ConnectedProperty))
                SetConnection(ReadConnection(update.Properties), "UPDATED");
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher) || update.Id != _deviceId) return;
            SetConnection(null, "REMOVED（移出枚举不等于确认断开）");
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher)) return;
            Write($"ENUMERATION_COMPLETED connection={ConnectionText(_connected)}");
        }
    }

    private void OnStopped(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher)) return;
            _connected = null;
            _status = $"系统监听已停止（{sender.Status}），请停止记录后重试";
            Write($"WATCHER_STOPPED status={sender.Status}");
            // 保持记录模式，避免监听异常时意外恢复自动解锁。
        }
    }

    private void SetConnection(bool? connected, string source)
    {
        if (_connected == false && connected == true) _connections++;
        if (_connected == true && connected == false) _disconnections++;
        var previous = _connected;
        _connected = connected;
        _status = ConnectionText(connected);
        Write($"{source} {ConnectionText(previous)} -> {_status}; session={_session}");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        lock (_gate)
        {
            if (_watcher is null) return;
            if (e.Reason == SessionSwitchReason.SessionLock) _session = "已锁定";
            if (e.Reason == SessionSwitchReason.SessionUnlock) _session = "已解锁";
            Write($"SESSION {e.Reason}; connection={ConnectionText(_connected)}");
        }
    }

    private void Heartbeat(DeviceWatcher watcher)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(watcher, _watcher)) return;
            Write($"HEARTBEAT connection={ConnectionText(_connected)}; session={_session}; watcher={watcher.Status}");
        }
    }

    public void Mark(string text)
    {
        lock (_gate)
        {
            if (_watcher is not null)
                Write($"MARK {text.Replace('\r', ' ').Replace('\n', ' ')}");
        }
    }

    // 所有调用在 _gate 内执行；界面仅保留 300 行，文件保留本轮完整记录。
    private void Write(string text)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {text}";
        _recent.Enqueue(line);
        while (_recent.Count > 300) _recent.Dequeue();
        try { File.AppendAllText(_logPath!, line + Environment.NewLine); }
        catch (Exception ex) { _writeError = $"日志写入失败：{ex.Message}"; }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_watcher is not { } watcher) return;
            _watcher = null; // 拒绝上一轮迟到的回调。
            _heartbeat?.Dispose();
            _heartbeat = null;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            watcher.Added -= OnAdded;
            watcher.Updated -= OnUpdated;
            watcher.Removed -= OnRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            watcher.Stopped -= OnStopped;
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch (Exception ex) { Write($"STOP_ERROR {ex.Message}"); }
            _status = "记录已停止";
            Write("STOP（恢复原有自动功能设置）");
        }
    }

    public void Dispose() => Stop();
}
