#pragma once

#include "CameraUtil.h"

namespace vcm
{
    // The single video stream of the virtual camera. A pump thread delivers one NV12 sample per client request:
    // the physical camera frame, or the overlay picture while muted. With "power off when muted" the physical camera
    // is closed while muted and overlay frames are generated at the stream frame rate.
    class MediaStream : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        Microsoft::WRL::ChainInterfaces<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>>
    {
    public:
        HRESULT RuntimeClassInitialize(IMFMediaSource* parent, DWORD streamId, const std::vector<VideoFormat>& formats,
            const std::wstring& initialLink);

        // IMFMediaEventGenerator
        IFACEMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
        IFACEMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
        IFACEMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
        IFACEMETHODIMP QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status, const PROPVARIANT* value) override;

        // IMFMediaStream
        IFACEMETHODIMP GetMediaSource(IMFMediaSource** source) override;
        IFACEMETHODIMP GetStreamDescriptor(IMFStreamDescriptor** descriptor) override;
        IFACEMETHODIMP RequestSample(IUnknown* token) override;

        // IMFMediaStream2
        IFACEMETHODIMP SetStreamState(MF_STREAM_STATE state) override;
        IFACEMETHODIMP GetStreamState(MF_STREAM_STATE* state) override;

        HRESULT Start(IMFMediaType* type);
        HRESULT Stop(bool sendEvent);
        HRESULT Shutdown();
        HRESULT SetAllocator(IMFVideoSampleAllocator* allocator);
        IMFAttributes* Attributes() const { return m_attributes.Get(); }
        DWORD Id() const { return m_streamId; }

    private:
        HRESULT CheckShutdown() const;
        HRESULT StartInternal(IMFMediaType* type, bool sendEvent);
        void StopInternal();
        void StartPump();
        void StopPump(std::unique_lock<std::mutex>& lock);
        void Pump(VideoFormat format, Microsoft::WRL::ComPtr<IMFVideoSampleAllocator> allocator);
        bool Deliver(IMFVideoSampleAllocator* allocator, const VideoFormat& format, IMFSample* cameraFrame,
            const std::vector<uint8_t>* overlay);

        mutable std::mutex m_lock;
        Microsoft::WRL::ComPtr<IMFMediaSource> m_parent;
        Microsoft::WRL::ComPtr<IMFMediaEventQueue> m_eventQueue;
        Microsoft::WRL::ComPtr<IMFAttributes> m_attributes;
        Microsoft::WRL::ComPtr<IMFStreamDescriptor> m_descriptor;
        Microsoft::WRL::ComPtr<IMFVideoSampleAllocator> m_allocator;
        Microsoft::WRL::ComPtr<IMFMediaType> m_mediaType;
        std::deque<Microsoft::WRL::ComPtr<IUnknown>> m_requests;
        MF_STREAM_STATE m_state = MF_STREAM_STATE_STOPPED;
        bool m_shutdown = false;
        bool m_counted = false;
        DWORD m_streamId = 0;
        std::wstring m_initialLink;

        std::thread m_pump;
        wil::unique_event m_stopPump;
        wil::unique_event m_wake;
    };
}
