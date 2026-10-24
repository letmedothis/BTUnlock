using System.Windows;
using System.Windows.Threading;
using BTUnlock.Desktop.Core;

namespace BTUnlock.Desktop;

/// <summary>
/// 两步阈值校准：工位采样 8s → 门口采样 8s → 自动计算
/// 解锁阈值 = 工位均值 - 3dB，锁屏阈值 = 门口均值 + 3dB。
/// </summary>
public partial class CalibrationWindow : Window
{
    private enum Step { Near, Far, Done }

    private const int SampleSeconds = 8;

    private Step _step = Step.Near;
    private readonly List<short> _nearSamples = new();
    private readonly List<short> _farSamples = new();
    private DispatcherTimer? _timer;
    private int _elapsed;
    private List<short> _current = new();

    public CalibrationWindow()
    {
        InitializeComponent();
        Closed += (_, _) => StopSampling();
    }

    private static App App => (App)Application.Current;

    private void MainButton_Click(object sender, RoutedEventArgs e)
    {
        if (_timer is not null)
        {
            StopSampling();
            return;
        }

        if (App.Settings.DeviceId is not { } boundId)
        {
            MessageBox.Show(this, "请先在主窗口绑定手机。", "BTUnlock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MainButton.Content = "停止";
        _current = _step == Step.Near ? _nearSamples : _farSamples;
        _current.Clear();
        _elapsed = 0;
        Progress.Value = 0;
        StepResult.Text = "采样中，请保持手机位置不动…";

        App.Scanner.AdvertReceived += OnAdvert;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => OnTick(boundId);
        _timer.Start();
    }

    private void OnAdvert(AdvertObservation observation)
    {
        if (App.Settings.DeviceId is { } id && observation.DeviceId == id)
            _current.Add(observation.Rssi);
    }

    private void OnTick(ulong boundId)
    {
        _elapsed += 100;
        Progress.Value = (double)_elapsed / (SampleSeconds * 1000) * 100;
        if (_elapsed < SampleSeconds * 1000) return;
        StopSampling();

        var average = TrimmedMean(_current);
        if (_current.Count < 8 || double.IsNaN(average))
        {
            StepResult.Text = $"采样失败：只收到 {_current.Count} 帧（需 ≥8）。确认手机正在广播后重试。";
            ResetButton();
            return;
        }

        if (_step == Step.Near)
        {
            StepResult.Text = $"工位均值 {average:F1} dBm（{_current.Count} 帧）。\n\n接下来：拿着手机走到平时「离开」的位置（如门口），到位后再点「开始采样」。";
            _step = Step.Far;
            StepTitle.Text = "第二步 · 离开位置采样";
            StepDesc.Text = "拿着手机走到你希望触发锁屏的距离（例如走出房门的走廊），站定后点「开始采样」。采样 8 秒。";
            ResetButton();
            return;
        }

        // Far 采样完成 → 计算
        _step = Step.Done;
        var nearAvg = TrimmedMean(_nearSamples);
        var farAvg = TrimmedMean(_farSamples);
        if (nearAvg - farAvg < 6)
        {
            StepResult.Text = $"两个位置信号差异太小（工位 {nearAvg:F1} / 离开 {farAvg:F1}，差 {nearAvg - farAvg:F1} dBm）。\n" +
                              "建议把「离开位置」放得更远些（或隔一堵墙）重新校准，否则容易误锁。";
            ResetButton();
            return;
        }

        var unlock = Math.Clamp((int)Math.Round(nearAvg - 3), -90, -35);
        var lockAt = Math.Clamp((int)Math.Round(farAvg + 3), -90, -35);
        if (unlock <= lockAt) unlock = lockAt + 5;

        var result = MessageBox.Show(this,
            $"工位 {nearAvg:F1} dBm / 离开 {farAvg:F1} dBm。\n\n" +
            $"建议阈值：解锁 {unlock} dBm、锁屏 {lockAt} dBm（已含 3 dB 安全余量）。\n应用并保存？",
            "校准完成", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.OK)
        {
            App.Settings.UnlockThresholdDbm = unlock;
            App.Settings.LockThresholdDbm = lockAt;
            App.SaveSettings();
            Log.Info($"校准完成：工位 {nearAvg:F1}，离开 {farAvg:F1} → 解锁 {unlock}，锁屏 {lockAt}");
        }
        Close();
    }

    private void ResetButton()
    {
        MainButton.Content = _step == Step.Done ? "完成" : "开始采样";
    }

    private void StopSampling()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
        App.Scanner.AdvertReceived -= OnAdvert;
        ResetButton();
    }

    private static double TrimmedMean(List<short> samples)
    {
        if (samples.Count == 0) return double.NaN;
        var sorted = samples.OrderBy(v => v).ToList();
        if (sorted.Count >= 6) sorted = sorted.Skip(1).Take(sorted.Count - 2).ToList();
        return sorted.Average(v => (double)v);
    }
}
