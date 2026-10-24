#pragma once
// BTUnlock Credential Provider 公共头
// 约定：本 DLL 运行在 LogonUI（SYSTEM）进程内，任何失败都必须静默降级——
// 拿不到状态就一个 tile 都不出，绝不能抛异常/崩溃把锁屏搞死。
#include <windows.h>
#include <objbase.h>
#include <credentialprovider.h>
#include <ntsecapi.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <string>

// {D4A3B7C1-52E9-4F6A-8B2D-9C0E1F3A5B7D} —— 本提供程序 CLSID，注册表与安装脚本共用，勿改
class __declspec(uuid("D4A3B7C1-52E9-4F6A-8B2D-9C0E1F3A5B7D")) BTUnlockProvider;

#define BTUNLOCK_CLSID L"{D4A3B7C1-52E9-4F6A-8B2D-9C0E1F3A5B7D}"
#define BTUNLOCK_REG_KEY L"SOFTWARE\\BTUnlock"
#define BTUNLOCK_REG_ENABLE_VALUE L"UnlockProviderEnabled"
#define BTUNLOCK_PIPE_NAME L"\\\\.\\pipe\\btunlock\\presence"
#define BTUNLOCK_LOG_FILE L"C:\\ProgramData\\BTUnlock\\cp.log"

void CpLog(const wchar_t* fmt, ...);
