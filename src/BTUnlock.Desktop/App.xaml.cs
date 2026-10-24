using System.Windows;
using System.Windows.Threading;
using BTUnlock.Desktop.Core;
using BTUnlock.Protocol;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;

namespace BTUnlock.Desktop;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _ownsMutex;

    private readonly SettingsStore _settingsStore = new();
    private BleScanner _scanner = null!;
    private ProximityDetector _detector = null!;
    private UnlockPipeServer _unlockServer = null!;
    private DispatcherTimer? _refreshTimer;
    private Forms.NotifyIcon? _trayIcon;
    private bool _exiting;
    private bool _startHidden;
    private bool _lockPaused;
    private bool _pendingLockForFullscreen;
    private byte[]? _tokenSecret;

    private readonly object _historyGate = new();
    private readonly List<(DateTime Time, double Rssi)> _rssiHistory = new();
    private long _tokenOkCount;
    private long _tokenFailCount;

    public AppSettings Settings { get; private set; } = null!;
    public bool IsExiting => _exiting;
    public bool LockPaused => _lockPaused;
    public BleScanner Scanner => _scanner;
    public ProximityDetector Detector => _detector;

    public string TokenStatus => _tokenSecret is null
        ? "未启用（手机端 v2 广播 + 粘贴密钥后生效）"
        : $"运行中 · 校验通过 {_tokenOkCount:N0} 次 · 拦截 {_tokenFailCount:N0} 帧";

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "BTUnlock.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("BTUnlock 已在运行（请查看系统托盘）。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Settings = _settingsStore.Load();
        _startHidden = e.Args.Contains("/tray", StringComparer.OrdinalIgnoreCase);
        LoadTokenSecret();
        Log.Info($"启动 v{typeof(App).Assembly.GetName().Version}，绑定设备 {Settings.DeviceIdHex ?? "无"}，" +
                 $"令牌 {(Settings.TokenSecretHex is null ? "未启用" : "已启用")}，解锁 {(Settings.EnableUnlock ? "启用" : "关闭")}");

        BuildTrayIcon();

        _scanner = new BleScanner();
        RebuildDetector();
        _scanner.AdvertReceived += OnAdvertReceived;
        _detector.StateChanged += OnPresenceChanged;
        _scanner.Start();

        // M3：解锁状态管道，供 LogonUI 里的凭据提供程序查询
        _unlockServer = new UnlockPipeServer(
            () => Settings.EnableUnlock &&
                  Core.CredentialStore.TryLoad() is not null &&
                  _detector.State == PresenceState.Present,
            () =>
            {
                var enrolled = Core.CredentialStore.TryLoad();
                if (enrolled is null) return null;
                var password = Core.CredentialStore.TryGetPassword(enrolled);
                if (password is null) return null;
                return $"{enrolled.User};{enrolled.Domain};{Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(password))}";
            });
        _unlockServer.Start();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => OnSecondElapsed();
        _refreshTimer.Start();

        var window = new MainWindow();
        MainWindow = window;
        if (_startHidden) window.Hide();
        else window.Show();

        _ = CheckForUpdateAsync(notifyNoUpdate: false); // 启动静默检查
    }

    public void LoadTokenSecret()
    {
        try
        {
            _tokenSecret = Settings.TokenSecretHex is null ? null : TokenMath.ParseSecretHex(Settings.TokenSecretHex);
            Interlocked.Exchange(ref _tokenFailCount, 0);
        }
        catch (Exception)
        {
            _tokenSecret = null;
            Log.Warn("滚动令牌密钥格式无效，已忽略");
        }
    }

    public void RebuildDetector()
    {
        var config = new ProximityConfig(
            Settings.LockThresholdDbm,
            Settings.UnlockThresholdDbm,
            Settings.LockDwellSeconds,
            Settings.UnlockDwellSeconds,
            Settings.MissingTimeoutSeconds);
        if (_detector is null)
            _detector = new ProximityDetector(config);
        else
            _detector.Reconfigure(config);
    }

    public void SaveSettings()
    {
        _settingsStore.Save(Settings);
        RebuildDetector();
    }

    public IReadOnlyList<(DateTime Time, double Rssi)> RssiSnapshot()
    {
        lock (_historyGate) return _rssiHistory.ToList();
    }

    private void OnAdvertReceived(AdvertObservation observation)
    {
        if (Settings.DeviceId is not { } boundId || observation.DeviceId != boundId) return;

        if (_tokenSecret is not null)
        {
            if (!observation.HasToken ||
                !TokenMath.Verify(_tokenSecret, observation.DeviceId, observation.Counter, observation.Mac) ||
                !TokenMath.IsFresh(observation.Counter))
            {
                var fails = Interlocked.Increment(ref _tokenFailCount);
                if (fails is 1 or 20 or 200)
                    Log.Warn($"令牌校验失败第 {fails} 帧（无令牌字段/MAC 错/过期），已丢弃");
                return;
            }
            Interlocked.Increment(ref _tokenOkCount);
        }

        _detector.Feed(observation.Rssi);
        lock (_historyGate)
        {
            _rssiHistory.Add((DateTime.Now, observation.Rssi));
            var cutoff = DateTime.Now - TimeSpan.FromSeconds(180);
            var firstValid = _rssiHistory.FindIndex(p => p.Time >= cutoff);
            if (firstValid > 0) _rssiHistory.RemoveRange(0, firstValid);
        }
    }

    private void OnPresenceChanged(PresenceState state)
    {
        Log.Info($"手机状态 → {state}");
        if (state == PresenceState.Absent && Settings.EnableAutoLock && !_lockPaused)
            TryLockNow("人走");
    }

    private void TryLockNow(string reason)
    {
        if (FullscreenDetector.IsForegroundFullscreen())
        {
            if (!_pendingLockForFullscreen) Log.Warn($"触发锁屏（{reason}）但前台全屏，挂起等待退出全屏");
            _pendingLockForFullscreen = true;
            return;
        }
        _pendingLockForFullscreen = false;
        if (WorkStationLocker.TryLock()) Log.Info($"已锁屏（{reason}）");
    }

    private void OnSecondElapsed()
    {
        _detector.EvaluateTimeout();

        // 全屏挂起的锁屏：退出全屏后补锁（仍要求状态为离开）
        if (_pendingLockForFullscreen && !_lockPaused &&
            Settings.EnableAutoLock && _detector.State == PresenceState.Absent)
        {
            TryLockNow("全屏结束补锁");
        }

        if (MainWindow is MainWindow { IsLoaded: true } window)
            window.RefreshStatus();

        if (_trayIcon is not null)
        {
            var stateText = _detector.State switch
            {
                PresenceState.Present => "手机在场",
                PresenceState.Absent => "手机离开",
                _ => "等待信号",
            };
            var rssiText = _detector.SmoothedRssi is { } rssi ? $"，{rssi:F0} dBm" : "";
            _trayIcon.Text = _lockPaused
                ? $"BTUnlock（已暂停）· {stateText}"
                : $"BTUnlock · {stateText}{rssiText}";
        }
    }

    public async Task CheckForUpdateAsync(bool notifyNoUpdate)
    {
        var (hasUpdate, tag, reason) = await Core.UpdateChecker.CheckAsync();
        if (hasUpdate && tag is not null)
        {
            Log.Info($"发现新版本 {tag}");
            await Dispatcher.InvokeAsync(() =>
                _trayIcon?.ShowBalloonTip(8000, "BTUnlock 有新版本",
                    $"{tag} 已发布，点击本提示打开下载页。", Forms.ToolTipIcon.Info));
        }
        else if (notifyNoUpdate)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"当前无可用更新（{reason}）。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information));
        }
    }

    private void BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        var pauseItem = new Forms.ToolStripMenuItem("暂停自动锁屏") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) => _lockPaused = pauseItem.Checked;
        menu.Items.Add(pauseItem);
        menu.Items.Add("检查更新", null, (_, _) => { var _ignore = CheckForUpdateAsync(true); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "BTUnlock",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        _trayIcon.BalloonTipClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Core.UpdateChecker.ReleasesUrl,
            UseShellExecute = true,
        });
    }

    public void ShowMainWindow()
    {
        MainWindow?.Show();
        if (MainWindow is not null)
        {
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    public void ExitApp()
    {
        _exiting = true;
        Log.Info("退出");
        _unlockServer?.Dispose();
        _trayIcon?.Dispose();
        _scanner.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        if (_ownsMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { /* 未持有所有权时忽略 */ }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
