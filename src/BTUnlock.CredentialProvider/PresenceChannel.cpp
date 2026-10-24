#include "PresenceChannel.h"

static const char B64[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

std::string Base64Decode(const char* src, size_t len)
{
    static short rev[256];
    static bool init = false;
    if (!init)
    {
        for (int i = 0; i < 256; i++) rev[i] = -1;
        for (int i = 0; i < 64; i++) rev[(unsigned char)B64[i]] = (short)i;
        init = true;
    }
    std::string out;
    int val = 0, bits = 0;
    for (size_t i = 0; i < len; i++)
    {
        char c = src[i];
        if (c == '=' || c == '\n' || c == '\r') continue;
        short d = rev[(unsigned char)c];
        if (d < 0) continue;
        val = (val << 6) | d;
        bits += 6;
        if (bits >= 8)
        {
            bits -= 8;
            out.push_back((char)((val >> bits) & 0xFF));
        }
    }
    return out;
}

// 带超时的管道问答。CreateFile 对忙管道立即失败，靠重试 + 总超时控制。
bool QueryPresence(DWORD timeoutMs, PresenceInfo& out)
{
    ULONGLONG deadline = GetTickCount64() + timeoutMs;

    HANDLE pipe = INVALID_HANDLE_VALUE;
    while (GetTickCount64() < deadline)
    {
        pipe = CreateFileW(BTUNLOCK_PIPE_NAME, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (pipe != INVALID_HANDLE_VALUE) break;
        if (GetLastError() != ERROR_PIPE_BUSY && GetLastError() != ERROR_FILE_NOT_FOUND) return false;
        Sleep(60);
    }
    if (pipe == INVALID_HANDLE_VALUE) return false;

    // 统一用重叠 IO + 事件实现读写超时
    HANDLE ev = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!ev) { CloseHandle(pipe); return false; }

    auto io = [&](bool write, void* buf, DWORD size, DWORD tmo) -> bool
    {
        OVERLAPPED ov = {};
        ov.hEvent = ev;
        ResetEvent(ev);
        BOOL ok = write ? WriteFile(pipe, buf, size, nullptr, &ov) : ReadFile(pipe, buf, size, nullptr, &ov);
        if (!ok && GetLastError() != ERROR_IO_PENDING) return false;
        if (WaitForSingleObject(ev, tmo) != WAIT_OBJECT_0) { CancelIo(pipe); return false; }
        DWORD done = 0;
        return GetOverlappedResult(pipe, &ov, &done, FALSE) && done > 0;
    };

    char req[] = "Q\n";
    char resp[1024] = {};
    bool sent = io(true, req, 2, 800);
    size_t got = 0;
    bool timedOut = false;
    while (sent && got < sizeof(resp) - 1)
    {
        if (!io(false, resp + got, (DWORD)(sizeof(resp) - 1 - got), 800)) { timedOut = true; break; }
        // 逐次读到 '\n' 为止
        bool done = false;
        for (size_t i = got; i < got + strnlen_s(resp + got, sizeof(resp) - got); i++)
            if (resp[i] == '\n') { done = true; break; }
        got += strnlen_s(resp + got, sizeof(resp) - got);
        if (done) break;
    }

    CloseHandle(ev);
    CloseHandle(pipe);

    if (!sent || timedOut || got == 0) return false;

    // 解析 "P;user;domain;b64"
    resp[got] = 0;
    if (resp[0] != 'P' || resp[1] != ';') return false;

    char user[128] = {}, domain[128] = {}, b64[512] = {};
    if (sscanf_s(resp + 2, "%127[^;];%127[^;];%511[^\n]", user, (rsize_t)sizeof(user),
        domain, (rsize_t)sizeof(domain), b64, (rsize_t)sizeof(b64)) != 3)
        return false;

    std::string pwBytes = Base64Decode(b64, strlen(b64));
    // UTF-16LE → wstring（跳过可能的奇数尾字节）
    std::wstring pw(reinterpret_cast<const wchar_t*>(pwBytes.data()), pwBytes.size() / sizeof(wchar_t));

    int wLen = MultiByteToWideChar(CP_UTF8, 0, user, -1, nullptr, 0);
    std::wstring wUser(wLen, L'\0'); MultiByteToWideChar(CP_UTF8, 0, user, -1, wUser.data(), wLen);
    wLen = MultiByteToWideChar(CP_UTF8, 0, domain, -1, nullptr, 0);
    std::wstring wDomain(wLen, L'\0'); MultiByteToWideChar(CP_UTF8, 0, domain, -1, wDomain.data(), wLen);
    if (!wUser.empty() && wUser.back() == L'\0') wUser.pop_back();
    if (!wDomain.empty() && wDomain.back() == L'\0') wDomain.pop_back();

    out.Present = true;
    out.User = std::move(wUser);
    out.Domain = std::move(wDomain);
    out.Password = std::move(pw);
    return true;
}
