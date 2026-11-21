# 04 · 「人来解锁」实现（M3，实验性）

## 现状：已实现基础版（v0.1.x），实验性

```
锁屏画面 (LogonUI, SYSTEM)
   │ 加载 COM DLL BTUnlock.CredentialProvider.dll
   ▼
BTUnlockProvider ──1.2s 轮询──▶ \\.\pipe\btunlock\presence ──▶ BTUnlock.exe 主程序
   │ 通过全部门禁：暴露 1 tile + AutoLogonWithDefault=TRUE          ▲
   ▼      （启用+未熔断+令牌已配置且新鲜+锁屏来源=本程序+已录入凭据+检测器 Present）
GetSerialization：KERB_INTERACTIVE_UNLOCK_LOGON(域/用户/密码) + 协商认证包
   ▼
LSASS 校验通过 → 解锁
```

组件与职责：

| 组件 | 位置 | 职责 |
|---|---|---|
| `src/BTUnlock.CredentialProvider`（C++，CI 用 MSVC 命令行编译，静态 CRT 零依赖） | LogonUI 进程内 | 仅 `CPUS_UNLOCK_WORKSTATION` 场景响应；其余场景 E_NOTIMPL 完全不参与。手机在场→tile+自动提交；不在场→0 tile（锁屏界面与原生一致） |
| `UnlockPipeServer`（C#，主程序内） | 用户会话 | 回答 `Q`：`A`（不可解锁）/ `P;user;domain;base64(密码)`；受理 `F;N`（CP 失败上报）回 `K`（M6.6）。放行门禁：启用 + 未熔断 + 令牌已配置且近 30s 有效 + 锁屏来源=本程序（M5）+ 已录入凭据 + 检测器 Present |
| `CredentialStore` + `MachineCrypto`（C#） | %ProgramData%\BTUnlock | 密码 DPAPI 加密落盘：v2 用户级（仅录入用户可解，M8），旧机器级密文首次读取自动迁移；ACL 收敛为 SYSTEM/Admins/当前用户 |

## 安装 / 卸载 / 回退

- 安装：下载 Release 的 `BTUnlock-UnlockProvider-win-x64-v*.zip`，管理员 PowerShell 运行 `install-cp.ps1`（复制 DLL 到 Program Files + regsvr32 注册 + 置启用开关）
- 前置：主程序运行中、已绑定手机、主界面勾选「启用」并录入 Windows 密码
- 卸载：`uninstall-cp.ps1`；**紧急停用**（不卸载）：
  `reg add HKLM\SOFTWARE\BTUnlock /v UnlockProviderEnabled /t REG_DWORD /d 0 /f`
  （CP 每次枚举都读该值，锁屏一次后生效）
- 日志：`C:\ProgramData\BTUnlock\cp.log`（时间戳+线程号，GetSerialization/轮询全链路）

## 安全模型（必读）

1. **Windows 无官方解锁 API**。实现 = 凭据提供程序提交你的密码本体，因此必须录入一次。
   密码落盘为 DPAPI 密文：v2 为**用户级**（仅录入用户的上下文可解，M8），旧版机器级密文自动迁移。
   若攻击者已能在录入用户上下文执行任意代码，该边界不增加防护（DPAPI 不防已攻陷会话，指导书 §6.3）；
   需明确：这不是安全增强功能，是便捷功能。完整威胁模型见 docs/09。
2. **仅解锁，不登录**：登录/UAC/改密场景一律不参与（E_NOTIMPL）。冷启动仍需密码/PIN。
3. **滚动令牌（M3.1；M6 起为解锁硬前置）**：静态 BLE 广播可被抓包克隆（30m 内）。未配置令牌时
   自动解锁不放行（fail-closed）。启用后伪造/过期（±20s 窗）帧被完全忽略，解锁只认有效帧。
   残余威胁为实时中继与窗口内重放（广播负载每 10 秒重复，逐帧去重不可行），见 docs/09。
4. 改 Windows 密码后需重新录入；解锁连续失败 3 次熔断暂停，重录密码恢复（M6.6）。
   手动锁屏（Win+L 等非本程序锁）不会被自动解锁（M5）。微软在线账户（MSA）不保证可用——实机验证是 docs/08 的 G0 关卡。

## 故障排查

| 症状 | 排查 |
|---|---|
| 手机在场但不出 tile | cp.log 有无 "GetCredentialCount: offer=1"；注册表开关；主程序是否运行；管道是否有进程占用 |
| tile 出现但解锁失败 | 密码错误（改密后未重录）→ 正常密码 tile 仍可用，登录后重新录入 |
| 解锁连续失败被熔断 | 主界面「录入 / 管理密码…」重新录入即恢复（M6.6）；旧密码失败原因见上一行 |
| 怀疑影响系统锁屏 | 紧急停用注册表命令 → 锁一次屏验证；仍异常则安全模式卸载（CP 随 Winlogon 栈加载，安全模式可删注册表） |
| 想看主程序侧 | 管道协议 `Q\n` → `P;user;domain;b64\n`，可用 PowerShell 管道客户端自测 |

## 已知边界（后续版本）

- 单用户会话假设（多用户切换场景未验证）
- `ICredentialProviderEvents` 直调（与微软动态 CP 示例一致，未做 GIT 封送）
- MSA 序列化兼容性未实测（G0 关卡）；M7 挑战应答评估结论见 docs/08 §6
