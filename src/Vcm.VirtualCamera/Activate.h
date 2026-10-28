#pragma once

#include "MediaSource.h"

namespace vcm
{
    // COM class registered under the media source CLSID. Frame Server sets the virtual camera attributes on it and
    // calls ActivateObject to get the media source (same pattern as VirtualCameraMediaSourceActivate in the sample).
    class __declspec(uuid("D0AB47C7-9EBA-4D7A-AF72-4769A4D62109")) Activate
        : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
            Microsoft::WRL::ChainInterfaces<IMFActivate, IMFAttributes>>
    {
    public:
        HRESULT RuntimeClassInitialize();

        // IMFActivate
        IFACEMETHODIMP ActivateObject(REFIID riid, void** object) override;
        IFACEMETHODIMP ShutdownObject() override;
        IFACEMETHODIMP DetachObject() override;

        // IMFAttributes, forwarded to an attribute store.
        IFACEMETHODIMP GetItem(REFGUID key, PROPVARIANT* value) override { return m_attributes->GetItem(key, value); }
        IFACEMETHODIMP GetItemType(REFGUID key, MF_ATTRIBUTE_TYPE* type) override { return m_attributes->GetItemType(key, type); }
        IFACEMETHODIMP CompareItem(REFGUID key, REFPROPVARIANT value, BOOL* result) override { return m_attributes->CompareItem(key, value, result); }
        IFACEMETHODIMP Compare(IMFAttributes* theirs, MF_ATTRIBUTES_MATCH_TYPE type, BOOL* result) override { return m_attributes->Compare(theirs, type, result); }
        IFACEMETHODIMP GetUINT32(REFGUID key, UINT32* value) override { return m_attributes->GetUINT32(key, value); }
        IFACEMETHODIMP GetUINT64(REFGUID key, UINT64* value) override { return m_attributes->GetUINT64(key, value); }
        IFACEMETHODIMP GetDouble(REFGUID key, double* value) override { return m_attributes->GetDouble(key, value); }
        IFACEMETHODIMP GetGUID(REFGUID key, GUID* value) override { return m_attributes->GetGUID(key, value); }
        IFACEMETHODIMP GetStringLength(REFGUID key, UINT32* length) override { return m_attributes->GetStringLength(key, length); }
        IFACEMETHODIMP GetString(REFGUID key, LPWSTR value, UINT32 size, UINT32* length) override { return m_attributes->GetString(key, value, size, length); }
        IFACEMETHODIMP GetAllocatedString(REFGUID key, LPWSTR* value, UINT32* length) override { return m_attributes->GetAllocatedString(key, value, length); }
        IFACEMETHODIMP GetBlobSize(REFGUID key, UINT32* size) override { return m_attributes->GetBlobSize(key, size); }
        IFACEMETHODIMP GetBlob(REFGUID key, UINT8* buffer, UINT32 size, UINT32* blobSize) override { return m_attributes->GetBlob(key, buffer, size, blobSize); }
        IFACEMETHODIMP GetAllocatedBlob(REFGUID key, UINT8** buffer, UINT32* size) override { return m_attributes->GetAllocatedBlob(key, buffer, size); }
        IFACEMETHODIMP GetUnknown(REFGUID key, REFIID riid, LPVOID* value) override { return m_attributes->GetUnknown(key, riid, value); }
        IFACEMETHODIMP SetItem(REFGUID key, REFPROPVARIANT value) override { return m_attributes->SetItem(key, value); }
        IFACEMETHODIMP DeleteItem(REFGUID key) override { return m_attributes->DeleteItem(key); }
        IFACEMETHODIMP DeleteAllItems() override { return m_attributes->DeleteAllItems(); }
        IFACEMETHODIMP SetUINT32(REFGUID key, UINT32 value) override { return m_attributes->SetUINT32(key, value); }
        IFACEMETHODIMP SetUINT64(REFGUID key, UINT64 value) override { return m_attributes->SetUINT64(key, value); }
        IFACEMETHODIMP SetDouble(REFGUID key, double value) override { return m_attributes->SetDouble(key, value); }
        IFACEMETHODIMP SetGUID(REFGUID key, REFGUID value) override { return m_attributes->SetGUID(key, value); }
        IFACEMETHODIMP SetString(REFGUID key, LPCWSTR value) override { return m_attributes->SetString(key, value); }
        IFACEMETHODIMP SetBlob(REFGUID key, const UINT8* buffer, UINT32 size) override { return m_attributes->SetBlob(key, buffer, size); }
        IFACEMETHODIMP SetUnknown(REFGUID key, IUnknown* value) override { return m_attributes->SetUnknown(key, value); }
        IFACEMETHODIMP LockStore() override { return m_attributes->LockStore(); }
        IFACEMETHODIMP UnlockStore() override { return m_attributes->UnlockStore(); }
        IFACEMETHODIMP GetCount(UINT32* count) override { return m_attributes->GetCount(count); }
        IFACEMETHODIMP GetItemByIndex(UINT32 index, GUID* key, PROPVARIANT* value) override { return m_attributes->GetItemByIndex(index, key, value); }
        IFACEMETHODIMP CopyAllItems(IMFAttributes* destination) override { return m_attributes->CopyAllItems(destination); }

    private:
        Microsoft::WRL::ComPtr<IMFAttributes> m_attributes;
        Microsoft::WRL::ComPtr<MediaSource> m_source;
        std::mutex m_lock;
    };
}
