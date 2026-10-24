#pragma once
#include "Common.h"

// LogonUI 里的枚举与自动提交逻辑：
//   手机在场 + 注册表开关打开 → 暴露 1 个 tile 并置 pbAutoLogonWithDefault=TRUE，
//   LogonUI 立即对它调用 GetSerialization → 静默解锁；
//   否则 0 个 tile，锁屏界面与原生完全一致。
class BTUnlockProvider : public ICredentialProvider, public ICredentialProviderSetUserArray
{
public:
    BTUnlockProvider();
    ~BTUnlockProvider();

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ICredentialProvider
    STDMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD) override;
    STDMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION*) override { return E_NOTIMPL; }
    STDMETHODIMP Advise(ICredentialProviderEvents* pcpe, UINT_PTR upAdviseContext) override;
    STDMETHODIMP UnAdvise() override;
    STDMETHODIMP GetFieldDescriptorCount(DWORD*) override;
    STDMETHODIMP GetFieldDescriptorAt(DWORD, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR**) override;
    STDMETHODIMP GetCredentialCount(DWORD*, DWORD*, BOOL*) override;
    STDMETHODIMP GetCredentialAt(DWORD, ICredentialProviderCredential**) override;

    // ICredentialProviderSetUserArray
    STDMETHODIMP SetUserArray(ICredentialProviderUserArray*) override;

    const wchar_t* GetUserSid() const { return _sid.c_str(); }

private:
    static DWORD WINAPI PollThreadProc(LPVOID self);
    void StartPollThread();
    void StopPollThread();
    bool IsEnabledByRegistry() const;
    void FireCredentialsChanged();

    LONG _ref = 1;
    DWORD _cpus = 0;
    volatile bool _present = false;
    std::wstring _sid;

    ICredentialProviderEvents* _events = nullptr; // LogonUI 传入（直调凭据事件，动态 CP 示例通行做法）
    UINT_PTR _adviseContext = 0;
    HANDLE _thread = nullptr;
    volatile bool _stop = false;

    class BTUnlockCredential* _credential = nullptr; // 唯一 tile（对 LogonUI 暴露，被其 AddRef）
};
