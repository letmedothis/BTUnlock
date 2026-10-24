using System.Globalization;

namespace BTUnlock.Protocol;

/// <summary>
/// Manufacturer Data 负载（12 字节，大端）：
///   [0..1]  Magic = 0x4255
///   [2]     Version
///   [3..10] DeviceId（8 字节，手机端安装时随机生成并持久化）
///   [11]    Flags（bit0 预留给 M3 的滚动令牌开关）
/// </summary>
public readonly record struct AdvertPayload(ulong DeviceId, byte Flags)
{
    public const int Length = 12;

    public byte[] ToBytes()
    {
        var bytes = new byte[Length];
        bytes[0] = (byte)(ProtocolConstants.Magic >> 8);
        bytes[1] = (byte)(ProtocolConstants.Magic & 0xFF);
        bytes[2] = ProtocolConstants.Version;
        for (var i = 0; i < 8; i++)
            bytes[3 + i] = (byte)(DeviceId >> ((7 - i) * 8));
        bytes[11] = Flags;
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out AdvertPayload payload)
    {
        payload = default;
        if (data.Length < Length) return false;
        if (data[0] != (byte)(ProtocolConstants.Magic >> 8) ||
            data[1] != (byte)(ProtocolConstants.Magic & 0xFF))
            return false;
        if (data[2] != ProtocolConstants.Version) return false;

        ulong deviceId = 0;
        for (var i = 0; i < 8; i++)
            deviceId = (deviceId << 8) | data[3 + i];
        payload = new AdvertPayload(deviceId, data[11]);
        return true;
    }

    /// <summary>完整设备 ID（16 位十六进制），用于配置存储与日志。</summary>
    public static string ToFullId(ulong deviceId) => deviceId.ToString("X16");

    /// <summary>4 位配对码（设备 ID 前 2 字节），供用户在 PC 端肉眼比对。</summary>
    public static string ToPairingCode(ulong deviceId) => ((deviceId >> 48) & 0xFFFF).ToString("X4");

    public static bool TryParseFullId(string? text, out ulong deviceId) =>
        ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out deviceId);
}
