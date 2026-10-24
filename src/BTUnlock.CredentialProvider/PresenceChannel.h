#pragma once
#include "Common.h"

// 向主程序（托盘进程）的命名管道查询在场状态。
// 协议（一行文本，UTF-8）：
//   请求: "Q\n"
//   应答: "A\n"                         —— 不在场 / 未启用 / 未录入
//         "P;user;domain;base64\n"      —— 在场，携带 UTF-16LE 密码的 base64
struct PresenceInfo
{
    bool Present = false;
    std::wstring User;
    std::wstring Domain;
    std::wstring Password;
};

// timeoutMs 内未应答按不在场处理（绝不阻塞锁屏界面）。
bool QueryPresence(DWORD timeoutMs, PresenceInfo& out);

// base64 解码到原始字节。
std::string Base64Decode(const char* src, size_t len);
