#pragma once
#include "Common.h"

class BTUnlockCredential : public ICredentialProviderCredential2
{
public:
    BTUnlockCredential(class BTUnlockProvider* owner);
    ~BTUnlockCredential();

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ICredentialProviderCredential
    STDMETHODIMP Advise(ICredentialProviderCredentialEvents*) override { return E_NOTIMPL; }
    STDMETHODIMP UnAdvise() override { return S_OK; }
    STDMETHODIMP SetSelected(BOOL*) override;
    STDMETHODIMP SetDeselected() override { return S_OK; }
    STDMETHODIMP GetFieldState(DWORD, CREDENTIAL_PROVIDER_FIELD_STATE*, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE*) override;
    STDMETHODIMP GetStringValue(DWORD, LPWSTR*) override;
    STDMETHODIMP GetBitmapValue(DWORD, HBITMAP*) override { return E_NOTIMPL; }
    STDMETHODIMP GetCheckboxValue(DWORD, BOOL*, LPWSTR*) override { return E_NOTIMPL; }
    STDMETHODIMP GetComboBoxValueCount(DWORD, DWORD*, DWORD*) override { return E_NOTIMPL; }
    STDMETHODIMP GetComboBoxValueAt(DWORD, DWORD, LPWSTR*) override { return E_NOTIMPL; }
    STDMETHODIMP GetSubmitButtonValue(DWORD, DWORD*) override { return E_NOTIMPL; }
    STDMETHODIMP SetStringValue(DWORD, LPCWSTR) override { return E_NOTIMPL; }
    STDMETHODIMP SetCheckboxValue(DWORD, BOOL) override { return E_NOTIMPL; }
    STDMETHODIMP SetComboBoxSelectedValue(DWORD, DWORD) override { return E_NOTIMPL; }
    STDMETHODIMP CommandLinkClicked(DWORD) override { return E_NOTIMPL; }
    STDMETHODIMP GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE*,
        CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION*, LPWSTR*, CREDENTIAL_PROVIDER_STATUS_ICON*) override;

    // ICredentialProviderCredential2
    STDMETHODIMP GetUserSid(LPWSTR* ppszSid) override;

private:
    LONG _ref = 1;
    class BTUnlockProvider* _owner; // 构造时 AddRef，防悬挂（与 Provider 互引，随 LogonUI 退出释放）
};
