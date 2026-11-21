using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BTUnlock.Desktop.Core;
using BTUnlock.Protocol;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace BTUnlock.Desktop;

public partial class MainWindow : Window
{
    private bool _suppressEvents = true;
    private List<ulong> _seenIds = new();
    private ConnectionProbeWindow? _connectionProbeWindow;

    private static readonly Color PresentColor = Color.FromRgb(0x16, 0xA3, 0x4A);
    private static readonly Color AbsentColor = Color.FromRgb(0xDC, 0x26, 0x26);
    private static readonly Color IdleColor = Color.FromRgb(0x6B, 0x72, 0x80);
    private static readonly Color PresentTint = Color.FromArgb(0x26, 0x16, 0xA3, 0x4A);
    private static readonly Color AbsentTint = Color.FromArgb(0x26, 0xDC, 0x26, 0x26);
    private static readonly Color IdleTint = Color.FromArgb(0x14, 0x6B, 0x72, 0x80);

    // 可动画画刷：状态卡底色 / 状态文字与脉冲点共用，切换走 ColorAnimation（深浅色主题下半透明叠色都成立）
    private readonly SolidColorBrush _cardBrush = new(IdleTint);
    private readonly SolidColorBrush _stateBrush = new(IdleColor);
    private PresenceState? _lastPresenceState;
    private bool _pulseOn;

    public MainWindow()
    {
        InitializeComponent();
        StatusCard.Background = _cardBrush;
        StateText.Foreground = _stateBrush;
        PulseDot.Fill = _stateBrush;
        var version = typeof(MainWindow).Assembly.GetName().Version;
        if (version is not null) Title += $"  v{version.ToString(3)}";
        SetWindowIconFromExe();
        SourceInitialized += OnSourceInitialized;
        Opacity = 0;
        Loaded += (_, _) =>
        {
            AnimateWindowIn();
            LoadSettingsIntoUi();
            _suppressEvents = false;
        };
    }

