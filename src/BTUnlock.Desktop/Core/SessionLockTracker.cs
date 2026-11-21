using Microsoft.Win32;

namespace BTUnlock.Desktop.Core;

public enum LockState
{
    /// <summary>启动时无锁意图，会话状态未知（保守处理：不自动解锁）。</summary>
    Unknown,
    /// <summary>会话已解锁（用户人工登录后），自动解锁重新武装。</summary>
    Unlocked,
    /// <summary>本程序发起的锁屏且意图仍在有效期内 → 允许随后的自动解锁。</summary>
    AutoLocked,
    /// <summary>用户手动锁屏 / 锁屏来源不明 → 禁止自动解锁（指导书 §2.3）。</summary>
    ManualLocked,
}

/// <summary>
/// 锁屏来源跟踪（指导书 §5 的 AUTO_LOCKED / MANUAL_LOCKED 区分）：
///   - SessionLock 与未过期、近时的锁意图匹配 → AutoLocked
///   - SessionLock 无匹配 → ManualLocked（宁可不自动解锁）
///   - SessionUnlock → Unlocked（用户人工登录，重新武装自动解锁）
///   - AutoLocked 的锁意图过期（默认 10 分钟）后降级 ManualLocked
/// 仅 <see cref="LockState.AutoLocked"/> 时管道才会向凭据提供程序返回凭据。
/// </summary>
public sealed class SessionLockTracker : IDisposable
{
    private readonly LockIntentJournal _journal;
    private readonly Action<LockState>? _onChanged;

    public LockState State { get; private set; } = LockState.Unknown;

    public SessionLockTracker(LockIntentJournal journal, Action<LockState>? onChanged)
    {
        _journal = journal;
        _onChanged = onChanged;
        // 启动恢复：残留未过期意图 → 视为上次自动锁仍生效（本轮周期内允许自动解锁）；
        // 否则 Unknown，等首个会话事件收敛。
        var startup = _journal.TryConsume(DateTime.UtcNow, LockIntentJournal.FullValidity)
            ? LockState.AutoLocked
            : LockState.Unknown;
        SetState(startup);
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>每秒驱动：AutoLocked 的意图过期后降级为手动锁（自动解锁窗口封顶）。</summary>
    public void Tick(DateTime nowUtc)
    {
        if (State == LockState.AutoLocked && !_journal.HasFresh(nowUtc))
            SetState(LockState.ManualLocked);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            var ours = _journal.TryConsume(DateTime.UtcNow, LockIntentJournal.MatchWindow);
            SetState(ours ? LockState.AutoLocked : LockState.ManualLocked);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _journal.Clear();
            SetState(LockState.Unlocked);
        }
    }

    private void SetState(LockState state)
    {
        if (State == state) return;
        State = state;
        _onChanged?.Invoke(state);
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
