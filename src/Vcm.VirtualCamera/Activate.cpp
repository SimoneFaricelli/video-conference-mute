#include "pch.h"
#include "Activate.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace vcm
{
    HRESULT Activate::RuntimeClassInitialize()
    {
        InstallFailureLogging();
        RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 4));
        return S_OK;
    }

    IFACEMETHODIMP Activate::ActivateObject(REFIID riid, void** object)
    {
        RETURN_HR_IF_NULL(E_POINTER, object);
        *object = nullptr;

        // A new source for every activation, as in the sample: a cached one may already have been shut down.
        ComPtr<MediaSource> source;
        RETURN_IF_FAILED(MakeAndInitialize<MediaSource>(&source, m_attributes.Get()));
        std::lock_guard lock(m_lock);
        m_source = source;
        return source->QueryInterface(riid, object);
    }

    IFACEMETHODIMP Activate::ShutdownObject()
    {
        std::lock_guard lock(m_lock);
        if (m_source)
        {
            m_source->Shutdown();
            m_source.Reset();
        }
        return S_OK;
    }

    IFACEMETHODIMP Activate::DetachObject()
    {
        std::lock_guard lock(m_lock);
        m_source.Reset();
        return S_OK;
    }
}
