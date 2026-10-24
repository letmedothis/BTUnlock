using System.Windows;
using BTUnlock.Desktop.Core;
using BTUnlock.Protocol;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace BTUnlock.Desktop;

public partial class MainWindow : Window
{
    private bool _suppressEvents = true;
    private List<ulong> _seenIds = new();

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        if (version is not null) Title += $"  v{version.ToString(3)}";
        SetWindowIconFromExe();
        Loaded += (_, _) =>
        {
            LoadSettingsIntoUi();
            _suppressEvents = false;
        };
    }

    /// <summary>从 exe 内嵌图标取窗口图标，与 ApplicationIcon 始终同源。</summary>
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

    private static App App => (App)Application.Current;

    /// <summary>由 App 的秒级定时器驱动，刷新全部状态区。</summary>
    public void RefreshStatus()
    {
        if (!IsLoaded) return;

        StateText.Text = App.Detector.State switch
        {
            PresenceState.Present => "在场",
            PresenceState.Absent => "离开（触发锁屏）",
            _ => "等待信号",
        };
        RssiText.Text = App.Detector.SmoothedRssi is { } rssi ? $"{rssi:F1} dBm" : "—";
        LastSeenText.Text = App.Detector.LastSample is { } last ? last.LocalDateTime.ToString("HH:mm:ss") : "—";
        SampleCountText.Text = App.Detector.SampleCount.ToString("N0");
        ScannerText.Text = App.Scanner.StatusText;
        LockEnabledText.Text = App.Settings.EnableAutoLock
            ? (App.LockPaused ? "已暂停（托盘菜单可恢复）" : "启用")
            : "关闭";

        BoundDeviceText.Text = App.Settings.DeviceId is { } id
            ? $"{AdvertPayload.ToFullId(id)}（配对码 {AdvertPayload.ToPairingCode(id)}）"
            : "（未绑定）";

        var previous = SeenList.SelectedItem as string;
        var seen = App.Scanner.SnapshotSeen();
        _seenIds = seen.Select(o => o.DeviceId).ToList();
        SeenList.Items.Clear();
        foreach (var o in seen)
            SeenList.Items.Add($"配对码 {AdvertPayload.ToPairingCode(o.DeviceId)}  |  {o.Rssi} dBm  |  {AdvertPayload.ToFullId(o.DeviceId)}");
        if (previous is not null && SeenList.Items.Contains(previous))
            SeenList.SelectedItem = previous;
    }

    private void LoadSettingsIntoUi()
    {
        var s = App.Settings;
        TbLockThreshold.Text = s.LockThresholdDbm.ToString();
        TbUnlockThreshold.Text = s.UnlockThresholdDbm.ToString();
        TbLockDwell.Text = s.LockDwellSeconds.ToString();
        TbUnlockDwell.Text = s.UnlockDwellSeconds.ToString();
        TbMissing.Text = s.MissingTimeoutSeconds.ToString();
        AutoLockCheckBox.IsChecked = s.EnableAutoLock;
        AutoStartCheckBox.IsChecked = Core.AutoStart.IsEnabled();
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
