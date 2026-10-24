using System.Windows;
using System.Windows.Threading;
using BTUnlock.Desktop.Core;
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
    private DispatcherTimer? _refreshTimer;
    private Forms.NotifyIcon? _trayIcon;
    private bool _exiting;
    private bool _startHidden;
    private bool _lockPaused;

    public AppSettings Settings { get; private set; } = null!;
    public bool IsExiting => _exiting;
    public bool LockPaused => _lockPaused;
    public BleScanner Scanner => _scanner;
    public ProximityDetector Detector => _detector;

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

        BuildTrayIcon();

        _scanner = new BleScanner();
        RebuildDetector();
        _scanner.AdvertReceived += OnAdvertReceived;
        _detector.StateChanged += OnPresenceChanged;
        _scanner.Start();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => OnSecondElapsed();
        _refreshTimer.Start();

        var window = new MainWindow();
        MainWindow = window;
        if (_startHidden) window.Hide();
        else window.Show();
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

    private void OnAdvertReceived(AdvertObservation observation)
    {
        if (Settings.DeviceId is { } boundId && observation.DeviceId == boundId)
            _detector.Feed(observation.Rssi);
    }

    private void OnPresenceChanged(PresenceState state)
    {
        // StateChanged 在检测器内部锁内触发，这里只做快速动作
        if (state == PresenceState.Absent && Settings.EnableAutoLock && !_lockPaused)
            WorkStationLocker.TryLock();
    }

    private void OnSecondElapsed()
    {
        _detector.EvaluateTimeout();

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

    private void BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        var pauseItem = new Forms.ToolStripMenuItem("暂停自动锁屏") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) => _lockPaused = pauseItem.Checked;
        menu.Items.Add(pauseItem);
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
        _trayIcon?.Dispose();
        _scanner?.Dispose();
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
