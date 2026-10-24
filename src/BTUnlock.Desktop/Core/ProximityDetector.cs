namespace BTUnlock.Desktop.Core;

public enum PresenceState
{
    Unknown,
    Present,
    Absent,
}

public sealed record ProximityConfig(
    int LockThresholdDbm,
    int UnlockThresholdDbm,
    int LockDwellSeconds,
    int UnlockDwellSeconds,
    int MissingTimeoutSeconds);

/// <summary>
/// 把连续的 RSSI 采样转成"在场/离开"状态机：
/// 修剪均值平滑 + 双阈值迟滞 + 双侧驻留时间 + 信号丢失超时。
/// 判定规则：
///   - 平滑 RSSI ≤ 锁屏阈值并持续 LockDwellSeconds → Absent（人走）
///   - 平滑 RSSI ≥ 解锁阈值并持续 UnlockDwellSeconds → Present（人回）
///   - 两者之间为迟滞区间，清零两侧计时
///   - 超过 MissingTimeoutSeconds 收不到任何采样 → Absent（手机蓝牙被关 / App 被杀）
/// </summary>
public sealed class ProximityDetector
{
    private const int WindowSize = 8;

    private readonly object _gate = new();
    private readonly Queue<double> _window = new();
    private ProximityConfig _config;
    private DateTime? _lockCandidateSince;
    private DateTime? _unlockCandidateSince;

    public ProximityDetector(ProximityConfig config) => _config = config;

    public PresenceState State { get; private set; } = PresenceState.Unknown;
    public double? SmoothedRssi { get; private set; }
    public DateTimeOffset? LastSample { get; private set; }
    public long SampleCount { get; private set; }

    /// <summary>状态切换时触发（在 Feed/EvaluateTimeout 的调用线程上，实现方不要回调本类）。</summary>
    public event Action<PresenceState>? StateChanged;

    public void Reconfigure(ProximityConfig config)
    {
        lock (_gate) _config = config;
    }

    public void Feed(short rssiDbm)
    {
        lock (_gate)
        {
            _window.Enqueue(rssiDbm);
            while (_window.Count > WindowSize) _window.Dequeue();
            SmoothedRssi = TrimmedMean(_window);
            LastSample = DateTimeOffset.Now;
            SampleCount++;
            Evaluate(DateTime.Now);
        }
    }

    /// <summary>每秒调用一次：推进驻留计时，并处理"完全收不到广播"的超时离开。</summary>
    public void EvaluateTimeout()
    {
        lock (_gate)
        {
            if (LastSample is { } last &&
                DateTime.Now - last.LocalDateTime >= TimeSpan.FromSeconds(_config.MissingTimeoutSeconds) &&
                State != PresenceState.Absent)
            {
                SetState(PresenceState.Absent);
            }
            Evaluate(DateTime.Now);
        }
    }

    private void Evaluate(DateTime now)
    {
        if (SmoothedRssi is not { } rssi) return;

        if (rssi <= _config.LockThresholdDbm)
        {
            _unlockCandidateSince = null;
            _lockCandidateSince ??= now;
            if (State != PresenceState.Absent &&
                now - _lockCandidateSince.Value >= TimeSpan.FromSeconds(_config.LockDwellSeconds))
                SetState(PresenceState.Absent);
        }
        else if (rssi >= _config.UnlockThresholdDbm)
        {
            _lockCandidateSince = null;
            _unlockCandidateSince ??= now;
            if (State != PresenceState.Present &&
                now - _unlockCandidateSince.Value >= TimeSpan.FromSeconds(_config.UnlockDwellSeconds))
                SetState(PresenceState.Present);
        }
        else
        {
            _lockCandidateSince = null;
            _unlockCandidateSince = null;
        }
    }

    private void SetState(PresenceState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>去掉最高、最低各一个样本后的均值，抑制偶发的 RSSI 毛刺。</summary>
    private static double TrimmedMean(Queue<double> samples)
    {
        var sorted = samples.OrderBy(v => v).ToList();
        if (sorted.Count >= 4)
            sorted = sorted.Skip(1).Take(sorted.Count - 2).ToList();
        return sorted.Average();
    }
}
