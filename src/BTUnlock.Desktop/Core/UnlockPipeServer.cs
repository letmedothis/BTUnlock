using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// 命名管道 \\.\pipe\btunlock\presence 的服务端，供 LogonUI 里的凭据提供程序查询：
///   请求 "Q\n" → "A\n"（不可解锁）或 "P;user;domain;base64(UTF-16LE 密码)\n"
///   请求 "F;N\n" → "K\n"（CP 上报解锁失败，连续第 N 次；M6.6 熔断依据）
/// 仅当「启用解锁 && 未熔断 && 已配置令牌且新鲜 && 锁屏来源为本程序 && 已录入凭据
/// && 检测状态为在场」时返回 P（各条件由 canUnlock 闭包决定）。
/// </summary>
public sealed class UnlockPipeServer : IDisposable
{
    public const string PipeName = "btunlock/presence";

    private readonly Func<bool> _canUnlock;
    private readonly Func<string?> _secretPayload; // "user;domain;base64"，不可用时 null
    private readonly Action<int>? _onUnlockFailure; // 参数 = CP 报告的连续失败次数
    private Thread? _thread;
    private volatile bool _stop;

    public UnlockPipeServer(Func<bool> canUnlock, Func<string?> secretPayload, Action<int>? onUnlockFailure = null)
    {
        _canUnlock = canUnlock;
        _secretPayload = secretPayload;
        _onUnlockFailure = onUnlockFailure;
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "BTUnlockPipe" };
        _thread.Start();
    }

    private void AcceptLoop()
    {
        while (!_stop)
        {
            NamedPipeServerStream pipe;
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.ReadWrite, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(
                    (SecurityIdentifier)WindowsIdentity.GetCurrent().User!,
                    PipeAccessRights.FullControl, AccessControlType.Allow));

                pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut,
                    maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, PipeOptions.None,
                    inBufferSize: 64, outBufferSize: 2048, pipeSecurity: security);
                pipe.WaitForConnection();
            }
            catch (Exception)
            {
                if (_stop) return;
                Thread.Sleep(1000); // 管道被占用等异常，稍后重建
                continue;
            }

            try
            {
                // 连接与读取均限时：同机失控/恶意进程占住唯一管道实例时，
                // 宁可本次查询失败也不能挂死 CP 的轮询（fail-closed，指导书 §6.2）
                var connected = false;
                try { connected = pipe.WaitForConnectionAsync().Wait(3000); }
                catch { connected = false; }
                if (!connected || _stop) throw new TimeoutException("等待管道连接超时");

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var readTask = reader.ReadLineAsync();
                if (!readTask.Wait(2000)) throw new TimeoutException("等待管道请求超时");
                var line = readTask.Result;
                if (line == "Q")
                {
                    var payload = _canUnlock() ? _secretPayload() : null;
                    var response = payload is not null ? $"P;{payload}\n" : "A\n";
                    var bytes = Encoding.UTF8.GetBytes(response);
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();
                }
                else if (line is not null && line.StartsWith("F;", StringComparison.Ordinal))
                {
                    // M6.6：CP 上报解锁失败（连续第 N 次），应答 K 确认已收到
                    var streak = int.TryParse(line.AsSpan(2), out var n) && n > 0 ? n : 1;
                    try { _onUnlockFailure?.Invoke(streak); }
                    catch { /* 回调异常不影响管道服务 */ }
                    var ack = Encoding.UTF8.GetBytes("K\n");
                    pipe.Write(ack, 0, ack.Length);
                    pipe.Flush();
                }
            }
            catch (Exception)
            {
                // 客户端超时断开等，忽略
            }
            finally
            {
                try
                {
                    if (pipe.IsConnected) pipe.Disconnect();
                }
                catch { /* 忽略 */ }
                pipe.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        // WaitForConnection 阻塞中的后台线程随进程退出结束（IsBackground）
    }
}
