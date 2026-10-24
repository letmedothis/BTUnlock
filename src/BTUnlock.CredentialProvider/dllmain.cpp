// BTUnlock Credential Provider —— COM 注册与入口
// 安装：管理员 PowerShell 运行 install-cp.ps1（内部调 regsvr32）
// 总开关：HKLM\SOFTWARE\BTUnlock\UnlockProviderEnabled (DWORD) —— 置 0 立即失效
#include "Common.h"
#include "Provider.h"
#include <new>

static HINSTANCE g_hModule = nullptr;

class BTUnlockClassFactory : public IClassFactory
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) *ppv = static_cast<IClassFactory*>(this);
        else { *ppv = nullptr; return E_NOINTERFACE; }
        AddRef();
        return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return 2; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto provider = new (std::nothrow) BTUnlockProvider();
        if (!provider) return E_OUTOFMEMORY;
        HRESULT hr = provider->QueryInterface(riid, ppv);
        provider->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL) override { return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (IsEqualCLSID(rclsid, __uuidof(BTUnlockProvider)))
    {
        static BTUnlockClassFactory factory;
        return factory.QueryInterface(riid, ppv);
    }
    return CLASS_E_CLASSNOTAVAILABLE;
}

STDAPI DllCanUnloadNow() { return S_FALSE; } // 跟随 LogonUI 生命周期即可

static LONG RegisterKey(HKEY root, const wchar_t* path, const wchar_t* valueName, const wchar_t* data)
{
    HKEY key = nullptr;
    LONG r = RegCreateKeyExW(root, path, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr);
    if (r == ERROR_SUCCESS)
    {
        r = RegSetValueExW(key, valueName, 0, REG_SZ, (const BYTE*)data, (DWORD)((wcslen(data) + 1) * sizeof(wchar_t)));
        RegCloseKey(key);
    }
    return r;
}

STDAPI DllRegisterServer()
{
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(g_hModule, path, MAX_PATH);

    wchar_t clsidPath[256];
    LONG r = RegisterKey(HKEY_LOCAL_MACHINE, L"Software\\Classes\\CLSID\\" BTUNLOCK_CLSID L"\\InprocServer32", nullptr, path);
    if (r == ERROR_SUCCESS) r = RegisterKey(HKEY_LOCAL_MACHINE, L"Software\\Classes\\CLSID\\" BTUNLOCK_CLSID L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    if (r == ERROR_SUCCESS)
    {
        swprintf_s(clsidPath, L"Software\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\%s", BTUNLOCK_CLSID);
        r = RegisterKey(HKEY_LOCAL_MACHINE, clsidPath, nullptr, L"BTUnlock 人来解锁");
    }
    if (r == ERROR_SUCCESS)
    {
        HKEY key = nullptr;
        if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, BTUNLOCK_REG_KEY, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) == ERROR_SUCCESS)
        {
            DWORD one = 1;
            RegSetValueExW(key, BTUNLOCK_REG_ENABLE_VALUE, 0, REG_DWORD, (const BYTE*)&one, sizeof(one));
            RegCloseKey(key);
        }
    }
    CpLog(L"DllRegisterServer: %ld path=%s", r, path);
    return r == ERROR_SUCCESS ? S_OK : SELFREG_E_CLASS;
}

STDAPI DllUnregisterServer()
{
    wchar_t clsidPath[256];
    swprintf_s(clsidPath, L"Software\\Classes\\CLSID\\%s", BTUNLOCK_CLSID);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, clsidPath);
    swprintf_s(clsidPath, L"Software\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\%s", BTUNLOCK_CLSID);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, clsidPath);
    CpLog(L"DllUnregisterServer done");
    return S_OK;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}
