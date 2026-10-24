using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// 解锁凭据的落盘存储：%ProgramData%\BTUnlock\credential.json
/// 密码经机器级 DPAPI 加密为 base64；用户名/域明文（序列化时要用）。
/// 文件 ACL 收敛为 SYSTEM + Administrators + 当前用户。
/// 约定：用户名/域不得包含 ';' 与换行（管道协议分隔符），录入时校验。
/// </summary>
internal static class CredentialStore
{
    public sealed record EnrolledCredential(string User, string Domain, byte[] ProtectedPassword);

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BTUnlock");

    private static string FilePath => Path.Combine(DirectoryPath, "credential.json");

    public static bool Exists() => File.Exists(FilePath);

    public static void Enroll(string user, string domain, string password)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(new
        {
            user,
            domain,
            protectedPassword = Convert.ToBase64String(MachineCrypto.Protect(
                System.Text.Encoding.Unicode.GetBytes(password))),
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
            return new EnrolledCredential(user, domain, protectedPassword);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? TryGetPassword(EnrolledCredential enrolled)
    {
        try
        {
            var bytes = MachineCrypto.Unprotect(enrolled.ProtectedPassword);
            return System.Text.Encoding.Unicode.GetString(bytes);
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
