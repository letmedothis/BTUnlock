#pragma once
#include "Common.h"

// 向主程序（托盘进程）的命名管道查询在场状态。
// 协议（一行文本，UTF-8）：
//   请求: "Q\n"
//   应答: "A\n"                         —— 不在场 / 未启用 / 未录入 / 已熔断
//         "P;user;domain;base64\n"      —— 在场，携带 UTF-16LE 密码的 base64
//   请求: "F;<连续失败次数>\n"           —— 上报解锁失败（M6.6），应答 "K\n"
// F 只由 Provider 的轮询线程发送（ReportResult 仅置标志），避免在 LogonUI 线程做管道 IO。
struct PresenceInfo
{
    bool Present = false;
    std::wstring User;
    std::wstring Domain;
    std::wstring Password;
};

// timeoutMs 内未应答按不在场处理（绝不阻塞锁屏界面）。
bool QueryPresence(DWORD timeoutMs, PresenceInfo& out);

// 上报解锁失败（连续第 streak 次）。发不出/超时只影响熔断联动，不影响解锁流程。
bool ReportFailure(DWORD timeoutMs, int streak);

// base64 解码到原始字节。
std::string Base64Decode(const char* src, size_t len);
