# AGENTS.md — 开发约束（每次开发前必读，不可绕过）

用户声明（2026-11-21，长期有效）：

## 铁律：本机只写代码，不调试、不构建

1. **本机禁止任何调试、运行、编译、构建。** 本机没有 .NET SDK / Android SDK / JDK / MSVC 工具链，也不允许安装（残留的 8.0.426 空壳目录不是可用 SDK）。本机 `dotnet` / `adb` / MSBuild 不可用属预期，不要尝试，更不要因此去装工具链。
2. **编码依据只有网络文档与问答**：learn.microsoft.com、developer.android.com、上游官方示例、仓库内已有代码先例。拿不准的 API / 签名 / 枚举名，先查在线文档和 docs/ 里已记录的踩坑，绝不靠本地编译试错。
3. **构建与打包只走 GitHub Actions**（`.github/workflows/build.yml`）：push → CI 三端构建 → main 分支自动 Release。纯文档改动（`docs/**`、`*.md`）paths-ignore 不触发 CI 属预期。
4. **功能验收只发生在用户的真实设备上**：用户用 CI 产物（Releases / artifacts）装机测试并反馈结果。CI 全绿 ≠ 功能通过；Agent 不得把未实机验证的能力描述为已验证。

## 开发习惯

- 小步提交、尽快推送，用 Actions 日志暴露编译错误并据此修正；失败读日志，不在本地复现。
- 动 WPF/WinForms、Android BLE、C++ CP 相关文件前，先查 README 与 docs/ 的已知坑（Application/MessageBox/Brush/Color/Point 二义性、`global::Android.` 前缀、广播 31 字节上限、CP 接口真名等）。
- 当前路线图与里程碑见 [docs/08-roadmap.md](docs/08-roadmap.md)；解锁组件改动先读 [docs/04-unlock-credential-provider.md](docs/04-unlock-credential-provider.md)，不得扩大解锁范围。
