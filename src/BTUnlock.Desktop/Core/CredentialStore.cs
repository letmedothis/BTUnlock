using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// 解锁凭据的落盘存储：%ProgramData%\BTUnlock\credential.json
/// 密码经 DPAPI 加密为 base64；用户名/域明文（序列化时要用）。
/// M8：v=2 为用户级 DPAPI（只有录入用户的上下文可解，桌面主程序即该用户）；
/// v 缺省为旧版机器级（本机任意上下文可解）——在录入用户会话中首次读取时自动迁移为 v=2。
/// 注意：凭据提供程序（LogonUI/SYSTEM）不读本文件，明文仅经 ACL 受限的管道传递。
/// 文件 ACL 收敛为 SYSTEM + Administrators + 当前用户。
/// 约定：用户名/域不得包含 ';' 与换行（管道协议分隔符），录入时校验。
/// </summary>
internal static class CredentialStore
{
    public sealed record EnrolledCredential(int Version, string User, string Domain, byte[] ProtectedPassword);

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BTUnlock");

    private static string FilePath => Path.Combine(DirectoryPath, "credential.json");

    public static bool Exists() => File.Exists(FilePath);

    public static void Enroll(string user, string domain, string password)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(new
        {
            v = 2, // M8：用户级 DPAPI
            user,
            domain,
            protectedPassword = Convert.ToBase64String(MachineCrypto.ProtectCurrentUser(
                Encoding.Unicode.GetBytes(password))),
        });
        File.WriteAllText(FilePath, json);
        RestrictAcl(FilePath);
    }

    public static EnrolledCredential? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            var user = root.GetProperty("user").GetString()!;
            var domain = root.GetProperty("domain").GetString()!;
            var protectedPassword = Convert.FromBase64String(root.GetProperty("protectedPassword").GetString()!);
            var version = root.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 1;
            return new EnrolledCredential(version, user, domain, protectedPassword);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? TryGetPassword(EnrolledCredential enrolled)
    {
        if (enrolled.Version >= 2)
        {
            try
            {
                return Encoding.Unicode.GetString(MachineCrypto.UnprotectCurrentUser(enrolled.ProtectedPassword));
            }
            catch (Exception)
            {
                return null; // 用户级解不开（其他用户上下文/系统重装等）视为未录入，fail-closed
            }
        }

        // 旧版机器级密文（v=1）：本机任意上下文可解。仅在录入用户会话中成功解密时升级为 v2。
        try
        {
            var password = Encoding.Unicode.GetString(MachineCrypto.Unprotect(enrolled.ProtectedPassword));
            try
            {
                Enroll(enrolled.User, enrolled.Domain, password);
                Log.Info("已把解锁凭据从机器级 DPAPI 迁移为用户级（M8）");
            }
            catch (Exception)
            {
                Log.Warn("凭据迁移用户级失败，保留旧密文继续可用");
            }
            return password;
        }
        catch (Exception)
        {
            return null; // 解密失败（跨机器拷贝/系统重装等）视为未录入
        }
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    private static void RestrictAcl(string path)
    {
        var info = new FileInfo(path);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new SecurityIdentifier[]
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            (SecurityIdentifier)WindowsIdentity.GetCurrent().User!,
        })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }
        info.SetAccessControl(security);
    }
}
