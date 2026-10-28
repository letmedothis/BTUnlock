using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BTUnlock.Desktop.Core;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace BTUnlock.Desktop;

public partial class ConnectionProbeWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _loading;
    private string? _lastLine;
    private static App App => (App)Application.Current;

    public ConnectionProbeWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => RefreshStatus();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { RefreshStatus(); _timer.Start(); }
            else _timer.Stop();
        };
        Loaded += async (_, _) => await RefreshDevicesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (_loading || App.ConnectionProbe.IsRunning) return;
        _loading = true;
        RefreshStatus();
        try
        {
            var selected = (Devices.SelectedItem as BluetoothConnectionProbe.PairedDevice)?.Id;
            var devices = await BluetoothConnectionProbe.FindPairedDevicesAsync();
            Devices.ItemsSource = devices;
            Devices.SelectedItem = devices.FirstOrDefault(d => d.Id == selected) ?? devices.FirstOrDefault();
            if (devices.Count == 0)
                MessageBox.Show(this, "没有找到已配对的蓝牙设备，请先在 Windows 设置中配对设备。鼠标需切换到蓝牙模式。", "蓝牙连接验证");
        }
        catch (Exception ex) { MessageBox.Show(this, $"读取已配对设备失败：{ex.Message}", "蓝牙连接验证"); }
        finally { _loading = false; RefreshStatus(); }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Devices.SelectedItem is not BluetoothConnectionProbe.PairedDevice device) return;
        try { App.ConnectionProbe.Start(device); }
        catch (Exception ex) { MessageBox.Show(this, $"开始记录失败：{ex.Message}", "蓝牙连接验证"); }
        RefreshStatus();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        App.ConnectionProbe.Stop();
        RefreshStatus();
    }

    private void MarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(MarkerText.Text)) App.ConnectionProbe.Mark(MarkerText.Text.Trim());
        RefreshStatus();
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        var path = App.ConnectionProbe.Snapshot().LogPath;
        if (path is null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, $"无法打开日志目录：{ex.Message}", "蓝牙连接验证"); }
    }

    private void RefreshStatus()
    {
        var snapshot = App.ConnectionProbe.Snapshot();
        RefreshButton.IsEnabled = Devices.IsEnabled = !snapshot.Running && !_loading;
        StartButton.IsEnabled = !snapshot.Running && !_loading && Devices.SelectedItem is not null;
        StopButton.IsEnabled = snapshot.Running;
        StatusText.Text = $"{snapshot.Status} | 会话：{snapshot.Session}\n" +
            $"已连接→断开 {snapshot.Disconnections} 次；断开→连接 {snapshot.Connections} 次（初始状态不计数）\n" +
            $"日志：{snapshot.LogPath ?? "开始后生成"}" +
            (snapshot.WriteError is null ? "" : $"\n{snapshot.WriteError}");
        var last = snapshot.Lines.LastOrDefault();
        if (last == _lastLine) return;
        _lastLine = last;
        EventsText.Text = string.Join(Environment.NewLine, snapshot.Lines);
        EventsText.ScrollToEnd();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.IsExiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
