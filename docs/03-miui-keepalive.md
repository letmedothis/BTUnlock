# 03 · 小米/红米（MIUI/HyperOS）后台保活适配

这是安卓端**真正的难点**：MIUI/HyperOS 以激进杀后台著称，前台服务 + 常驻通知
只是及格线，不做好下面五项，广播迟早被系统静默停掉（表现：PC 莫名其妙锁屏）。

## 保活五件套（App 内置一键引导）

| # | 设置项 | 路径 | App 内实现 |
|---|---|---|---|
| ① | 自启动权限 | 安全中心 → 应用管理 → 自启动 | `MiuiIntents.OpenAutoStart`：跳 `com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` |
| ② | 省电策略无限制 | 设置 → 电量与性能 → 应用智能省电 | `OpenPowerKeeper`：跳 `com.miui.powerkeeper/com.miui.powerkeeper.ui.HiddenAppsConfigActivity` 并带 package 参数 |
| ③ | 电池优化白名单 | 系统设置 → 电池优化 | `OpenBatteryOptimization`：`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` |
| ④ | 通知权限 | 系统通知设置 | `OpenNotificationSettings`（Android 13+ 前台服务通知必需） |
| ⑤ | 最近任务锁定 | 最近任务卡片长按 → 锁定 | 无法代劳，`AlertDialog` 图文说明 |

①② 是 MIUI 私有能力，版本更迭可能失效——代码里全部 try/catch，失败降级到
系统应用详情页；⑤是手动操作。**验证标准：手机息屏放 30 分钟，PC 端仍能持续
收到广播。**

## 代码层的自愈措施（已实现）

1. **前台服务** `foregroundServiceType=connectedDevice` + 低优先级常驻通知 +
   `START_STICKY`（进程被杀后系统会尝试重建服务）。
2. **广播看门狗**（`AdvertiseController`）：
   - 每 60s 自检：`OnStartFailure` 回调触发或状态非广播中 → 自动重启广播；
   - 每 15 分钟无条件 stop/start 一次，对付「回调成功但射频层已静默」的僵死。
3. **开机自启**（`BootReceiver`）：仅在用户开启过服务时拉起；被 MIUI 拦截属预期，
   引导用户完成①。

## Android 版本相关

- Android 12+：`BLUETOOTH_ADVERTISE` / `BLUETOOTH_CONNECT` 运行时权限
  （`neverForLocation`，广播不涉位置）；`POST_NOTIFICATIONS`（13+）。
- Android ≤11：清单里的旧版 `BLUETOOTH`/`BLUETOOTH_ADMIN`（maxSdkVersion=30）。
- minSdk=26（通知渠道 API 起点，覆盖近年的小米/红米机型）。

## 已知坑与排查

| 症状 | 原因 | 处理 |
|---|---|---|
| 息屏几分钟后广播消失 | ②未设无限制 | 重新设置②，验证清单重测 |
| 重启手机后不自启 | ①未授权 或 BootReceiver 被拦 | 完成①；打开 App 手动开一次 |
| PC 端偶发锁屏但人在旁边 | 广播间隙 > 信号丢失超时；或环境衰减大 | PC 端调大「信号丢失超时」/ 调低锁屏阈值 |
| 开发者选项里的「MIUI 优化」 | 关闭有时能放开限制，但副作用大 | 仅测试用，不建议最终方案依赖 |
| `TooManyAdvertisers` 失败码 | 系统广播位被占满（少见） | 看门狗会持续重试，重启蓝牙即恢复 |