    /// <summary>Mica 背板（22621+）；失败退回仅 DWM 圆角，再失败保持原生外观。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!Core.Win11Fx.TryEnableBackdrop(this))
            Core.Win11Fx.TryRoundCorners(this);
    }

    private void AnimateWindowIn()
    {
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new QuadraticEase() });
    }

    private static DoubleAnimation MakePulse() => new(1, 1.45, TimeSpan.FromMilliseconds(900))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase(),
    };

    /// <summary>状态切换时过渡状态卡底色、状态文字与脉冲点（在场时圆点持续脉冲）。</summary>
    private void AnimateStatusState(PresenceState state)
    {
        var (tint, text) = state switch
        {
            PresenceState.Present => (PresentTint, PresentColor),
            PresenceState.Absent => (AbsentTint, AbsentColor),
            _ => (IdleTint, IdleColor),
        };
        var duration = TimeSpan.FromMilliseconds(320);
        _cardBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(tint, duration) { EasingFunction = new QuadraticEase() });
        _stateBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(text, duration) { EasingFunction = new QuadraticEase() });

        if (state == PresenceState.Present && !_pulseOn)
        {
            _pulseOn = true;
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, MakePulse());
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, MakePulse());
        }
        else if (state != PresenceState.Present && _pulseOn)
        {
            _pulseOn = false;
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    private static App App => (App)Application.Current;

    /// <summary>由 App 的秒级定时器驱动，刷新全部状态区与曲线。</summary>
    public void RefreshStatus()
    {
        if (!IsLoaded) return;

        var state = App.Detector.State;
        StateText.Text = state switch
        {
            PresenceState.Present => "手机在场",
            PresenceState.Absent => "手机离开",
            _ => "等待信号",
        };
        if (state != _lastPresenceState)
        {
            _lastPresenceState = state;
            AnimateStatusState(state);
        }

        RssiText.Text = App.Detector.SmoothedRssi is { } rssi ? $"{rssi:F1} dBm" : "—";
        LastSeenText.Text = App.Detector.LastSample is { } last ? last.LocalDateTime.ToString("HH:mm:ss") : "—";
        ScannerText.Text = App.Scanner.StatusText;
        LockEnabledText.Text = App.ConnectionProbe.IsRunning ? "蓝牙连接验证中，自动锁屏/解锁暂停" : App.Settings.EnableAutoLock
            ? (App.LockPaused ? "已暂停（托盘菜单可恢复）" : "启用")
            : "关闭";

        BoundDeviceText.Text = App.Settings.DeviceId is { } id
            ? $"{AdvertPayload.ToFullId(id)}（配对码 {AdvertPayload.ToPairingCode(id)}）"
            : "（未绑定）";

        TokenStatusText.Text = App.TokenStatus;
        UnlockStatusText.Text = BuildUnlockStatusText();

        var previous = SeenList.SelectedItem as string;
        var seen = App.Scanner.SnapshotSeen();
        _seenIds = seen.Select(o => o.DeviceId).ToList();
        SeenList.Items.Clear();
        foreach (var o in seen)
            SeenList.Items.Add($"配对码 {AdvertPayload.ToPairingCode(o.DeviceId)}  |  {o.Rssi} dBm  |  {AdvertPayload.ToFullId(o.DeviceId)}");
        if (previous is not null && SeenList.Items.Contains(previous))
            SeenList.SelectedItem = previous;

        RedrawChart();
    }

    /// <summary>解锁状态行：凭据录入、熔断提示、令牌前置与锁屏来源。</summary>
    private string BuildUnlockStatusText()
    {
        var parts = new List<string>();
        if (!Core.CredentialStore.Exists()) parts.Add("未录入密码");
        else if (App.Settings.UnlockSuspended) parts.Add("已熔断暂停：解锁连续失败，请重新录入密码");
        else parts.Add("已录入密码");
        if (App.Settings.EnableUnlock && App.Settings.TokenSecretHex is null)
            parts.Add("自动解锁需先配置滚动令牌");
        parts.Add("锁定来源：" + App.LockState switch
        {
            LockState.AutoLocked => "自动（本程序）",
            LockState.ManualLocked => "手动/其他",
            LockState.Unlocked => "会话已解锁",
            _ => "未知",
        });
        return "状态：" + string.Join(" · ", parts);
    }

    /// <summary>近 3 分钟 RSSI 折线 + 双阈值参考线（红=锁屏 绿=解锁）。</summary>
    private void RedrawChart()
    {
        RssiChart.Children.Clear();
        var data = App.RssiSnapshot();
        if (data.Count < 2) return;

        const double minRssi = -95, maxRssi = -35;
        var width = RssiChart.ActualWidth is > 10 ? RssiChart.ActualWidth : 420;
        var height = RssiChart.ActualHeight is > 10 ? RssiChart.ActualHeight : 90;
        var t0 = data[0].Time;
        var rangeSeconds = Math.Max(1.0, (data[^1].Time - t0).TotalSeconds);
        var clampY = height - 2;

        double X(DateTime t) => (t - t0).TotalSeconds / rangeSeconds * width;
        double Y(double rssi)
        {
            var y = height - (Math.Clamp(rssi, minRssi, maxRssi) - minRssi) / (maxRssi - minRssi) * height;
            return Math.Clamp(y, 2, clampY);
        }

        void AddThresholdLine(double rssi, Color color)
        {
            var y = Y(rssi);
            var line = new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            };
            RssiChart.Children.Add(line);
        }

        AddThresholdLine(App.Settings.LockThresholdDbm, Color.FromRgb(0xDC, 0x26, 0x26));
        AddThresholdLine(App.Settings.UnlockThresholdDbm, Color.FromRgb(0x16, 0xA3, 0x4A));

        var polyline = new System.Windows.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(SystemColors.AccentColor), // 跟随系统强调色
            StrokeThickness = 1.5,
        };
        foreach (var (time, rssi) in data)
            polyline.Points.Add(new Point(X(time), Y(rssi)));
        RssiChart.Children.Add(polyline);
    }

    private void SetWindowIconFromExe()
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            if (icon is null) return;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            Icon = source;
        }
        catch (Exception)
        {
            // 取不到就沿用系统默认图标，不影响功能
        }
    }

    private void LoadSettingsIntoUi()
    {
        var s = App.Settings;
        TbLockThreshold.Text = s.LockThresholdDbm.ToString();
        TbUnlockThreshold.Text = s.UnlockThresholdDbm.ToString();
        TbLockDwell.Text = s.LockDwellSeconds.ToString();
        TbUnlockDwell.Text = s.UnlockDwellSeconds.ToString();
        TbMissing.Text = s.MissingTimeoutSeconds.ToString();
        TbTokenSecret.Text = s.TokenSecretHex ?? string.Empty;
        AutoLockCheckBox.IsChecked = s.EnableAutoLock;
        AutoStartCheckBox.IsChecked = Core.AutoStart.IsEnabled();
        CbUnlock.IsChecked = s.EnableUnlock;
        RefreshStatus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseInt(TbLockThreshold.Text, -100, -30, out var lockThreshold) ||
            !TryParseInt(TbUnlockThreshold.Text, -100, -30, out var unlockThreshold) ||
            !TryParseInt(TbLockDwell.Text, 1, 120, out var lockDwell) ||
            !TryParseInt(TbUnlockDwell.Text, 1, 120, out var unlockDwell) ||
            !TryParseInt(TbMissing.Text, 3, 600, out var missing))
        {
            MessageBox.Show(this, "参数格式不正确，请检查范围提示。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (unlockThreshold <= lockThreshold)
        {
            MessageBox.Show(this, "解锁阈值必须大于锁屏阈值（两者之间是防抖迟滞区间）。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = App.Settings;
        s.LockThresholdDbm = lockThreshold;
        s.UnlockThresholdDbm = unlockThreshold;
        s.LockDwellSeconds = lockDwell;
        s.UnlockDwellSeconds = unlockDwell;
        s.MissingTimeoutSeconds = missing;
        App.SaveSettings();
        MessageBox.Show(this, "已保存并生效。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool TryParseInt(string text, int min, int max, out int value)
    {
        value = 0;
        return int.TryParse(text, out value) && value >= min && value <= max;
    }

    private void BindButton_Click(object sender, RoutedEventArgs e)
    {
        if (SeenList.SelectedIndex < 0 || SeenList.SelectedIndex >= _seenIds.Count)
        {
            MessageBox.Show(this, "请先在列表中选择一台手机。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var deviceId = _seenIds[SeenList.SelectedIndex];
        App.Settings.DeviceIdHex = AdvertPayload.ToFullId(deviceId);
        App.SaveSettings();
        App.Scanner.ClearSeen();
        RefreshStatus();
        MessageBox.Show(this,
            $"已绑定 {AdvertPayload.ToFullId(deviceId)}。\n请与手机端显示的配对码 {AdvertPayload.ToPairingCode(deviceId)} 比对确认。",
            "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UnbindButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DeviceIdHex = null;
        App.SaveSettings();
        RefreshStatus();
    }

    private void SaveTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var hex = TbTokenSecret.Text.Trim();
        if (hex.Length == 0)
        {
            MessageBox.Show(this, "请粘贴手机端显示的 32 位密钥，或点「清除」停用。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            TokenMath.ParseSecretHex(hex);
        }
        catch (Exception)
        {
            MessageBox.Show(this, "密钥格式不正确：应为 32 位十六进制字符（0-9 / a-f）。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.Settings.TokenSecretHex = hex.ToLowerInvariant();
        App.SaveSettings();
        App.LoadTokenSecret();
        RefreshStatus();
        MessageBox.Show(this,
            "滚动令牌已启用。手机端升级后广播带时间戳签名，伪造/重放信号将被忽略。\n注意：手机端 App 重装或清除数据会重新生成密钥，需重新粘贴。",
            "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearTokenButton_Click(object sender, RoutedEventArgs e)
    {
        TbTokenSecret.Text = string.Empty;
        App.Settings.TokenSecretHex = null;
        App.SaveSettings();
        App.LoadTokenSecret();
        RefreshStatus();
    }

    private void AutoLockCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        App.Settings.EnableAutoLock = AutoLockCheckBox.IsChecked == true;
        App.SaveSettings();
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var enabled = AutoStartCheckBox.IsChecked == true;
        Core.AutoStart.SetEnabled(enabled);
        App.Settings.StartWithWindows = enabled;
        App.SaveSettings();
    }

    private void CbUnlock_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        App.Settings.EnableUnlock = CbUnlock.IsChecked == true;
        App.SaveSettings();
    }

    private void BtnEnroll_Click(object sender, RoutedEventArgs e)
    {
        new EnrollWindow { Owner = this }.ShowDialog();
        RefreshStatus();
    }

    private void CalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        new CalibrationWindow { Owner = this }.ShowDialog();
        LoadSettingsIntoUi();
    }

    private void ConnectionProbeButton_Click(object sender, RoutedEventArgs e)
    {
        _connectionProbeWindow ??= new ConnectionProbeWindow { Owner = this };
        _connectionProbeWindow.Show();
        _connectionProbeWindow.Activate();
    }

    private void LockNowButton_Click(object sender, RoutedEventArgs e) => WorkStationLocker.TryLock();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
