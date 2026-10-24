using System.Globalization;

namespace BTUnlock.Protocol;

/// <summary>
/// Manufacturer Data 负载（大端 deviceId，其余小端）：
///   v1（12 字节）：[0..1] Magic  [2] 0x01  [3..10] DeviceId  [11] Flags
///   v2（19 字节）：[0..1] Magic  [2] 0x02  [3..10] DeviceId  [11..14] 时间戳(LE)
///                 [15..18] HMAC-SHA256(secret, deviceId ‖ 时间戳) 前 4 字节(LE)
/// v2 时间戳 = Unix 秒；PC 校验 MAC 与新鲜度（TokenFreshnessSeconds 窗口），
/// 抗抓包重放。v1 帧仍可解析（仅用于锁屏判定，不能用于解锁链路）。
/// </summary>
public readonly record struct AdvertPayload(ulong DeviceId, uint Counter, uint Mac, bool HasToken)
{
    public const int V1Length = 12;
    public const int V2Length = 19;

    public static byte[] ToBytesV2(ulong deviceId, uint counter, uint mac)
    {
        var b = new byte[V2Length];
        b[0] = (byte)(ProtocolConstants.Magic >> 8);
        b[1] = (byte)(ProtocolConstants.Magic & 0xFF);
        b[2] = ProtocolConstants.Version2;
        for (var i = 0; i < 8; i++)
            b[3 + i] = (byte)(deviceId >> ((7 - i) * 8));
        b[11] = (byte)counter; b[12] = (byte)(counter >> 8);
        b[13] = (byte)(counter >> 16); b[14] = (byte)(counter >> 24);
        b[15] = (byte)mac; b[16] = (byte)(mac >> 8);
        b[17] = (byte)(mac >> 16); b[18] = (byte)(mac >> 24);
        return b;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out AdvertPayload payload)
    {
        payload = default;
        if (data.Length < V1Length) return false;
        if (data[0] != (byte)(ProtocolConstants.Magic >> 8) ||
            data[1] != (byte)(ProtocolConstants.Magic & 0xFF))
            return false;

        // ref 结构不能被局部函数捕获，改为传参
        static ulong ReadDeviceId(ReadOnlySpan<byte> d)
        {
            ulong id = 0;
            for (var i = 0; i < 8; i++)
                id = (id << 8) | d[3 + i];
            return id;
        }

        switch (data[2])
        {
            case ProtocolConstants.Version1:
                payload = new AdvertPayload(ReadDeviceId(data), 0, 0, HasToken: false);
                return true;
            case ProtocolConstants.Version2 when data.Length >= V2Length:
                payload = new AdvertPayload(
                    ReadDeviceId(data),
                    (uint)(data[11] | (data[12] << 8) | (data[13] << 16) | (data[14] << 24)),
                    (uint)(data[15] | (data[16] << 8) | (data[17] << 16) | (data[18] << 24)),
                    HasToken: true);
                return true;
            default:
                return false;
        }
    }

    /// <summary>完整设备 ID（16 位十六进制），用于配置存储与日志。</summary>
    public static string ToFullId(ulong deviceId) => deviceId.ToString("X16");

    /// <summary>4 位配对码（设备 ID 前 2 字节），供用户在 PC 端肉眼比对。</summary>
    public static string ToPairingCode(ulong deviceId) => ((deviceId >> 48) & 0xFFFF).ToString("X4");

    public static bool TryParseFullId(string? text, out ulong deviceId) =>
        ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out deviceId);
}
