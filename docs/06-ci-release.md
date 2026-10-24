# 06 · GitHub Actions 打包与发布

流水线定义：[`.github/workflows/build.yml`](../.github/workflows/build.yml)，
无需配置任何 Secret（使用内置 `GITHUB_TOKEN`）。

## 触发与产物

| 事件 | 动作 | 产物在哪下载 |
|---|---|---|
| push 到任意分支 | 构建两端 | 该次 Action 运行页的 Artifacts（7 天有效，测试用） |
| push 到 `main` | 构建 + **自动创建 Release** | 仓库 **Releases 页**（永久保存） |
| 手动触发（workflow_dispatch） | 同 push 到该分支的行为 | 同上 |

产物清单：

- `BTUnlock-Desktop-win-x64-v0.1.N.zip` —— PC 端自包含单文件（无需装 .NET），附 `.sha256` 校验
- `BTUnlock-Android-v0.1.N.apk` —— 安卓端安装包（附 `.sha256`）

## 版本号：CI 自动递增，不回写仓库

版本号 = **`0.1.<GITHUB_RUN_NUMBER>`**（run_number 是仓库级自增整数，每次
触发流水线 +1），构建时通过 `-p:Version` 注入两端：

- PC：程序集/文件版本 + 主窗口标题显示
- Android：`ApplicationDisplayVersion`（版本名）与 `ApplicationVersion`
  （versionCode，整数）

**为什么不把版本号提交回仓库**：CI 提交版本号会产生新的 push → 再次触发 CI
→ 死循环，必须引入跳过标记来打破；业界通行做法是版本只在构建时派生。
仓库里 csproj 的 `0.1.0` 只是本地构建的占位默认值，不代表发布版本。

Tag 与 Release 一一对应：`v0.1.N`。

## 更新说明（下载页展示的内容）

Release 正文用 GitHub 的 `--generate-notes` 自动生成，内容 = **上一个版本
tag 到本次 tag 之间的全部提交记录**（含 PR 链接、贡献者）。

因此约定：**提交信息写清楚做了什么**（建议 Conventional Commits 风格：
`feat: 手机端新增 XX`、`fix: 修复 XX 误锁`），下载页的更新说明就自然可读。
想手工润色某个版本：到 Releases 页编辑该 Release 的正文即可，不会被覆盖
（只有新建时才生成）。

## 安卓签名说明

APK 用**仓库内固定密钥**签名（`src/BTUnlock.Android/keystore/btunlock.keystore`，
Release 配置见 csproj）。这是为了让 CI 产物可以覆盖安装、设备 ID 不因重装丢失——
否则 CI 每次运行生成的临时调试密钥都不同，每次升级都要卸载重装。

代价与红线：私有仓库自用场景下密钥入库（含明文密码）是可接受的折中；
**严禁**把该密钥用到公开仓库或 Play 上架（等于交出签名身份）。换密钥 =
所有已装用户必须卸载重装并重新绑定。

## 重跑与幂等

同一 run 重跑（Re-run jobs）时版本号不变，release 任务会先删除同名
Release/Tag 再重建，不会报"tag 已存在"。**避免**对历史 run 补跑（会产生
比最新版本号小的"新"发布）。

## 本地验证 workflow 语法（可选）

```bash
# 装 act 后在本地容器里模拟（安卓 workload 体积大，建议只验证 job 结构）
act push -j build-pc
```
