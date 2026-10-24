# 05 · 构建与调试指南

## 环境要求

| 组件 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 8.0.x | PC 端与 Protocol（本机已装） |
| .NET SDK | 10.0.x | 仅安卓端（net10.0-android 需现役 LTS；net8.0-android workload 已停止支持） |
| Android workload | `dotnet workload install android` | 仅安卓端（随 SDK 10 安装，含 JDK 17 与 Android SDK 依赖） |
| Android 设备 | API 26+（Android 8.0+） | 小米/红米需开发者模式 + USB 安装 |
| Windows | 10 19041+ / 11，带蓝牙适配器 | PC 端 |

> 安卓 workload 体积数 GB（JDK + Android SDK），只调 PC 端可不装——解决方案里
> Android 项目不会被 `dotnet build src/BTUnlock.Desktop` 触发。

## 构建

```bash
# PC 端（依赖 Protocol，自动级联）
dotnet build src/BTUnlock.Desktop -c Release

# 安卓端（本机需另装 .NET 10 SDK，CI 已自动处理）
dotnet workload install android        # 首次
dotnet build src/BTUnlock.Android -c Release
# 产物 APK：src/BTUnlock.Android/bin/Release/net10.0-android/*-Signed.apk
adb install -r <apk路径>
```

## PC 端发布（单文件自包含，免运行时）

```bash
dotnet publish src/BTUnlock.Desktop -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -o publish/pc
```

## 调试

### PC 端

- 直接 VS/Rider 打开 `BTUnlock.sln`，断点可落在 `BleScanner.OnReceived`（注意
  是线程池线程）与 `ProximityDetector.Evaluate`（锁内，断点会阻塞状态机，别久停）。
- 无手机联调：把任一 BLE 信标 App（如 nRF Connect 模拟广播）按
  [02-ble-protocol.md](docs/02-ble-protocol.md) 填入 Manufacturer Data 即可模拟手机。
- 配置文件：`%APPDATA%\BTUnlock\settings.json`，删掉即恢复默认。

### Android 端

```bash
adb logcat -s BTUnlock           # 过滤本应用日志
adb shell dumpsys activity services BTUnlock   # 查看前台服务状态
```

- 验证广播是否真的在发：PC 端「采样总数」持续增长，或用另一台手机 nRF Connect
  扫描确认。
- 保活验证清单（通过标准：连续 30 分钟息屏不断流）见
  [03-miui-keepalive.md](docs/03-miui-keepalive.md)。

## 参数调优建议

| 现象 | 调整 |
|---|---|
| 人还没出门就锁了 | 锁屏阈值调低（如 -75）、锁屏驻留加长（8s） |
| 走出老远才锁 | 锁屏阈值调高（如 -65）；检查手机壳/口袋遮挡 |
| 解锁判定慢 | 解锁驻留减到 2s、解锁阈值降低（如 -65） |
| 手机放桌上偶尔「离开」 | 信号丢失超时加长（20s+）；多为环境多径衰减 |

阈值与墙体、蓝牙天线朝向强相关，以「实际工位 - 实际门口」距离采样为准。
