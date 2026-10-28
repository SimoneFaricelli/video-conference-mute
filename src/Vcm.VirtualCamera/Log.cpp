#include "pch.h"

#include <cstdarg>
#include <cwchar>

namespace
{
    constexpr LONGLONG MaxLogSize = 5 * 1024 * 1024;

    std::mutex g_logLock;

    std::wstring LogPath()
    {
        wil::unique_cotaskmem_string programData;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_ProgramData, 0, nullptr, &programData)))
        {
            return {};
        }
        std::wstring folder = std::wstring(programData.get()) + L"\\VCM";
        CreateDirectoryW(folder.c_str(), nullptr);
        return folder + L"\\camera.log";
    }

    void WriteLine(const std::wstring& line)
    {
        OutputDebugStringW(line.c_str());

        static const std::wstring path = LogPath();
        if (path.empty())
        {
            return;
        }

        std::lock_guard lock(g_logLock);
        wil::unique_hfile file(CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
        if (!file)
        {
            return;
        }

        LARGE_INTEGER size{};
        if (GetFileSizeEx(file.get(), &size) && size.QuadPart > MaxLogSize)
        {
            file.reset(CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
            if (!file) return;
        }

        int bytes = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), static_cast<int>(line.size()), nullptr, 0, nullptr, nullptr);
        std::string utf8(static_cast<size_t>(bytes), '\0');
        WideCharToMultiByte(CP_UTF8, 0, line.c_str(), static_cast<int>(line.size()), utf8.data(), bytes, nullptr, nullptr);
        DWORD written = 0;
        WriteFile(file.get(), utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    }

    std::wstring Prefix(const char* function)
    {
        SYSTEMTIME t{};
        GetLocalTime(&t);
        wchar_t buffer[160];
        swprintf_s(buffer, L"%04u-%02u-%02u %02u:%02u:%02u.%03u [%lu:%lu] %hs: ", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute,
            t.wSecond, t.wMilliseconds, GetCurrentProcessId(), GetCurrentThreadId(), function);
        return buffer;
    }
}

namespace vcm
{
    void Log(const char* function, const wchar_t* format, ...)
    {
        wchar_t message[1024];
        va_list args;
        va_start(args, format);
        _vsnwprintf_s(message, _TRUNCATE, format, args);
        va_end(args);
        WriteLine(Prefix(function) + message + L"\r\n");
    }

    void InstallFailureLogging()
    {
        static std::once_flag once;
        std::call_once(once, []
        {
            wil::SetResultLoggingCallback([](const wil::FailureInfo& failure) noexcept
            {
                wchar_t message[2048];
                if (SUCCEEDED(wil::GetFailureLogString(message, ARRAYSIZE(message), failure)))
                {
                    WriteLine(Prefix("wil") + message);
                }
            });
        });
    }
}
