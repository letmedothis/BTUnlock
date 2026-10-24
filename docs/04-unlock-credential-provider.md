# 04 · 「人来解锁」实现（M3，实验性）

## 现状：已实现基础版（v0.1.x），实验性

```
锁屏画面 (LogonUI, SYSTEM)
   │ 加载 COM DLL BTUnlock.CredentialProvider.dll
   ▼
BTUnlockProvider ──1.2s 轮询──▶ \\.\pipe\btunlock\presence ──▶ BTUnlock.exe 主程序
   │ 在场且启用：暴露 1 tile + AutoLogonWithDefault=TRUE            ▲
   ▼                                  （检测器 Present + 已录入凭据 + 启用开关）
GetSerialization：KERB_INTERACTIVE_UNLOCK_LOGON(域/用户/密码) + 协商认证包
   ▼
LSASS 校验通过 → 解锁
```

组件与职责：

| 组件 | 位置 | 职责 |
|---|---|---|
| `src/BTUnlock.CredentialProvider`（C++，CI 用 MSVC 命令行编译，静态 CRT 零依赖） | LogonUI 进程内 | 仅 `CPUS_UNLOCK_WORKSTATION` 场景响应；其余场景 E_NOTIMPL 完全不参与。手机在场→tile+自动提交；不在场→0 tile（锁屏界面与原生一致） |
| `UnlockPipeServer`（C#，主程序内） | 用户会话 | 回答 `Q`：`A`（不可解锁）/ `P;user;domain;base64(密码)` |
| `CredentialStore` + `MachineCrypto`（C#） | %ProgramData%\BTUnlock | 密码经机器级 DPAPI 加密落盘，文件 ACL 收敛为 SYSTEM/Admins/当前用户 |

## 安装 / 卸载 / 回退

- 安装：下载 Release 的 `BTUnlock-UnlockProvider-win-x64-v*.zip`，管理员 PowerShell 运行 `install-cp.ps1`（复制 DLL 到 Program Files + regsvr32 注册 + 置启用开关）
- 前置：主程序运行中、已绑定手机、主界面勾选「启用」并录入 Windows 密码
- 卸载：`uninstall-cp.ps1`；**紧急停用**（不卸载）：
  `reg add HKLM\SOFTWARE\BTUnlock /v UnlockProviderEnabled /t REG_DWORD /d 0 /f`
  （CP 每次枚举都读该值，锁屏一次后生效）
- 日志：`C:\ProgramData\BTUnlock\cp.log`（时间戳+线程号，GetSerialization/轮询全链路）

## 安全模型（必读）

1. **Windows 无官方解锁 API**。实现 = 凭据提供程序提交你的密码本体，因此必须录入一次。
   密码经 `CRYPTPROTECT_LOCAL_MACHINE` DPAPI 加密——**本机任意 SYSTEM 上下文可解**。
   若攻击者已能在本机执行任意代码，该边界实际不增加防护；但需明确：这不是安全增强功能，是便捷功能。
2. **仅解锁，不登录**：登录/UAC/改密场景一律不参与（E_NOTIMPL）。冷启动仍需密码/PIN。
3. **滚动令牌（M3.1，已实现）**：静态 BLE 广播可被抓包克隆（30m 内）。启用「滚动令牌」后
   （手机 v2 广播 + PC 粘贴密钥），伪造/过期帧被完全忽略，解锁只认有效帧。
   残余威胁为实时中继，见 docs/02 威胁模型。**开启解锁前强烈建议同时启用令牌。**
4. 改 Windows 密码后需重新录入；微软在线账户（MSA）不保证可用（本地/域账户为验证路径）。

## 故障排查

| 症状 | 排查 |
|---|---|
| 手机在场但不出 tile | cp.log 有无 "GetCredentialCount: offer=1"；注册表开关；主程序是否运行；管道是否有进程占用 |
| tile 出现但解锁失败 | 密码错误（改密后未重录）→ 正常密码 tile 仍可用，登录后重新录入 |
| 怀疑影响系统锁屏 | 紧急停用注册表命令 → 锁一次屏验证；仍异常则安全模式卸载（CP 随 Winlogon 栈加载，安全模式可删注册表） |
| 想看主程序侧 | 管道协议 `Q\n` → `P;user;domain;b64\n`，可用 PowerShell 管道客户端自测 |

## 已知边界（后续版本）

- 单用户会话假设（多用户切换场景未验证）
- `ICredentialProviderEvents` 直调（与微软动态 CP 示例一致，未做 GIT 封送）
- 滚动令牌（M3.1）、错误密码自动降级提示（M3.2）
