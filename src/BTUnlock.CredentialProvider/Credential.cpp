#include "Credential.h"
#include "Provider.h"
#include "PresenceChannel.h"

static const DWORD FIELD_LABEL = 0;
static const DWORD FIELD_COUNT = 1;

BTUnlockCredential::BTUnlockCredential(BTUnlockProvider* owner) : _owner(owner)
{
    _owner->AddRef();
    CpLog(L"Credential created");
}

BTUnlockCredential::~BTUnlockCredential()
{
    _owner->Release();
}

STDMETHODIMP BTUnlockCredential::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown || riid == __uuidof(ICredentialProviderCredential))
        *ppv = static_cast<ICredentialProviderCredential*>(this);
    else if (riid == __uuidof(ICredentialProviderCredential2))
        *ppv = static_cast<ICredentialProviderCredential2*>(this);
    else { *ppv = nullptr; return E_NOINTERFACE; }
    AddRef();
    return S_OK;
}
STDMETHODIMP_(ULONG) BTUnlockCredential::AddRef() { return InterlockedIncrement(&_ref); }
STDMETHODIMP_(ULONG) BTUnlockCredential::Release()
{
    LONG r = InterlockedDecrement(&_ref);
    if (r == 0) delete this;
    return r;
}

STDMETHODIMP BTUnlockCredential::SetSelected(BOOL* pbAutoAdvance)
{
    if (pbAutoAdvance) *pbAutoAdvance = FALSE;
    return S_OK;
}

STDMETHODIMP BTUnlockCredential::GetFieldState(DWORD field, CREDENTIAL_PROVIDER_FIELD_STATE* pcpfs,
    CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* pcpfis)
{
    if (!pcpfs || !pcpfis) return E_POINTER;
    *pcpfs = (field == FIELD_LABEL) ? CPFS_DISPLAY_FIELDDATA : CPFS_HIDDEN;
    *pcpfis = CPFIS_NONE;
    return S_OK;
}

STDMETHODIMP BTUnlockCredential::GetStringValue(DWORD field, LPWSTR* ppsz)
{
    if (field != FIELD_LABEL || !ppsz) return E_INVALIDARG;
    const wchar_t* text = L"手机在场 · 自动解锁";
    size_t bytes = (wcslen(text) + 1) * sizeof(wchar_t);
    *ppsz = (LPWSTR)CoTaskMemAlloc(bytes);
    if (!*ppsz) return E_OUTOFMEMORY;
    memcpy(*ppsz, text, bytes);
    return S_OK;
}

STDMETHODIMP BTUnlockCredential::GetUserSid(LPWSTR* ppszSid)
{
    if (!ppszSid) return E_POINTER;
    const wchar_t* sid = _owner->GetUserSid();
    if (!sid || !*sid)
    {
        *ppszSid = nullptr;
        return E_FAIL;
    }
    size_t bytes = (wcslen(sid) + 1) * sizeof(wchar_t);
    *ppszSid = (LPWSTR)CoTaskMemAlloc(bytes);
    if (!*ppszSid) return E_OUTOFMEMORY;
    memcpy(*ppszSid, sid, bytes);
    return S_OK;
}

// 组装 KERB_INTERACTIVE_UNLOCK_LOGON 自包含序列化块：
// [struct][domain][user][password]，UNICODE_STRING 指向块内偏移（同 SDK 示例做法）。
static HRESULT BuildUnlockBlob(const wchar_t* domain, const wchar_t* user, const wchar_t* password,
    KERB_INTERACTIVE_UNLOCK_LOGON** ppBlob, ULONG* pcbBlob)
{
    USHORT dLen = (USHORT)(wcslen(domain) * sizeof(wchar_t));
    USHORT uLen = (USHORT)(wcslen(user) * sizeof(wchar_t));
    USHORT pLen = (USHORT)(wcslen(password) * sizeof(wchar_t));
    ULONG total = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON) + dLen + uLen + pLen;

    PBYTE buf = (PBYTE)CoTaskMemAlloc(total);
    if (!buf) return E_OUTOFMEMORY;
    ZeroMemory(buf, total);

    auto kil = (KERB_INTERACTIVE_UNLOCK_LOGON*)buf;
    kil->Logon.MessageType = KerbInteractiveLogon;

    PBYTE cursor = buf + sizeof(KERB_INTERACTIVE_UNLOCK_LOGON);
    auto put = [&](const wchar_t* s, USHORT len, UNICODE_STRING* us)
    {
        us->Length = len;
        us->MaximumLength = len;
        us->Buffer = (PWSTR)cursor;
        memcpy(cursor, s, len);
        cursor += len;
    };
    put(domain, dLen, &kil->Logon.LogonDomainName);
    put(user, uLen, &kil->Logon.UserName);
    put(password, pLen, &kil->Logon.Password);
    kil->LogonId.QuadPart = 0;

    *ppBlob = kil;
    *pcbBlob = total;
    return S_OK;
}

STDMETHODIMP BTUnlockCredential::GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* pcpgsr,
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs, LPWSTR* ppszStatus, CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi)
{
    *pcpgsr = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
    *ppszStatus = nullptr;
    *pcpsi = CPSI_NONE;
    if (!pcpcs) return E_POINTER;

    PresenceInfo info;
    if (!QueryPresence(1800, info) || !info.Present || info.User.empty() || info.Password.empty())
    {
        CpLog(L"GetSerialization: phone not present / payload empty");
        return S_OK; // 未就绪：不出凭据，交回正常登录界面
    }

    // LSA 协商认证包号（微软官方示例同款做法，credui.dll 未文档化导出）
    ULONG authPackage = 0;
    HANDLE hLsa = nullptr;
    HMODULE hCredui = nullptr;
    NTSTATUS status = LsaConnectUntrusted(&hLsa);
    if (status == 0)
    {
        hCredui = LoadLibraryW(L"credui.dll");
        if (hCredui)
        {
            auto fn = (NTSTATUS(WINAPI*)(HANDLE, ULONG*))GetProcAddress(hCredui, "RetrieveNegotiateAuthPackage");
            if (fn) status = fn(hLsa, &authPackage);
        }
    }
    if (status != 0 || authPackage == 0)
    {
        CpLog(L"RetrieveNegotiateAuthPackage failed: 0x%08X", status);
        if (hLsa) LsaDeregisterLogonProcess(hLsa);
        if (hCredui) FreeLibrary(hCredui);
        return S_OK;
    }

    KERB_INTERACTIVE_UNLOCK_LOGON* blob = nullptr;
    ULONG cbBlob = 0;
    HRESULT hr = BuildUnlockBlob(info.Domain.c_str(), info.User.c_str(), info.Password.c_str(), &blob, &cbBlob);
    if (SUCCEEDED(hr))
    {
        pcpcs->clsidCredentialProvider = __uuidof(BTUnlockProvider);
        pcpcs->ulAuthenticationPackage = authPackage;
        pcpcs->pbSerialization = (PBYTE)blob;
        pcpcs->cbSerialization = cbBlob;
        *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;
        CpLog(L"GetSerialization: submitting credential (user=%s)", info.User.c_str());
    }

    if (hLsa) LsaDeregisterLogonProcess(hLsa);
    if (hCredui) FreeLibrary(hCredui);
    return hr;
}
