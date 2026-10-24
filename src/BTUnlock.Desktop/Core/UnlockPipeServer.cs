using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// 命名管道 \\.\pipe\btunlock\presence 的服务端，供 LogonUI 里的凭据提供程序查询：
///   请求 "Q\n" → "A\n"（不可解锁）或 "P;user;domain;base64(UTF-16LE 密码)\n"
/// 仅当「启用解锁 && 已录入凭据 && 检测状态为在场」时返回 P。
/// </summary>
public sealed class UnlockPipeServer : IDisposable
{
    public const string PipeName = "btunlock/presence";

    private readonly Func<bool> _canUnlock;
    private readonly Func<string?> _secretPayload; // "user;domain;base64"，不可用时 null
    private Thread? _thread;
    private volatile bool _stop;

    public UnlockPipeServer(Func<bool> canUnlock, Func<string?> secretPayload)
    {
        _canUnlock = canUnlock;
        _secretPayload = secretPayload;
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
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var line = reader.ReadLine();
                if (line == "Q")
                {
                    var payload = _canUnlock() ? _secretPayload() : null;
                    var response = payload is not null ? $"P;{payload}\n" : "A\n";
                    var bytes = Encoding.UTF8.GetBytes(response);
                    pipe.Write(bytes, 0, bytes.Length);
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
