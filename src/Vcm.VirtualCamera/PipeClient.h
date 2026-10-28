#pragma once

namespace vcm
{
    // Mirrors CameraPipeProtocol.cs in Vcm.Core.
    namespace protocol
    {
        inline constexpr wchar_t PipePath[] = L"\\\\.\\pipe\\VCM.VirtualCamera";
        inline constexpr uint32_t Version = 1;
        inline constexpr uint32_t HeaderSize = 8;
        inline constexpr uint32_t MaxPayloadSize = 32 * 1024 * 1024;

        enum class MessageType : uint32_t
        {
            Hello = 1,
            State = 2,
            Overlay = 3,
            InUse = 4,
        };
    }

    struct CameraState
    {
        bool connected = false;
        bool muted = false;
        bool powerOffWhenMuted = true;
        uint32_t overlayVersion = 0;
        std::wstring sourceLink;
    };

    struct OverlayImage
    {
        uint32_t version = 0;
        std::vector<uint8_t> bytes; // encoded image file; empty = black
    };

    // Process-wide connection to the VCM app. Started by the first media source and stopped by the last one.
    class PipeClient
    {
    public:
        static PipeClient& Instance();

        void AddRef();
        void Release();

        CameraState GetState();
        std::shared_ptr<const OverlayImage> GetOverlay();

        // Waits until the first state message arrives (used when a media source is created).
        bool WaitForState(DWORD timeoutMs);

        // Reference count of streaming streams; the app is told when the camera starts/stops being used.
        void StreamStarted();
        void StreamStopped();

        bool IsRunning();

    private:
        PipeClient() = default;

        void Run(HANDLE stop);
        bool RunSession(HANDLE pipe, HANDLE stop);
        bool ReadExact(HANDLE pipe, HANDLE stop, uint8_t* buffer, uint32_t size);
        void Send(const std::vector<uint8_t>& message);
        void SendLocked(const std::vector<uint8_t>& message);
        void SendCurrentInUse();
        void HandleMessage(protocol::MessageType type, const std::vector<uint8_t>& payload);

        std::mutex m_lock;
        std::condition_variable m_stateArrived;
        int m_refs = 0;
        int m_streaming = 0;
        CameraState m_state;
        bool m_stateReceived = false;
        std::shared_ptr<const OverlayImage> m_overlay = std::make_shared<OverlayImage>();
        std::thread m_thread;
        std::shared_ptr<wil::unique_event> m_stop; // one per thread, so a restart never reuses a waited-on handle
        bool m_stopping = false;                    // set while Release() joins the thread without the lock
        std::condition_variable m_stopped;

        std::mutex m_writeLock;
        HANDLE m_pipe = nullptr; // guarded by m_writeLock
    };
}
