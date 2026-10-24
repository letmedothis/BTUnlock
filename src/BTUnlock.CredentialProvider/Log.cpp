#include "Common.h"

// 尽力而为的文件日志：失败静默。LogonUI 环境没有控制台，排查全靠它。
void CpLog(const wchar_t* fmt, ...)
{
    HANDLE h = CreateFileW(BTUNLOCK_LOG_FILE, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;

    wchar_t line[1024];
    SYSTEMTIME st;
    GetLocalTime(&st);
    int head = swprintf_s(line, L"[%02d:%02d:%02d.%03d tid=%lu] ", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, GetCurrentThreadId());

    va_list args;
    va_start(args, fmt);
    _vsnwprintf_s(line + head, _countof(line) - head - 1, _TRUNCATE, fmt, args);
    va_end(args);
    wcscat_s(line, L"\r\n");

    DWORD written = 0;
    WriteFile(h, line, (DWORD)(wcslen(line) * sizeof(wchar_t)), &written, nullptr);
    CloseHandle(h);
}
