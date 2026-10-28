#pragma once

namespace vcm
{
    // Appends a line to %ProgramData%\VCM\camera.log (also sent to OutputDebugString).
    void Log(const char* function, const wchar_t* format, ...);

    // Routes WIL failures (RETURN_IF_FAILED & co.) to the log, so every failing HRESULT is recorded with its location.
    void InstallFailureLogging();
}

#define VCM_LOG(...) ::vcm::Log(__FUNCTION__, __VA_ARGS__)
