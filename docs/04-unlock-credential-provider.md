# 04 · 「人来解锁」技术方案（M3，未实现）

## 为什么锁屏容易、解锁难

- 锁屏：`LockWorkStation()` 一个调用，任何普通权限程序都能做。
- 解锁：Windows 出于安全设计，**没有公开 API** 解锁已锁定的会话。锁屏界面由
  LogonUI（winlogon 进程）绘制，唯一正规扩展点是 **Credential Provider（CP）**
  ——一组 COM 组件，登录/解锁界面上的每个登录方式（密码、PIN、指纹…）都是一个 CP。

## 方案总览

```
锁定画面出现时：
LogonUI 进程 ──加载──▶ BTUnlockCredentialProvider (自研 COM DLL, C++)
                          │ 枚举 UI 时判断：是否满足静默解锁条件
                          ▼
                    命名管道 \\.\pipe\btunlock\presence
                          ▲ 查询"手机是否在场(最近N秒内RSSI达标)"
                  BTUnlock.exe (托盘程序, 常驻, 写入状态)
                          ▲
                     BleScanner 判定 Present
```

1. 托盘程序判定 `Present`（含 M3 滚动令牌校验通过）→ 把「在场凭据」写入命名管道。
2. 自研 CP 在解锁界面出现时读管道：在场 → 返回一个自动通过的身份凭据，
   LogonUI 静默解锁；不在场 → 不出现，走正常密码/PIN。
3. CP 与主程序用 `CoCreateInstance` 的标准 CLSID 注册（HKLM/HKCR 注册表）。

## 安全模型（必须先想清楚再动手）

静态广播可被 30 米内任何人抓包克隆。解锁上线**必须**同时满足：

1. **仅解锁，不登录**：CP 的 `SetUsageScenario` 只响应
   `CPUS_UNLOCK_WORKSTATION`，冷启动/首次登录永远要求密码或 PIN——
   蓝牙钥匙丢了对号入座不了系统本体。
2. **滚动令牌**：见 [02-ble-protocol.md](02-ble-protocol.md) M3 预留，
   重放/中继旧帧无效。
3. **双因子开关**（可选）：解锁仍需轻量配合（如手机弹窗确认 / Windows Hello
   生物特征），由用户按敏感度自选。

威胁模型文档化结论：即便如此，物理接近 + 设备克隆仍是最强攻击面，文档与
README 需向用户明示「便捷换安全」的取舍。

## 实施步骤（建议顺序）

1. C++ COM DLL：实现 `ICredentialProvider` / `ICredentialProviderCredential`
   最小骨架（枚举一个隐藏 tile），先做到「注册后解锁画面不报错」。
2. 命名管道：主程序写（DACL 限本机 SYSTEM+当前用户），CP 读；定义
   `{state, lastSeenTs, tokenOk}` 简单 JSON/二进制帧。
3. 静默解锁逻辑：tile 不落 UI 直接 `GetSerialization` 返回缓存的凭据
   （配合 `CPGV_NO_UI` / 保存的凭据机制；密码如何安全托管是本步核心，
   参考 pGina/multiOTP 的做法——凭据加密存 DPAPI，仅解锁场景可解）。
4. 安装器：`regsvr32`/注册表写 CLSID + 卸载清理；签名（自签名也行，但需固定）。

## 参考

- 开源 CP 实现可借鉴结构：[multiOTPCredentialProvider](https://github.com/multiOTP/multiOTPCredentialProvider)（C++，V2 CP）
- pGina（C++ CP + 托管插件进程的架构分离思路）
- 官方文档：Windows Credential Provider（learn.microsoft.com）
- 微软官方「可信信号解锁」走 Windows Hello 企业版，个人机不可用，故自研。

## 调试技巧（届时用）

- CP 崩溃会黑屏锁死：虚拟机里先行；备好 PE/安全模式恢复注册表。
- `GetSerialization` 全链路日志写 `C:\ProgramData\BTUnlock\cp.log`（LogonUI
  会话下没有控制台）。
