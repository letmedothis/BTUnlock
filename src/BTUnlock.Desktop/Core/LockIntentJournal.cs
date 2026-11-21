using System.IO;
using System.Text.Json;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// 自动锁屏意图 journal（指导书 §5.2）：调用 LockWorkStation 之前写入，
/// 用于把会话锁定事件归因为「本程序发起」（AUTO_LOCKED）或「其他来源」
/// （MANUAL_LOCKED，禁止自动解锁）。落盘以便程序崩溃/重启后仍能匹配。
/// 匹配是一次性的，且「锁定事件距意图写入」超过 maxAge 视为不匹配，
/// 防止残留意图把用户稍后的手动 Win+L 误归因为自动锁。
/// 匹配不上时一律按手动锁定处理——宁可不自动解锁。
/// </summary>
public sealed class LockIntentJournal
{
    /// <summary>在线匹配窗口：SessionLock 必须在写入意图后这么久内到达。</summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(120);

    /// <summary>意图有效期（也是自动解锁窗口的上限），过期后视为手动锁。</summary>
    public static readonly TimeSpan FullValidity = TimeSpan.FromMinutes(10);

    private sealed record Intent(string Epoch, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BTUnlock");

    private static string FilePath => Path.Combine(DirectoryPath, "lock-intent.json");

    private readonly object _gate = new();
    private string? _consumedEpoch;

    /// <summary>发起自动锁屏前调用：写入一条带过期时间的意图。</summary>
    public void Write()
    {
        lock (_gate)
        {
            _consumedEpoch = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var now = DateTime.UtcNow;
                File.WriteAllText(FilePath, JsonSerializer.Serialize(
                    new Intent(Guid.NewGuid().ToString("N"), now, now + FullValidity)));
            }
            catch (Exception)
            {
                // 写不进去等价于没有意图：随后的 SessionLock 会按手动锁处理（安全侧）
            }
        }
    }

    /// <summary>
    /// 一次性消费一条意图。maxAge 限制「意图写入 → 锁定事件」的最大间隔：
    /// 在线匹配用 <see cref="MatchWindow"/>，启动恢复用全部有效期。
    /// </summary>
    public bool TryConsume(DateTime nowUtc, TimeSpan maxAge)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var intent = JsonSerializer.Deserialize<Intent>(File.ReadAllText(FilePath));
                if (intent is null || intent.ExpiresAtUtc < nowUtc) return false;
                if (intent.Epoch == _consumedEpoch) return false;
                if (nowUtc - intent.CreatedAtUtc > maxAge) return false;
                _consumedEpoch = intent.Epoch;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>是否存在未过期意图（不消费）——AutoLocked 状态的有效性依据。</summary>
    public bool HasFresh(DateTime nowUtc)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var intent = JsonSerializer.Deserialize<Intent>(File.ReadAllText(FilePath));
                return intent is not null && intent.ExpiresAtUtc >= nowUtc;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _consumedEpoch = null;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* 删不掉等自然过期，无碍安全 */ }
        }
    }
}
