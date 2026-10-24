# 01 · 总体架构

## 组件与数据流

```
Android (小米/红米)                     Windows PC
┌──────────────────────┐               ┌─────────────────────────────────┐
│ MainActivity         │               │ BTUnlock.exe (WPF, 托盘常驻)     │
│  ├ 状态/配对码展示    │               │                                 │
│  ├ 广播开关          │               │ BleScanner                      │
│  └ 小米保活引导(5项) │               │  (WinRT BluetoothLEAdvertisement│
│         │            │               │   Watcher, 后台线程回调)          │
│ BleAdvertiserService │  BLE 广播     │   ↓ AdvertObservation           │
│  (前台服务,connected │ ────────────▶ │ ProximityDetector               │
│   Device 类型)       │  ~250ms/帧    │  (修剪均值→双阈值迟滞→驻留判定)   │
│  └ AdvertiseController│              │   ↓ PresenceState               │
│     ├ 协议负载构造    │               │ Present / Absent                │
│     └ 看门狗(60s自检, │               │   ↓ Absent 且启用自动锁屏        │
│        15min 强制重启)│               │ WorkStationLocker.LockWorkStation│
│ BootReceiver(开机)   │               │ App (UI刷新/托盘/设置/自启)       │
└──────────────────────┘               └─────────────────────────────────┘
        │                                        ▲
        └──── BTUnlock.Protocol (共享常量/编解码) ─┘
```

两端唯一的耦合点是 `BTUnlock.Protocol`：Service UUID、公司 ID、魔数、版本、
负载编解码。协议变更必须两端同步。

## RSSI 判定状态机（ProximityDetector）

输入：绑定设备的每帧广播 RSSI（约每 250ms 一帧）。

1. **平滑**：滑动窗口 8 帧，去掉最高最低后取均值，抑制多径反射造成的毛刺。
2. **迟滞 + 驻留**（时间轴上必须持续满足才切换状态）：

| 条件（平滑 RSSI） | 持续时间 | 结果 |
|---|---|---|
| ≤ 锁屏阈值（默认 -70 dBm） | ≥ 锁屏驻留（默认 5s） | → **Absent**（人走，锁屏） |
| ≥ 解锁阈值（默认 -60 dBm） | ≥ 解锁驻留（默认 3s） | → **Present**（人在） |
| 介于两者之间 | — | 计时清零，状态不变（防抖迟滞区） |
| 完全无帧超过 信号丢失超时（默认 12s） | — | → **Absent**（蓝牙被关/App 被杀/手机重启） |

初始状态 Unknown：先满足「人在」条件才进入 Present，避免启动即锁屏；
手机蓝牙关闭等信号丢失场景由超时兜底强制锁屏。

3. **触发**：`Absent` 且启用自动锁屏且未暂停 → `LockWorkStation()`（user32 P/Invoke，
   任意会话类型可用，无权限问题）。

## 线程模型

- `BluetoothLEAdvertisementWatcher.Received` 在系统线程池线程触发 →
  `BleScanner` 内部完成解析（微秒级）后转发；`ProximityDetector` 全部方法内部加锁，
  事件在锁内触发，订阅方（App）只做快速动作（调 LockWorkStation / Dispatcher 投递）。
- UI 刷新用 WPF `DispatcherTimer`（1s）：读取检测器快照 + 驱动 `EvaluateTimeout()`。
- 安卓端：广播控制在前台服务主线程，看门狗用 `Handler`（60s 周期）。

## 配置与持久化

- PC：`%APPDATA%\BTUnlock\settings.json`（设备 ID、阈值、开关）。
- Android：`SharedPreferences`（设备 ID 随机生成后不变、服务自启意愿）。
- PC 开机自启：HKCU `Software\Microsoft\Windows\CurrentVersion\Run`，命令行加
  `/tray` 静默启动。

## 安全边界（当前版本）

广播为**静态设备 ID，未加密**。任何人抓包可复制该 ID 并伪造广播——因此当前版本
只做「锁屏」（低敏感动作），不做解锁。解锁（M3）的对抗设计见
[04-unlock-credential-provider.md](04-unlock-credential-provider.md)。
