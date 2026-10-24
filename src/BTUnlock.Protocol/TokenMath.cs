using System.Security.Cryptography;

namespace BTUnlock.Protocol;

/// <summary>
/// 滚动令牌计算（两端算法一致，Android 侧用 javax.crypto HmacSHA256 对齐）：
///   mac = HMAC-SHA256(secret16, deviceId 大端 8 字节 ‖ counter 小端 4 字节) 前 4 字节按小端读 uint
/// </summary>
public static class TokenMath
{
    public static uint ComputeMac(ReadOnlySpan<byte> secret, ulong deviceId, uint counter)
    {
        Span<byte> message = stackalloc byte[12];
        for (var i = 0; i < 8; i++)
            message[i] = (byte)(deviceId >> ((7 - i) * 8));
        message[8] = (byte)counter;
        message[9] = (byte)(counter >> 8);
        message[10] = (byte)(counter >> 16);
        message[11] = (byte)(counter >> 24);

        using var hmac = new HMACSHA256(secret.ToArray());
        var digest = hmac.ComputeHash(message.ToArray());
        return (uint)(digest[0] | (digest[1] << 8) | (digest[2] << 16) | (digest[3] << 24));
    }

    public static bool Verify(byte[] secret, ulong deviceId, uint counter, uint mac) =>
        ComputeMac(secret, deviceId, counter) == mac;

    public static bool IsFresh(uint counterUtcSeconds) =>
        Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)counterUtcSeconds)
            <= ProtocolConstants.TokenFreshnessSeconds;

    public static byte[] ParseSecretHex(string? hex)
    {
        if (hex is { Length: 32 } &&
            byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out _) &&
            hex.All(c => Uri.IsHexDigit(c)))
        {
            var bytes = new byte[16];
            for (var i = 0; i < 16; i++)
                bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
            return bytes;
        }
        throw new FormatException("密钥应为 32 位十六进制字符");
    }
}
