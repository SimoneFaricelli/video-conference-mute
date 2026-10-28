#include "pch.h"
#include "MediaSource.h"
#include "Guids.h"
#include "PipeClient.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace vcm
{
    namespace
    {
        constexpr DWORD StreamId = 0;
        constexpr DWORD StateWaitMs = 1500;
    }

    MediaSource::~MediaSource()
    {
        if (m_pipeReferenced)
        {
            PipeClient::Instance().Release();
        }
    }

    HRESULT MediaSource::RuntimeClassInitialize(IMFAttributes* activateAttributes)
    {
        std::lock_guard lock(m_lock);
        InstallFailureLogging();

        // Ask the app which camera to wrap; fall back to the camera given at creation, then to the first one.
        PipeClient::Instance().AddRef();
        m_pipeReferenced = true;
        bool connected = PipeClient::Instance().WaitForState(StateWaitMs);
        std::wstring link = PipeClient::Instance().GetState().sourceLink;
        if ((link.empty() || IsVirtualCamera(link)) && activateAttributes)
        {
            wil::unique_cotaskmem_string value;
            UINT32 length = 0;
            if (SUCCEEDED(activateAttributes->GetAllocatedString(AttributeSourceLink, &value, &length)) && value)
            {
                link = value.get();
            }
        }
        if (link.empty() || IsVirtualCamera(link))
        {
            link = FindFirstPhysicalCamera();
        }
        VCM_LOG(L"Media source created (app %s), source camera: %s", connected ? L"connected" : L"not reachable",
            link.empty() ? L"<none>" : link.c_str());

        RETURN_IF_FAILED(CreateSourceAttributes(activateAttributes));
        RETURN_IF_FAILED(MFCreateEventQueue(&m_eventQueue));
        RETURN_IF_FAILED(MakeAndInitialize<MediaStream>(&m_stream, this, StreamId, GetOutputFormats(link), link));
        // On failure, break the stream -> source reference cycle so both are released.
        auto shutdownStream = wil::scope_exit([&] { m_stream->Shutdown(); m_stream.Reset(); });

        ComPtr<IMFStreamDescriptor> descriptor;
        RETURN_IF_FAILED(m_stream->GetStreamDescriptor(&descriptor));
        IMFStreamDescriptor* descriptors[] = { descriptor.Get() };
        RETURN_IF_FAILED(MFCreatePresentationDescriptor(1, descriptors, &m_presentation));
        shutdownStream.release();

        m_state = State::Stopped;
        return S_OK;
    }

    HRESULT MediaSource::CreateSourceAttributes(IMFAttributes* activateAttributes)
    {
        RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 4));
        if (activateAttributes)
        {
            RETURN_IF_FAILED(activateAttributes->CopyAllItems(m_attributes.Get()));
        }

        // The legacy profile is mandatory so that apps unaware of camera profiles keep working (see the sample).
        ComPtr<IMFSensorProfileCollection> profiles;
        ComPtr<IMFSensorProfile> profile;
        RETURN_IF_FAILED(MFCreateSensorProfileCollection(&profiles));
        RETURN_IF_FAILED(MFCreateSensorProfile(KSCAMERAPROFILE_Legacy, 0, nullptr, &profile));
        RETURN_IF_FAILED(profile->AddProfileFilter(StreamId, L"((RES==;FRT<=30,1;SUT==))"));
        RETURN_IF_FAILED(profiles->AddProfile(profile.Get()));
        RETURN_IF_FAILED(m_attributes->SetUnknown(MF_DEVICEMFT_SENSORPROFILE_COLLECTION, profiles.Get()));
        return S_OK;
    }

    HRESULT MediaSource::CheckShutdown() const
    {
        if (m_state == State::Shutdown) return MF_E_SHUTDOWN;
        if (!m_eventQueue || !m_stream) return E_UNEXPECTED;
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->BeginGetEvent(callback, state);
    }

    IFACEMETHODIMP MediaSource::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->EndGetEvent(result, event);
    }

    IFACEMETHODIMP MediaSource::GetEvent(DWORD flags, IMFMediaEvent** event)
    {
        ComPtr<IMFMediaEventQueue> queue;
        {
            std::lock_guard lock(m_lock);
            RETURN_IF_FAILED(CheckShutdown());
            queue = m_eventQueue;
        }
        return queue->GetEvent(flags, event);
    }

    IFACEMETHODIMP MediaSource::QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status, const PROPVARIANT* value)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->QueueEventParamVar(type, extendedType, status, value);
    }

    IFACEMETHODIMP MediaSource::CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor)
    {
        RETURN_HR_IF_NULL(E_POINTER, descriptor);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_presentation->Clone(descriptor);
    }

    IFACEMETHODIMP MediaSource::GetCharacteristics(DWORD* characteristics)
    {
        RETURN_HR_IF_NULL(E_POINTER, characteristics);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        *characteristics = MFMEDIASOURCE_IS_LIVE;
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::Pause()
    {
        return MF_E_INVALID_STATE_TRANSITION;
    }

    IFACEMETHODIMP MediaSource::Shutdown()
    {
        ComPtr<MediaStream> stream;
        {
            std::lock_guard lock(m_lock);
            if (m_state == State::Shutdown) return S_OK;
            m_state = State::Shutdown;
            stream = std::move(m_stream);
            if (m_eventQueue)
            {
                m_eventQueue->Shutdown();
                m_eventQueue.Reset();
            }
            m_presentation.Reset();
            m_attributes.Reset();
        }

        // Outside the lock: stopping the stream joins its pump thread.
        if (stream) stream->Shutdown();
        VCM_LOG(L"Media source shut down");
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat, const PROPVARIANT* startPosition)
    {
        RETURN_HR_IF(E_INVALIDARG, !descriptor || !startPosition);
        RETURN_HR_IF(MF_E_UNSUPPORTED_TIME_FORMAT, timeFormat && *timeFormat != GUID_NULL);

        ComPtr<MediaStream> stream;
        ComPtr<IMFMediaType> type;
        bool selected = false;
        bool wasSelected = false;
        {
            std::lock_guard lock(m_lock);
            RETURN_IF_FAILED(CheckShutdown());
            RETURN_HR_IF(MF_E_INVALID_STATE_TRANSITION, m_state == State::Invalid);

            DWORD count = 0;
            RETURN_IF_FAILED(descriptor->GetStreamDescriptorCount(&count));
            RETURN_HR_IF(E_INVALIDARG, count != 1);

            BOOL isSelected = FALSE;
            ComPtr<IMFStreamDescriptor> streamDescriptor;
            RETURN_IF_FAILED(descriptor->GetStreamDescriptorByIndex(0, &isSelected, &streamDescriptor));
            BOOL localSelected = FALSE;
            ComPtr<IMFStreamDescriptor> localDescriptor;
            RETURN_IF_FAILED(m_presentation->GetStreamDescriptorByIndex(0, &localSelected, &localDescriptor));
            wasSelected = localSelected && m_state == State::Started;
            selected = isSelected;

            if (selected)
            {
                ComPtr<IMFMediaTypeHandler> handler;
                RETURN_IF_FAILED(streamDescriptor->GetMediaTypeHandler(&handler));
                RETURN_IF_FAILED(handler->GetCurrentMediaType(&type));
                RETURN_IF_FAILED(m_presentation->SelectStream(0));
                RETURN_IF_FAILED(m_eventQueue->QueueEventParamUnk(wasSelected ? MEUpdatedStream : MENewStream, GUID_NULL, S_OK,
                    static_cast<IMFMediaStream*>(m_stream.Get())));
            }
            else
            {
                RETURN_IF_FAILED(m_presentation->DeselectStream(0));
            }
            stream = m_stream;
            m_state = State::Started;
        }

        if (selected)
        {
            RETURN_IF_FAILED(stream->Start(type.Get()));
        }
        else if (wasSelected)
        {
            RETURN_IF_FAILED(stream->Stop(false));
        }

        wil::unique_prop_variant time;
        RETURN_IF_FAILED(InitPropVariantFromInt64(MFGetSystemTime(), &time));
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, &time);
    }

    IFACEMETHODIMP MediaSource::Stop()
    {
        ComPtr<MediaStream> stream;
        {
            std::lock_guard lock(m_lock);
            RETURN_IF_FAILED(CheckShutdown());
            RETURN_HR_IF(MF_E_INVALID_STATE_TRANSITION, m_state != State::Started);
            m_state = State::Stopped;
            stream = m_stream;
        }

        RETURN_IF_FAILED(stream->Stop(true));

        wil::unique_prop_variant time;
        RETURN_IF_FAILED(InitPropVariantFromInt64(MFGetSystemTime(), &time));
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        RETURN_IF_FAILED(m_presentation->DeselectStream(0));
        return m_eventQueue->QueueEventParamVar(MESourceStopped, GUID_NULL, S_OK, &time);
    }

    IFACEMETHODIMP MediaSource::GetSourceAttributes(IMFAttributes** attributes)
    {
        RETURN_HR_IF_NULL(E_POINTER, attributes);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        // A video capture source must return a reference to its own store, not a copy.
        return m_attributes.CopyTo(attributes);
    }

    IFACEMETHODIMP MediaSource::GetStreamAttributes(DWORD streamId, IMFAttributes** attributes)
    {
        RETURN_HR_IF_NULL(E_POINTER, attributes);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        RETURN_HR_IF(MF_E_NOT_FOUND, streamId != m_stream->Id());
        *attributes = m_stream->Attributes();
        (*attributes)->AddRef();
        return S_OK;
    }

    IFACEMETHODIMP MediaSource::SetD3DManager(IUnknown*)
    {
        // Best effort only; frames are produced in system memory.
        return E_NOTIMPL;
    }

    IFACEMETHODIMP MediaSource::GetService(REFGUID, REFIID, LPVOID* object)
    {
        RETURN_HR_IF_NULL(E_POINTER, object);
        *object = nullptr;
        return MF_E_UNSUPPORTED_SERVICE;
    }

    IFACEMETHODIMP MediaSource::KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*)
    {
        // Same answer as an AVStream driver without a handler for the property set.
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaSource::KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaSource::KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*)
    {
        return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
    }

    IFACEMETHODIMP MediaSource::SetDefaultAllocator(DWORD outputStreamId, IUnknown* allocator)
    {
        RETURN_HR_IF_NULL(E_POINTER, allocator);
        ComPtr<MediaStream> stream;
        {
            std::lock_guard lock(m_lock);
            RETURN_IF_FAILED(CheckShutdown());
            RETURN_HR_IF(MF_E_NOT_FOUND, outputStreamId != m_stream->Id());
            stream = m_stream;
        }
        ComPtr<IMFVideoSampleAllocator> videoAllocator;
        RETURN_IF_FAILED(allocator->QueryInterface(IID_PPV_ARGS(&videoAllocator)));
        return stream->SetAllocator(videoAllocator.Get());
    }

    IFACEMETHODIMP MediaSource::GetAllocatorUsage(DWORD outputStreamId, DWORD* inputStreamId, MFSampleAllocatorUsage* usage)
    {
        RETURN_HR_IF(E_POINTER, !inputStreamId || !usage);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        RETURN_HR_IF(MF_E_NOT_FOUND, outputStreamId != m_stream->Id());
        *inputStreamId = outputStreamId;
        *usage = MFSampleAllocatorUsage_UsesProvidedAllocator;
        return S_OK;
    }
}
