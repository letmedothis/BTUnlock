#include "Provider.h"
#include "Credential.h"
#include "PresenceChannel.h"

BTUnlockProvider::BTUnlockProvider()
{
    CpLog(L"Provider created");
}

BTUnlockProvider::~BTUnlockProvider()
{
    StopPollThread();
    if (_credential) { _credential->Release(); _credential = nullptr; }
    CpLog(L"Provider destroyed");
}

STDMETHODIMP BTUnlockProvider::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown || riid == __uuidof(ICredentialProvider))
        *ppv = static_cast<ICredentialProvider*>(this);
    else if (riid == __uuidof(ICredentialProviderSetUserArray))
        *ppv = static_cast<ICredentialProviderSetUserArray*>(this);
    else { *ppv = nullptr; return E_NOINTERFACE; }
    AddRef();
    return S_OK;
}
STDMETHODIMP_(ULONG) BTUnlockProvider::AddRef() { return InterlockedIncrement(&_ref); }
STDMETHODIMP_(ULONG) BTUnlockProvider::Release()
{
    LONG r = InterlockedDecrement(&_ref);
    if (r == 0) delete this;
    return r;
}

// 只响应解锁场景；登录/UAC/改密等一律 E_NOTIMPL 让系统跳过本提供程序。
STDMETHODIMP BTUnlockProvider::SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD)
{
    _cpus = cpus;
    CpLog(L"SetUsageScenario: %lu", cpus);
    return cpus == CPUS_UNLOCK_WORKSTATION ? S_OK : E_NOTIMPL;
}

STDMETHODIMP BTUnlockProvider::Advise(ICredentialProviderEvents* pcpe, UINT_PTR upAdviseContext)
{
    if (_events) _events->Release();
    _events = pcpe;
    if (_events) _events->AddRef();
    _adviseContext = upAdviseContext;
    StartPollThread(); // 有事件通道了才开轮询，状态变化时通知 LogonUI 重新枚举
    return S_OK;
}

STDMETHODIMP BTUnlockProvider::UnAdvise()
{
    StopPollThread();
    if (_events) { _events->Release(); _events = nullptr; }
    return S_OK;
}

STDMETHODIMP BTUnlockProvider::GetFieldDescriptorCount(DWORD* pdw)
{
    if (!pdw) return E_POINTER;
    *pdw = 1;
    return S_OK;
}

STDMETHODIMP BTUnlockProvider::GetFieldDescriptorAt(DWORD index, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** ppcpfd)
{
    if (!ppcpfd || index != 0) return E_INVALIDARG;
    const wchar_t* label = L"手机在场";
    size_t bytes = (wcslen(label) + 1) * sizeof(wchar_t);
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* fd =
        (CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*)CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR) + bytes);
    if (!fd) return E_OUTOFMEMORY;
    ZeroMemory(fd, sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
    fd->cpft = CPFT_LARGE_TEXT;
    fd->guidFieldType = GUID_NULL;
    fd->pszLabel = (LPWSTR)((PBYTE)fd + sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
    memcpy(fd->pszLabel, label, bytes);
    *ppcpfd = fd;
    return S_OK;
}

// 总开关：HKLM\SOFTWARE\BTUnlock\UnlockProviderEnabled。缺省视为关（装好后由安装脚本置 1）。
bool BTUnlockProvider::IsEnabledByRegistry() const
{
    DWORD v = 0, size = sizeof(v), type = 0;
    if (RegGetValueW(HKEY_LOCAL_MACHINE, BTUNLOCK_REG_KEY, BTUNLOCK_REG_ENABLE_VALUE,
        RRF_RT_REG_DWORD, &type, &v, &size) != ERROR_SUCCESS)
        return false;
    return v == 1;
}

STDMETHODIMP BTUnlockProvider::GetCredentialCount(DWORD* pdwCount, DWORD* pdwDefault, BOOL* pbAutoLogon)
{
    if (!pdwCount || !pdwDefault || !pbAutoLogon) return E_POINTER;

    bool offer = _cpus == CPUS_UNLOCK_WORKSTATION && IsEnabledByRegistry() && _present;
    if (offer)
    {
        if (!_credential) _credential = new BTUnlockCredential(this);
        *pdwCount = 1;
        *pdwDefault = 0;
        *pbAutoLogon = TRUE; // 关键：LogonUI 立即对默认 tile 调 GetSerialization，实现无感解锁
    }
    else
    {
        *pdwCount = 0;
        *pdwDefault = CREDENTIAL_PROVIDER_NO_DEFAULT;
        *pbAutoLogon = FALSE;
    }
    CpLog(L"GetCredentialCount: offer=%d present=%d", offer ? 1 : 0, _present ? 1 : 0);
    return S_OK;
}

STDMETHODIMP BTUnlockProvider::GetCredentialAt(DWORD index, ICredentialProviderCredential** ppcpc)
{
    if (!ppcpc || index != 0) return E_INVALIDARG;
    if (!_credential) _credential = new BTUnlockCredential(this);
    *ppcpc = _credential;
    (*ppcpc)->AddRef();
    return S_OK;
}

// Win8+ 锁屏界面把被锁定用户传进来（通常 1 个）。取第一个的 SID 供序列化分组。
STDMETHODIMP BTUnlockProvider::SetUserArray(ICredentialProviderUserArray* users)
{
    _sid.clear();
    DWORD count = 0;
    if (users && SUCCEEDED(users->GetCount(&count)) && count > 0)
    {
        ICredentialProviderUser* user = nullptr;
        if (SUCCEEDED(users->GetAt(0, &user)) && user)
        {
            LPWSTR sid = nullptr;
            if (SUCCEEDED(user->GetSid(&sid)) && sid)
            {
                _sid = sid;
                CoTaskMemFree(sid);
            }
            user->Release();
        }
    }
    CpLog(L"SetUserArray: sid=%s", _sid.c_str());
    return S_OK;
}

DWORD WINAPI BTUnlockProvider::PollThreadProc(LPVOID self)
{
    auto provider = (BTUnlockProvider*)self;
    bool last = false;
    while (!provider->_stop)
    {
        PresenceInfo info;
        bool now = QueryPresence(700, info);
        if (now != last)
        {
            provider->_present = now;
            provider->FireCredentialsChanged();
            CpLog(L"poll: presence %d -> %d", last ? 1 : 0, now ? 1 : 0);
            last = now;
        }
        for (int i = 0; i < 12 && !provider->_stop; i++) Sleep(100); // ~1.2s 轮询
    }
    return 0;
}

void BTUnlockProvider::StartPollThread()
{
    if (_thread) return;
    _stop = false;
    _thread = CreateThread(nullptr, 0, PollThreadProc, this, 0, nullptr);
}

void BTUnlockProvider::StopPollThread()
{
    if (!_thread) return;
    _stop = true;
    WaitForSingleObject(_thread, 4000);
    CloseHandle(_thread);
    _thread = nullptr;
}

void BTUnlockProvider::FireCredentialsChanged()
{
    if (_events)
    {
        CpLog(L"firing CredentialsChanged");
        _events->CredentialsChanged(_adviseContext);
    }
}
