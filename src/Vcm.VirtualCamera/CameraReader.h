#pragma once

#include "CameraUtil.h"

namespace vcm
{
    // Receives frames from the asynchronous source reader and keeps only the latest one.
    class FrameSlot : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IMFSourceReaderCallback>
    {
    public:
        HRESULT RuntimeClassInitialize(HANDLE frameEvent);

        void Activate(IMFSourceReader* reader);
        void Deactivate();

        // Latest frame not taken yet, or null.
        Microsoft::WRL::ComPtr<IMFSample> Take();
        bool HasDelivered();
        bool HasFailed();

        // IMFSourceReaderCallback
        IFACEMETHODIMP OnReadSample(HRESULT status, DWORD streamIndex, DWORD streamFlags, LONGLONG timestamp, IMFSample* sample) override;
        IFACEMETHODIMP OnFlush(DWORD) override { return S_OK; }
        IFACEMETHODIMP OnEvent(DWORD, IMFMediaEvent*) override { return S_OK; }

    private:
        std::mutex m_lock;
        Microsoft::WRL::ComPtr<IMFSourceReader> m_reader;
        Microsoft::WRL::ComPtr<IMFSample> m_latest;
        wil::unique_handle m_frameEvent;
        bool m_active = false;
        bool m_delivered = false;
        bool m_failed = false;
    };

    // Streams the selected physical camera, converted (decoded, resized) to NV12 at the requested format.
    // Closing it releases the device, which turns the camera light off.
    class CameraReader
    {
    public:
        ~CameraReader() { Close(); }

        HRESULT Open(const std::wstring& symbolicLink, const VideoFormat& format, HANDLE frameEvent);
        void Close();

        bool IsOpen() const { return m_slot != nullptr; }
        const std::wstring& Link() const { return m_link; }
        bool HasDelivered() const { return m_slot && m_slot->HasDelivered(); }
        bool HasFailed() const { return m_slot && m_slot->HasFailed(); }
        Microsoft::WRL::ComPtr<IMFSample> Take() const { return m_slot ? m_slot->Take() : nullptr; }

    private:
        Microsoft::WRL::ComPtr<IMFMediaSource> m_device;
        Microsoft::WRL::ComPtr<IMFSourceReader> m_reader;
        Microsoft::WRL::ComPtr<FrameSlot> m_slot;
        std::wstring m_link;
    };
}
