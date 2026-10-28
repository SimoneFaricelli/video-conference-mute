#pragma once

#include "MediaStream.h"

namespace vcm
{
    // Custom media source loaded by the Windows Frame Server for the "VideoConference Mute" virtual camera.
    // Structure follows SimpleMediaSource of the Windows-Camera VirtualCamera sample (MIT).
    class MediaSource : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        Microsoft::WRL::ChainInterfaces<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>,
        IMFGetService, IKsControl, IMFSampleAllocatorControl>
    {
    public:
        ~MediaSource() override;

        HRESULT RuntimeClassInitialize(IMFAttributes* activateAttributes);

        // IMFMediaEventGenerator
        IFACEMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
        IFACEMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
        IFACEMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
        IFACEMETHODIMP QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status, const PROPVARIANT* value) override;

        // IMFMediaSource
        IFACEMETHODIMP CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor) override;
        IFACEMETHODIMP GetCharacteristics(DWORD* characteristics) override;
        IFACEMETHODIMP Pause() override;
        IFACEMETHODIMP Shutdown() override;
        IFACEMETHODIMP Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat, const PROPVARIANT* startPosition) override;
        IFACEMETHODIMP Stop() override;

        // IMFMediaSourceEx
        IFACEMETHODIMP GetSourceAttributes(IMFAttributes** attributes) override;
        IFACEMETHODIMP GetStreamAttributes(DWORD streamId, IMFAttributes** attributes) override;
        IFACEMETHODIMP SetD3DManager(IUnknown* manager) override;

        // IMFGetService
        IFACEMETHODIMP GetService(REFGUID service, REFIID riid, LPVOID* object) override;

        // IKsControl: no camera controls are exposed.
        IFACEMETHODIMP KsProperty(PKSPROPERTY property, ULONG propertyLength, LPVOID data, ULONG dataLength, ULONG* returned) override;
        IFACEMETHODIMP KsMethod(PKSMETHOD method, ULONG methodLength, LPVOID data, ULONG dataLength, ULONG* returned) override;
        IFACEMETHODIMP KsEvent(PKSEVENT event, ULONG eventLength, LPVOID data, ULONG dataLength, ULONG* returned) override;

        // IMFSampleAllocatorControl
        IFACEMETHODIMP SetDefaultAllocator(DWORD outputStreamId, IUnknown* allocator) override;
        IFACEMETHODIMP GetAllocatorUsage(DWORD outputStreamId, DWORD* inputStreamId, MFSampleAllocatorUsage* usage) override;

    private:
        enum class State { Invalid, Stopped, Started, Shutdown };

        HRESULT CheckShutdown() const;
        HRESULT CreateSourceAttributes(IMFAttributes* activateAttributes);

        mutable std::mutex m_lock;
        State m_state = State::Invalid;
        Microsoft::WRL::ComPtr<IMFMediaEventQueue> m_eventQueue;
        Microsoft::WRL::ComPtr<IMFPresentationDescriptor> m_presentation;
        Microsoft::WRL::ComPtr<IMFAttributes> m_attributes;
        Microsoft::WRL::ComPtr<MediaStream> m_stream;
        bool m_pipeReferenced = false;
    };
}
