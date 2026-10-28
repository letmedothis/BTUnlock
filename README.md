# BTUnlock — 人走锁屏 / 人来解锁

将 Android 手机作为 BLE 在场信标：Windows 桌面程序根据手机广播的信号强度判断离开与返回，离开时自动锁屏；安装可选的 Credential Provider 并录入 Windows 密码后，可尝试实验性的自动解锁。

[![build-release](https://github.com/letmedothis/BTUnlock/actions/workflows/build.yml/badge.svg)](https://github.com/letmedothis/BTUnlock/actions/workflows/build.yml)
[下载最新版本](https://github.com/letmedothis/BTUnlock/releases/latest)

## 当前功能

| 组件 | 已实现功能 |
|---|---|
| Windows 桌面端 | BLE 扫描、单设备绑定、RSSI 平滑与迟滞判定、离开锁屏、托盘常驻、暂停自动锁屏、登录后自启 |
| Android 端 | BLE v2 广播、前台服务与常驻通知、令牌定时刷新、广播看门狗、开机恢复广播、小米/红米保活设置引导 |
| 信号调优 | 最近 3 分钟 RSSI 曲线、工位与离开位置各 8 秒的阈值校准、手动调整触发参数、全屏时延后自动锁屏 |
| 实验性解锁 | C++ Credential Provider、命名管道查询在场状态、Windows 密码录入与机器级 DPAPI 加密存储 |
| 日志与更新 | 桌面文件日志、解锁组件日志、GitHub Release 新版本提醒（手动下载安装） |

以上为代码已实现的能力，不代表已通过所有机型、账户类型及真实锁屏场景的验证。自动解锁默认关闭。

## 环境要求

| 场景 | 要求 |
|---|---|
| PC 运行 | Windows 10 版本 2004 / Build 19041 及以上，或 Windows 11；蓝牙适配器须支持 BLE 扫描 |
| 手机运行 | Android 8.0 / API 26 及以上，硬件须支持 BLE 广播；内置 MIUI / HyperOS 保活引导 |
| Release 安装包 | PC 与解锁组件目前提供 `win-x64`；PC 自包含发布，无需另装 .NET 运行时 |
| 本地开发 | .NET 10 SDK；Android 另需 `android` workload、Android SDK 与 JDK；解锁组件另需 MSVC C++ 工具链和 Windows SDK |

项目目标框架为 `net10.0`、`net10.0-windows10.0.19041.0` 和 `net10.0-android`。

## 工作原理

```text
Android 前台服务
  └─ 非连接 BLE 广播（约 250 ms；设备 ID + 时间戳 + HMAC）
       ↓
Windows BLE 扫描 → 绑定设备过滤 → 可选令牌校验 → RSSI 在场状态机
  ├─ 离开 + 启用自动锁屏 → LockWorkStation
  └─ 在场 + 启用解锁 + 已录入密码 → 命名管道
       ↓
可选 Credential Provider → 提交 Windows 凭据 → 系统验证并解锁
```

两端无需系统蓝牙配对或建立 GATT 连接。手机使用 Manufacturer Data 广播，PC 按 Company ID `0xFFFF`、魔数及协议版本识别设备。4 位“配对码”仅用于人工核对设备身份。

检测器最多保留 8 个 RSSI 样本；样本数不少于 4 时去掉最大值、最小值后取均值。双阈值之间为迟滞区间，配合驻留时间减少边缘抖动。默认配置如下，可在主界面修改并保存：

| 参数 | 默认值 | 判定用途 |
|---|---|---|
| 锁屏阈值 | `-70 dBm` | 平滑 RSSI 不高于此值，进入离开计时 |
| 解锁阈值 | `-60 dBm` | 平滑 RSSI 不低于此值，进入在场计时 |
| 锁屏驻留 | `5 秒` | 离开条件持续满足后切换为离开 |
| 解锁驻留 | `3 秒` | 在场条件持续满足后切换为在场 |
| 信号丢失超时 | `12 秒` | 收到过采样后，长时间无新采样触发离开判定 |

解锁阈值必须大于锁屏阈值。RSSI 受墙体、手机放置方式和天线朝向影响，不直接等同于距离。前台窗口全屏时自动锁屏会延后；退出全屏且仍为离开状态时补锁。

## 下载与首次使用

### 1. 安装两端程序

从 [Releases](https://github.com/letmedothis/BTUnlock/releases) 下载同一版本的产物：

| 文件 | 用途 |
|---|---|
| `BTUnlock-Desktop-win-x64-v*.zip` | 解压后运行 `BTUnlock.exe` |
| `BTUnlock-Android-v*.apk` | 传到手机安装 |
| `BTUnlock-UnlockProvider-win-x64-v*.zip` | 可选的实验性解锁组件，含 DLL 和安装/卸载脚本 |
| 各文件对应的 `.sha256` | 下载文件的 SHA-256 校验值 |

### 2. 手机开启广播

1. 打开蓝牙，启动 BTUnlock，按提示授予蓝牙及通知权限；授权或打开蓝牙后，如广播未开始，再点一次“开始广播”。
2. 小米/红米按 App 中的五项引导设置自启动、省电策略、忽略电池优化、通知权限和最近任务锁定。其他品牌需按系统设置允许后台运行。
3. 确认状态显示正在广播，并保留常驻通知。记下设备 ID、4 位配对码及 32 位十六进制安全密钥。

手动停止广播会关闭应用记录的开机恢复标志；开启广播后，开机接收器会尝试恢复服务，是否成功仍受系统权限与后台策略影响。

### 3. PC 绑定与配置令牌

1. 运行 `BTUnlock.exe`，在扫描列表选中自己的设备，点击“绑定选中设备”，核对配对码及设备 ID。
2. 将手机显示的 **32 位安全密钥**填入 PC 的“滚动令牌”，点击“保存密钥”，观察校验通过计数是否增长。
3. 两端开启自动校时。手机每 10 秒刷新令牌，PC 校验 HMAC 及时间戳是否在当前时间的 `±45 秒`内；配置密钥后，无令牌、签名错误或过期的广播会被丢弃。
4. 如手机清除数据或重装后身份发生变化，重新绑定并更新 PC 密钥。

### 4. 校准并测试锁屏

1. 初次校准可先取消“离开时自动锁屏”，通过“校准向导…”分别采集工位与离开位置的信号。
2. 保存参数后启用自动锁屏，携带手机离开，观察是否锁屏；返回后观察是否恢复在场。
3. 仅安装两端程序时，返回只恢复在场状态；自动解锁还需要下方组件。

关闭主窗口会隐藏到托盘；双击托盘图标可恢复窗口，托盘菜单可暂停自动锁屏或退出。勾选“开机自启”实际通过当前用户的 Run 注册表项在登录后以 `/tray` 启动，不是系统服务。

## 实验性自动解锁

如果希望不安装 Android App，可先使用主界面的“蓝牙设备连接验证（无需手机 App）…”测试任意已配对的手机、蓝牙鼠标或键盘。工具同时枚举经典蓝牙和 BLE，记录连接变化、会话锁定/解锁事件与运行心跳；记录期间暂停原有自动锁屏/解锁，当前尚未将连接事件接入自动解锁。步骤与判断标准见 [蓝牙设备连接验证](docs/07-bluetooth-connection-probe.md)。

1. 先完成绑定、密钥配置及在场判定测试，并阅读 [解锁组件说明](docs/04-unlock-credential-provider.md)。
2. 解压解锁组件包，在**管理员 PowerShell** 中进入解压目录，执行 `./install-cp.ps1`。脚本将 DLL 复制到 `%ProgramFiles%\BTUnlock` 并注册组件。
3. PC 主界面点击“录入 / 管理密码…”，填写 Windows 用户名、域和**账户密码（不是 PIN）**，然后勾选“人来解锁”的启用项。
4. 保持桌面主程序在托盘运行，锁屏后将手机拿开再带回，验证实际解锁行为。Windows 密码变更后需重新录入。

当前实现只响应 `CPUS_UNLOCK_WORKSTATION`，不参与冷启动登录、UAC 或改密。它依赖检测器的在场状态，不要求每次都先经历离开；手动锁屏时若手机仍在场，也可能触发自动解锁。

**安全边界与当前限制：**

- 自动解锁通过向 Windows 提交保存的账户密码实现。密码经机器级 DPAPI 加密，文件 ACL 限定为 SYSTEM、Administrators 和录入用户；这是便利功能，不能替代系统认证或 Windows Hello。
- **启用解锁前应配置滚动令牌。** 当前代码未强制将“已配置密钥”作为解锁前置条件；未配置密钥时，仅凭设备 ID 的广播也能推动在场判定。
- HMAC 截断为 4 字节，并只检查时间窗口，没有逐帧去重；窗口内重放和实时中继仍有风险，不能将其视为完整的防重放或距离认证机制。
- 当前超时逻辑在判定离开后仍会评估旧的平滑 RSSI；旧值很强时可能重新判为在场。涉及信号中断的锁屏与解锁行为需要重点实测，不能假定其一定保持离开状态。
- 多用户切换、微软在线账户兼容性及错误密码自动降级提示仍需完善或验证；Windows Hello 双因素确认和国际化尚未实现。

停用自动解锁可取消主界面启用项。紧急停用组件可在管理员 PowerShell 执行：

```powershell
reg add HKLM\SOFTWARE\BTUnlock /v UnlockProviderEnabled /t REG_DWORD /d 0 /f
```

卸载组件使用包内 `./uninstall-cp.ps1`。卸载脚本保留密码文件，可在主界面“录入 / 管理密码…”中清除；DLL 被 LogonUI 占用时，重启后检查残留文件。

## 本地构建与 CI

安装 .NET 10 SDK 后，在仓库根目录执行；仅开发 PC 端无需安装 Android workload：

```powershell
# 构建和运行 PC 端（自动构建 Protocol）
dotnet build src/BTUnlock.Desktop -c Release
dotnet run --project src/BTUnlock.Desktop -c Release

# 发布 Windows x64 自包含单文件版本
dotnet publish src/BTUnlock.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/pc

# Android：另需 Android SDK / JDK 工具链
dotnet workload install android
dotnet build src/BTUnlock.Android -c Release
# 已签名 APK：src/BTUnlock.Android/bin/Release/net10.0-android/*-Signed.apk
```

`BTUnlock.sln` 包含 Protocol、Desktop 和 Android 三个 C# 项目。Credential Provider 未加入解决方案，由 [.github/workflows/build.yml](.github/workflows/build.yml) 使用 MSVC C++17、x64、静态 CRT 单独编译；完整命令以该工作流为准。

CI 配置行为：代码 push 触发构建，纯 `docs/**` / Markdown 改动不触发；也支持手动运行。三个构建任务全部成功后，仅 `main` 分支创建 Release，版本为 `0.1.<github.run_number>`；其他分支通过 Actions artifacts 获取产物。仓库当前未配置自动化测试项目或测试执行步骤，构建成功不能替代手机保活及真实锁屏/解锁验证。

Android Release 使用仓库内固定 keystore 签名，方便覆盖安装；该密钥及其配置仅适用于当前自用方案，公开分发前应改为受保护的签名管理，且更换签名需考虑已有安装的升级兼容。

## 数据与排查

| 路径 | 内容 |
|---|---|
| `%APPDATA%\BTUnlock\settings.json` | 绑定设备、阈值、功能开关及**明文令牌密钥** |
| `%APPDATA%\BTUnlock\log.txt` | 桌面日志，超过约 512 KiB 后清空重建 |
| `%ProgramData%\BTUnlock\credential.json` | 用户名、域及 DPAPI 加密密码 |
| `%ProgramData%\BTUnlock\cp.log` | Credential Provider 日志 |

分享配置或排查截图前应隐藏令牌密钥和账户信息。

| 现象 | 检查方向 |
|---|---|
| 扫描不到手机 | PC 蓝牙监听器状态、手机蓝牙权限、手机是否支持并正在 BLE 广播 |
| 有设备但无有效采样 | 绑定 ID、两端密钥、两端时钟、令牌校验计数及最近采样时间 |
| 息屏后断流 | 前台通知、后台权限、MIUI / HyperOS 五项保活设置；按保活文档做长时间实测 |
| 误锁或触发慢 | 重新校准，结合 RSSI 曲线调整阈值、驻留与丢失超时；确认托盘暂停状态及是否处于全屏 |
| 在场但无法解锁 | 主程序是否运行、组件是否安装并启用、凭据是否录入或已变更，查看 `cp.log` |

## 仓库结构与文档

```text
BTUnlock/
├── .github/workflows/build.yml      # 三端构建、打包与 Release
├── BTUnlock.sln                     # C# 解决方案
├── src/
│   ├── BTUnlock.Protocol/           # 广播协议、编解码、令牌算法
│   ├── BTUnlock.Desktop/            # WPF 托盘程序、扫描与在场判定
│   ├── BTUnlock.Android/            # Android 广播服务与保活引导
│   └── BTUnlock.CredentialProvider/ # C++ 实验性解锁组件
├── tools/                          # 组件安装/卸载、图标生成脚本
└── docs/                           # 架构、协议、适配与开发说明
```

| 文档 | 内容 |
|---|---|
| [01-architecture.md](docs/01-architecture.md) | 组件划分、RSSI 状态机、线程模型 |
| [02-ble-protocol.md](docs/02-ble-protocol.md) | 广播负载、令牌算法、设备识别 |
| [03-miui-keepalive.md](docs/03-miui-keepalive.md) | MIUI / HyperOS 后台设置与验证清单 |
| [04-unlock-credential-provider.md](docs/04-unlock-credential-provider.md) | 解锁组件设计、安装与回退 |
| [05-development.md](docs/05-development.md) | 本地构建与调试说明 |
| [06-ci-release.md](docs/06-ci-release.md) | Actions 打包与 Release 流程 |
| [07-bluetooth-connection-probe.md](docs/07-bluetooth-connection-probe.md) | 无 Android App 的蓝牙设备连接验证与日志说明 |

部分专题文档仍保留旧版本表述；目标框架、执行命令和安全条件请结合当前项目文件、工作流及源码核对。
