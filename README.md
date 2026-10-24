# BTUnlock — 人走锁屏 / 人来解锁

用一台**小米/红米手机**作为蓝牙身份钥匙：手机离开 PC 身边，Windows 自动锁屏；手机回到身边，自动解锁（解锁能力规划中，见路线图）。

- PC 端：Windows 10 19041+ / Windows 11，仅需 .NET 8 运行时（自包含发布后零依赖）
- 手机端：Android 8.0+，按小米 MIUI / HyperOS 专项适配（后台保活引导）

[![build-release](https://github.com/letmedothis/BTUnlock/actions/workflows/build.yml/badge.svg)](https://github.com/letmedothis/BTUnlock/actions/workflows/build.yml)
[下载最新版本](https://github.com/letmedothis/BTUnlock/releases/latest)

## 工作原理

```
┌─────────────┐   BLE 广播(~250ms, 含设备ID)    ┌──────────────────────┐
│  小米手机    │ ─────────────────────────────▶ │  Windows PC          │
│  BTUnlock   │      RSSI 信号强度随距离衰减     │  BTUnlock 托盘程序    │
│  (前台服务)  │                                │  扫描 → 平滑 → 迟滞   │
└─────────────┘                                │   ↓                  │
                                               │  离开 → LockWorkStation│
                                               │  回来 → (M3: 解锁)    │
                                               └──────────────────────┘
```

不依赖蓝牙配对/连接，PC 端只做 BLE 扫描，因此对系统蓝牙栈无侵入、功耗低；
判定采用「修剪均值 + 双阈值迟滞 + 双侧驻留时间 + 信号丢失超时」，防止人还没
出门就被误锁、或在阈值边缘来回抖动。

## 仓库结构

```
BTUnlock/
├── README.md                  ← 你在这里
├── docs/                      ← 全部设计/适配/开发文档
│   ├── 01-architecture.md     总体架构与状态机
│   ├── 02-ble-protocol.md     BLE 广播协议规范
│   ├── 03-miui-keepalive.md   小米/红米后台保活适配
│   ├── 04-unlock-credential-provider.md  「人来解锁」技术方案（M3）
│   └── 05-development.md      构建与调试指南
├── BTUnlock.sln
└── src/
    ├── BTUnlock.Protocol/     两端共享的协议常量与编解码
    ├── BTUnlock.Desktop/      Windows 端（WPF 托盘程序）
    └── BTUnlock.Android/      安卓端（前台广播服务 + 保活引导）
```

## 快速开始

### 方式一：直接用 CI 产物（推荐）

每次 push 到 main，GitHub Actions 自动打包并在
[Releases 页](https://github.com/letmedothis/BTUnlock/releases) 发布新版本
（版本号自动递增，更新说明自动按提交记录生成）：

- `BTUnlock-Desktop-win-x64-v*.zip`：解压即用，免装 .NET
- `BTUnlock-Android-v*.apk`：传到手机安装

流水线细节见 [docs/06-ci-release.md](docs/06-ci-release.md)。

### 方式二：本地构建（可选；当前约定全靠 CI，本机未装 SDK）

#### 手机端（小米/红米）

1. 构建：**默认不在本地构建安卓端**，push 后从 [Actions](https://github.com/letmedothis/BTUnlock/actions)/[Releases](https://github.com/letmedothis/BTUnlock/releases) 下载 APK（本项目约定：编译验证全靠 CI；本地工具链仅断点调试时才需要，见 [docs/05-development.md](docs/05-development.md)）
   ```bash
   dotnet build src/BTUnlock.Android -c Release
   ```
2. 安装到手机后打开 App，完成「小米/红米保活设置」五项引导（自启动、省电策略无限制、
   电池优化、通知权限、最近任务锁定），点「开始广播」。
3. 记下界面上的**配对码**（4 位十六进制）。

#### PC 端

1. 构建：`dotnet build src/BTUnlock.Desktop -c Release`，运行
   `src/BTUnlock.Desktop/bin/Release/.../BTUnlock.exe`
   （自包含单文件发布见 [docs/05-development.md](docs/05-development.md)）

### 首次使用流程（两端装好后）

1. 手机打开 BTUnlock → 完成保活引导 → 开始广播；
2. PC 端「扫描到的手机」列表中选中你的设备 → **绑定选中设备**，核对配对码与手机一致；
3. 手机带离电脑（或关闭手机蓝牙），几秒后 PC 锁屏；带回身边恢复「在场」；
4. 误锁/不够灵敏 → 主界面调整「触发参数」。

## 路线图

- [x] **M1 手机端**：BLE 广播前台服务 + 小米保活引导 + 看门狗自愈
- [x] **M2 PC 端**：扫描 / RSSI 迟滞判定 / 自动锁屏 / 托盘常驻 / 开机自启
- [x] **图标**：统一挂锁视觉——PC exe/窗口/托盘 + Android 自适应启动图标/通知剪影（`tools/generate-icons.ps1` 可再生）
- [ ] **M3 人来解锁**：Windows Credential Provider + 命名管道在场状态
      （方案已定稿：[docs/04-unlock-credential-provider.md](docs/04-unlock-credential-provider.md)）
- [ ] **M4 打磨**：阈值校准向导、全屏/下载防误锁、正式图标

## 文档索引

| 文档 | 内容 |
|---|---|
| [01-architecture.md](docs/01-architecture.md) | 组件划分、RSSI 判定状态机、线程模型 |
| [02-ble-protocol.md](docs/02-ble-protocol.md) | 广播负载格式、字段定义、安全边界 |
| [03-miui-keepalive.md](docs/03-miui-keepalive.md) | MIUI/HyperOS 杀后台对策与验证清单 |
| [04-unlock-credential-provider.md](docs/04-unlock-credential-provider.md) | 解锁凭据提供程序设计 |
| [05-development.md](docs/05-development.md) | 环境要求、构建、调试、发布 |
| [06-ci-release.md](docs/06-ci-release.md) | GitHub Actions 自动打包/版本号/Release |
