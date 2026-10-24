# 02 · BLE 广播协议

两端共享实现：`src/BTUnlock.Protocol`。修改任何字段必须两端同步发版。

## 广播参数

| 项 | 值 | 说明 |
|---|---|---|
| 广播类型 | 非连接广播（non-connectable） | PC 只扫描不连接，手机无需维持 GATT |
| 广播间隔 | AdvertiseModeBalanced ≈ 250ms | 响应速度与功耗折中 |
| Tx 功率 | High | 拉远有效距离，让「离开」判定更早触发 |
| 内容刷新 | 每 10 秒 stop/start 重建 | 令牌时间戳刷新（旧 API 广播内容固定） |
| Manufacturer Company ID | `0xFFFF` | 蓝牙 SIG 测试保留值，自用项目不注册正式 ID |

**广播内容只含 Manufacturer Data**。传统 BLE 广播包上限 31 字节，放不下 128 位
Service UUID（单独占 18 字节）——设备识别依赖 CompanyId + 负载魔数。

## 负载格式（大端 deviceId，其余小端）

### v1（12 字节，旧版兼容）

| 偏移 | 长度 | 字段 |
|---|---|---|
| 0 | 2 | Magic `0x4255` |
| 2 | 1 | Version `0x01` |
| 3 | 8 | DeviceId（安装时随机生成） |
| 11 | 1 | Flags（保留） |

### v2（19 字节，现行）

| 偏移 | 长度 | 字段 |
|---|---|---|
| 0 | 2 | Magic `0x4255` |
| 2 | 1 | Version `0x02` |
| 3 | 8 | DeviceId |
| 11 | 4 | Unix 秒时间戳（LE） |
| 15 | 4 | HMAC 截断签名（LE） |

签名 = **HMAC-SHA256(secret16, DeviceId 大端 8B ‖ 时间戳 小端 4B) 前 4 字节按小端读 uint**，
两端算法见 `TokenMath.ComputeMac`（C#）与 `AdvertiseController.ComputeMac`（Android/Javax.Crypto）。

## 滚动令牌（M3.1，防信号克隆/重放）

- 手机首启随机生成 16 字节密钥，界面显示 32 位十六进制，用户抄录到 PC 端「滚动令牌」输入框；
- PC 端启用令牌后，每帧校验：① MAC 正确 ② 时间戳 ±45 秒内（`TokenFreshnessSeconds`）。
  **任一不满足 → 该帧完全忽略**（既不推动「在场」也不推动「离开」——防止伪造信号阻断锁屏）；
- 解锁链路依赖检测器状态，因此令牌启用时解锁只认有效帧；
- 密钥更换（手机清除数据/重装）后 PC 端需重新粘贴。

### 残余威胁（明确不解决）

- **实时中继**：攻击者在手机与 PC 附近同时收发、转发新鲜帧，任何邻近性方案都无法
  区分「信号近」与「转发近」。缓解只能靠多因子（解锁可另行要求 Windows Hello）。
- 静态 v1 帧仍被 PC 接受（驱动锁屏判定），但令牌启用后 v1 帧同样被丢弃。

## 设备识别与绑定

- PC 端列出所有解析成功的广播 → 用户选中绑定 → 只保存 DeviceId；
- 手机端**配对码** = DeviceId 前 2 字节（4 位十六进制），供绑定瞬间肉眼比对。
