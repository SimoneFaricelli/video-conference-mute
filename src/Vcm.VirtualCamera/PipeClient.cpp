#include "pch.h"
#include "PipeClient.h"

namespace vcm
{
    namespace
    {
        void PutU32(std::vector<uint8_t>& out, uint32_t value)
        {
            for (int i = 0; i < 4; i++) out.push_back(static_cast<uint8_t>(value >> (8 * i)));
        }

        uint32_t GetU32(const uint8_t* p)
        {
            return p[0] | (p[1] << 8) | (p[2] << 16) | (static_cast<uint32_t>(p[3]) << 24);
        }

        std::vector<uint8_t> Frame(protocol::MessageType type, const std::vector<uint8_t>& payload)
        {
            std::vector<uint8_t> message;
            message.reserve(protocol::HeaderSize + payload.size());
            PutU32(message, static_cast<uint32_t>(type));
            PutU32(message, static_cast<uint32_t>(payload.size()));
            message.insert(message.end(), payload.begin(), payload.end());
            return message;
        }
    }

    PipeClient& PipeClient::Instance()
    {
        static PipeClient instance;
        return instance;
    }

    void PipeClient::AddRef()
    {
        std::lock_guard lock(m_lock);
        if (m_refs++ == 0)
        {
            m_stop.create(wil::EventOptions::ManualReset);
            m_thread = std::thread([this] { Run(); });
        }
    }

    void PipeClient::Release()
    {
        std::thread thread;
        {
            std::lock_guard lock(m_lock);
            if (m_refs == 0 || --m_refs > 0)
            {
                return;
            }
            m_stop.SetEvent();
            thread = std::move(m_thread);
        }

        if (thread.joinable())
        {
            thread.join();
        }

        std::lock_guard lock(m_lock);
        m_state.connected = false;
        m_stateReceived = false;
    }

    bool PipeClient::IsRunning()
    {
        std::lock_guard lock(m_lock);
        return m_refs > 0;
    }

    CameraState PipeClient::GetState()
    {
        std::lock_guard lock(m_lock);
        return m_state;
    }

    std::shared_ptr<const OverlayImage> PipeClient::GetOverlay()
    {
        std::lock_guard lock(m_lock);
        return m_overlay;
    }

    bool PipeClient::WaitForState(DWORD timeoutMs)
    {
        std::unique_lock lock(m_lock);
        return m_stateArrived.wait_for(lock, std::chrono::milliseconds(timeoutMs), [this] { return m_stateReceived; });
    }

    void PipeClient::StreamStarted()
    {
        bool notify;
        {
            std::lock_guard lock(m_lock);
            notify = m_streaming++ == 0;
        }
        if (notify) SendInUse(true);
    }

    void PipeClient::StreamStopped()
    {
        bool notify;
        {
            std::lock_guard lock(m_lock);
            if (m_streaming == 0) return;
            notify = --m_streaming == 0;
        }
        if (notify) SendInUse(false);
    }

    void PipeClient::SendInUse(bool inUse)
    {
        Send(Frame(protocol::MessageType::InUse, { static_cast<uint8_t>(inUse ? 1 : 0) }));
    }

    void PipeClient::Send(const std::vector<uint8_t>& message)
    {
        std::lock_guard lock(m_writeLock);
        if (!m_pipe)
        {
            return; // the current state is sent again after (re)connecting
        }

        wil::unique_event done(wil::EventOptions::ManualReset);
        OVERLAPPED overlapped{};
        overlapped.hEvent = done.get();
        DWORD written = 0;
        if (!WriteFile(m_pipe, message.data(), static_cast<DWORD>(message.size()), nullptr, &overlapped) &&
            GetLastError() != ERROR_IO_PENDING)
        {
            VCM_LOG(L"WriteFile failed: %lu", GetLastError());
            return;
        }
        if (!GetOverlappedResult(m_pipe, &overlapped, &written, TRUE))
        {
            VCM_LOG(L"Write did not complete: %lu", GetLastError());
        }
    }

