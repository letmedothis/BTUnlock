namespace BTUnlock.Protocol;

/// <summary>
/// 跨端共享的 BLE 广播协议常量，PC 端与 Android 端必须保持一致。
/// 详见 docs/02-ble-protocol.md。
/// </summary>
public static class ProtocolConstants
{
    /// <summary>广播中携带的 Service UUID，前 8 个十六进制字符即 "BTUNLOCK"。</summary>
    public static readonly Guid ServiceUuid = new("4254554E-4C4F-434B-8E2D-C50F3F1A0001");

    /// <summary>
    /// Manufacturer Data 使用的公司 ID。0xFFFF 是蓝牙 SIG 的测试保留值，
    /// 本项目自用不注册正式 ID；冲突概率极低，负载内另有魔数二次校验。
    /// </summary>
    public const ushort ManufacturerCompanyId = 0xFFFF;

    /// <summary>负载魔数，ASCII "BU"（0x42 0x55），用于二次过滤。</summary>
    public const ushort Magic = 0x4255;

    /// <summary>当前协议版本。</summary>
    public const byte Version = 1;
}
