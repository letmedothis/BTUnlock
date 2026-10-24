using System.Runtime.InteropServices;

namespace BTUnlock.Desktop.Core;

/// <summary>
/// 机器级 DPAPI 加解密（CryptProtectData + CRYPTPROTECT_LOCAL_MACHINE）。
/// 机器级 = 本机任意上下文（含 SYSTEM，即 LogonUI 里的凭据提供程序）可解——
/// 这正是解锁链路所需；代价是本机任意本地代码也能解，威胁模型见 docs/04。
/// entropy 仅作混淆，不是安全边界。
/// </summary>
internal static class MachineCrypto
{
    private const int CRYPTPROTECT_LOCAL_MACHINE = 0x4;

    // 与 C++ 端无共享需求（解密只发生在 C# 侧，明文经管道送出），可自由修改
    private static readonly byte[] Entropy = { 0xB7, 0x2C, 0x41, 0x55, 0x4E, 0x4C, 0x4F, 0x43, 0x4B };

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] plain)
    {
        var input = BlobFrom(plain);
        var entropy = BlobFrom(Entropy);
        var output = default(DATA_BLOB);
        try
        {
            if (!CryptProtectData(ref input, "BTUnlock", entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_LOCAL_MACHINE, ref output))
                throw new InvalidOperationException($"CryptProtectData 失败：{Marshal.GetLastWin32Error()}");
            return BytesFrom(output);
        }
        finally
        {
            Free(input); Free(entropy); Free(output);
        }
    }

    public static byte[] Unprotect(byte[] cipher)
    {
        var input = BlobFrom(cipher);
        var entropy = BlobFrom(Entropy);
        var output = default(DATA_BLOB);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_LOCAL_MACHINE, ref output))
                throw new InvalidOperationException($"CryptUnprotectData 失败：{Marshal.GetLastWin32Error()}");
            return BytesFrom(output);
        }
        finally
        {
            Free(input); Free(entropy); Free(output);
        }
    }

    private static DATA_BLOB BlobFrom(byte[] data)
    {
        var blob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static byte[] BytesFrom(DATA_BLOB blob)
    {
        var data = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, data, 0, blob.cbData);
        return data;
    }

    private static void Free(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero) LocalFree(blob.pbData);
    }
}
