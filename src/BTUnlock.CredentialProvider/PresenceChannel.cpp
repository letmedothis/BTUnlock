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

// 连接管道（重试到 deadline）；失败返回 INVALID_HANDLE_VALUE。
static HANDLE OpenPipe(DWORD timeoutMs)
{
    ULONGLONG deadline = GetTickCount64() + timeoutMs;
    while (GetTickCount64() < deadline)
    {
        HANDLE pipe = CreateFileW(BTUNLOCK_PIPE_NAME, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (pipe != INVALID_HANDLE_VALUE) return pipe;
        if (GetLastError() != ERROR_PIPE_BUSY && GetLastError() != ERROR_FILE_NOT_FOUND) return INVALID_HANDLE_VALUE;
        Sleep(60);
    }
    return INVALID_HANDLE_VALUE;
}

// 单次重叠 IO + 事件超时。返回 true 表示完成且传输字节数 > 0。
static bool IoWithTimeout(HANDLE pipe, HANDLE ev, bool write, void* buf, DWORD size, DWORD tmo)
{
    OVERLAPPED ov = {};
    ov.hEvent = ev;
    ResetEvent(ev);
    BOOL ok = write ? WriteFile(pipe, buf, size, nullptr, &ov) : ReadFile(pipe, buf, size, nullptr, &ov);
    if (!ok && GetLastError() != ERROR_IO_PENDING) return false;
    if (WaitForSingleObject(ev, tmo) != WAIT_OBJECT_0) { CancelIo(pipe); return false; }
    DWORD done = 0;
    return GetOverlappedResult(pipe, &ov, &done, FALSE) && done > 0;
}

// 带超时的管道问答。CreateFile 对忙管道立即失败，靠重试 + 总超时控制。
bool QueryPresence(DWORD timeoutMs, PresenceInfo& out)
{
    HANDLE pipe = OpenPipe(timeoutMs);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    HANDLE ev = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!ev) { CloseHandle(pipe); return false; }

    char req[] = "Q\n";
    char resp[1024] = {};
    bool sent = IoWithTimeout(pipe, ev, true, req, 2, 800);
    size_t got = 0;
    bool timedOut = false;
    while (sent && got < sizeof(resp) - 1)
    {
        if (!IoWithTimeout(pipe, ev, false, resp + got, (DWORD)(sizeof(resp) - 1 - got), 800)) { timedOut = true; break; }
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
    if (sscanf_s(resp + 2, "%127[^;];%127[^;];%511[^\n]", user, (unsigned)sizeof(user),
        domain, (unsigned)sizeof(domain), b64, (unsigned)sizeof(b64)) != 3)
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

bool ReportFailure(DWORD timeoutMs, int streak)
{
    HANDLE pipe = OpenPipe(timeoutMs);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    HANDLE ev = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!ev) { CloseHandle(pipe); return false; }

    char req[32] = {};
    sprintf_s(req, sizeof(req), "F;%d\n", streak);
    bool sent = IoWithTimeout(pipe, ev, true, req, (DWORD)strlen(req), 600);
    if (sent)
    {
        // 应答仅作确认；对端旧版本不回 K 时读超时，忽略
        char ack[8] = {};
        IoWithTimeout(pipe, ev, false, ack, sizeof(ack) - 1, 600);
    }

    CloseHandle(ev);
    CloseHandle(pipe);
    return sent;
}