    void PipeClient::Run()
    {
        auto coInit = wil::CoInitializeEx(COINIT_MULTITHREADED);
        DWORD lastError = 0;

        while (WaitForSingleObject(m_stop.get(), 0) != WAIT_OBJECT_0)
        {
            wil::unique_hfile pipe(CreateFileW(protocol::PipePath, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED, nullptr));
            if (!pipe)
            {
                DWORD error = GetLastError();
                if (error != lastError)
                {
                    // ERROR_FILE_NOT_FOUND: app not running; ERROR_ACCESS_DENIED: pipe ACL does not admit this account.
                    VCM_LOG(L"Cannot open %s: error %lu", protocol::PipePath, error);
                    lastError = error;
                }
                if (error == ERROR_PIPE_BUSY)
                {
                    WaitNamedPipeW(protocol::PipePath, 1000);
                }
                else
                {
                    WaitForSingleObject(m_stop.get(), 1000);
                }
                continue;
            }

            lastError = 0;
            VCM_LOG(L"Connected to the VCM app");
            {
                std::lock_guard lock(m_writeLock);
                m_pipe = pipe.get();
            }

            std::vector<uint8_t> hello;
            PutU32(hello, protocol::Version);
            PutU32(hello, GetCurrentProcessId());
            Send(Frame(protocol::MessageType::Hello, hello));
            bool streaming;
            {
                std::lock_guard lock(m_lock);
                streaming = m_streaming > 0;
            }
            SendInUse(streaming);

            RunSession(pipe.get());

            {
                std::lock_guard lock(m_writeLock);
                m_pipe = nullptr;
            }
            {
                std::lock_guard lock(m_lock);
                m_state.connected = false;
            }
            VCM_LOG(L"Disconnected from the VCM app");
            WaitForSingleObject(m_stop.get(), 500);
        }
    }

    bool PipeClient::RunSession(HANDLE pipe)
    {
        uint8_t header[protocol::HeaderSize];
        while (ReadExact(pipe, header, protocol::HeaderSize))
        {
            auto type = static_cast<protocol::MessageType>(GetU32(header));
            uint32_t length = GetU32(header + 4);
            if (length > protocol::MaxPayloadSize)
            {
                VCM_LOG(L"Invalid message length %u", length);
                return false;
            }

            std::vector<uint8_t> payload(length);
            if (length > 0 && !ReadExact(pipe, payload.data(), length))
            {
                return false;
            }
            HandleMessage(type, payload);
        }
        return false;
    }

    bool PipeClient::ReadExact(HANDLE pipe, uint8_t* buffer, uint32_t size)
    {
        wil::unique_event done(wil::EventOptions::ManualReset);
        uint32_t total = 0;
        while (total < size)
        {
            OVERLAPPED overlapped{};
            overlapped.hEvent = done.get();
            done.ResetEvent();
            DWORD read = 0;
            if (!ReadFile(pipe, buffer + total, size - total, nullptr, &overlapped) && GetLastError() != ERROR_IO_PENDING)
            {
                return false;
            }

            HANDLE handles[] = { done.get(), m_stop.get() };
            if (WaitForMultipleObjects(2, handles, FALSE, INFINITE) != WAIT_OBJECT_0)
            {
                CancelIoEx(pipe, &overlapped);
                GetOverlappedResult(pipe, &overlapped, &read, TRUE);
                return false;
            }
            if (!GetOverlappedResult(pipe, &overlapped, &read, FALSE) || read == 0)
            {
                return false;
            }
            total += read;
        }
        return true;
    }

    void PipeClient::HandleMessage(protocol::MessageType type, const std::vector<uint8_t>& payload)
    {
        switch (type)
        {
        case protocol::MessageType::State:
        {
            if (payload.size() < 12) return;
            uint32_t chars = GetU32(payload.data() + 8);
            if (12 + static_cast<size_t>(chars) * 2 > payload.size()) return;

            CameraState state;
            state.connected = true;
            state.muted = payload[0] != 0;
            state.powerOffWhenMuted = payload[1] != 0;
            state.overlayVersion = GetU32(payload.data() + 4);
            state.sourceLink.assign(reinterpret_cast<const wchar_t*>(payload.data() + 12), chars);
            {
                std::lock_guard lock(m_lock);
                m_state = std::move(state);
                m_stateReceived = true;
            }
            m_stateArrived.notify_all();
            break;
        }
        case protocol::MessageType::Overlay:
        {
            if (payload.size() < 4) return;
            auto overlay = std::make_shared<OverlayImage>();
            overlay->version = GetU32(payload.data());
            overlay->bytes.assign(payload.begin() + 4, payload.end());
            VCM_LOG(L"Overlay image %u received (%zu bytes)", overlay->version, overlay->bytes.size());
            std::lock_guard lock(m_lock);
            m_overlay = std::move(overlay);
            break;
        }
        default:
            break;
        }
    }
}
