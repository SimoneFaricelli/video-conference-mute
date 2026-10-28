#include "pch.h"
#include "Activate.h"
#include "CameraUtil.h"
#include "Guids.h"
#include "PipeClient.h"

using Microsoft::WRL::ComPtr;

namespace
{
    HMODULE g_module = nullptr;

    // Factory creating the activator through MakeAndInitialize (WRL's default factory would skip initialization).
    class ActivateFactory : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IClassFactory>
    {
    public:
        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** object) override
        {
            RETURN_HR_IF_NULL(E_POINTER, object);
            *object = nullptr;
            RETURN_HR_IF(CLASS_E_NOAGGREGATION, outer != nullptr);
            ComPtr<vcm::Activate> activate;
            RETURN_IF_FAILED(Microsoft::WRL::MakeAndInitialize<vcm::Activate>(&activate));
            return activate->QueryInterface(riid, object);
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            auto& module = Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule();
            if (lock) module.IncrementObjectCount();
            else module.DecrementObjectCount();
            return S_OK;
        }
    };

    // MFCreateVirtualCamera exists only on Windows 11; resolve it at run time so the DLL still loads on Windows 10.
    template <typename T>
    T SensorGroupFunction(const char* name)
    {
        static HMODULE library = LoadLibraryExW(L"mfsensorgroup.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        return library ? reinterpret_cast<T>(GetProcAddress(library, name)) : nullptr;
    }

    using MFCreateVirtualCameraFn = HRESULT(WINAPI*)(MFVirtualCameraType, MFVirtualCameraLifetime, MFVirtualCameraAccess, LPCWSTR,
        LPCWSTR, const GUID*, ULONG, IMFVirtualCamera**);
    using MFIsVirtualCameraTypeSupportedFn = HRESULT(WINAPI*)(MFVirtualCameraType, BOOL*);

    std::mutex g_cameraLock;
    ComPtr<IMFVirtualCamera> g_camera;

    // Keeps a multithreaded apartment alive for the virtual camera object, whatever thread calls the exports.
    HRESULT EnsureRuntime()
    {
        static HRESULT result = []
        {
            vcm::InstallFailureLogging();
            CO_MTA_USAGE_COOKIE cookie{};
            HRESULT hr = CoIncrementMTAUsage(&cookie);
            if (SUCCEEDED(hr)) hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
            return hr;
        }();
        return result;
    }

    std::wstring ModulePath()
    {
        wchar_t path[MAX_PATH * 4];
        DWORD length = GetModuleFileNameW(g_module, path, ARRAYSIZE(path));
        return std::wstring(path, length);
    }

    std::wstring ClsidKey()
    {
        return std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + vcm::MediaSourceClsidString;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** object)
{
    RETURN_HR_IF_NULL(E_POINTER, object);
    *object = nullptr;
    Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule();
    if (clsid != vcm::MediaSourceClsid)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    auto factory = Microsoft::WRL::Make<ActivateFactory>();
    RETURN_IF_NULL_ALLOC(factory);
    return factory->QueryInterface(riid, object);
}

STDAPI DllCanUnloadNow()
{
    bool idle = Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule().GetObjectCount() == 0 && !vcm::PipeClient::Instance().IsRunning();
    std::lock_guard lock(g_cameraLock);
    return idle && !g_camera ? S_OK : S_FALSE;
}

// Registers the media source under HKLM (requires administrator rights; done by the installer through regsvr32).
STDAPI DllRegisterServer()
{
    std::wstring key = ClsidKey();
    std::wstring inproc = key + L"\\InprocServer32";
    std::wstring path = ModulePath();
    auto size = [](const std::wstring& s) { return static_cast<DWORD>((s.size() + 1) * sizeof(wchar_t)); };

    RETURN_IF_WIN32_ERROR(RegSetKeyValueW(HKEY_LOCAL_MACHINE, key.c_str(), nullptr, REG_SZ, vcm::MediaSourceFriendlyName,
        static_cast<DWORD>(sizeof(vcm::MediaSourceFriendlyName))));
    RETURN_IF_WIN32_ERROR(RegSetKeyValueW(HKEY_LOCAL_MACHINE, inproc.c_str(), nullptr, REG_SZ, path.c_str(), size(path)));
    static const std::wstring both = L"Both";
    RETURN_IF_WIN32_ERROR(RegSetKeyValueW(HKEY_LOCAL_MACHINE, inproc.c_str(), L"ThreadingModel", REG_SZ, both.c_str(), size(both)));
    return S_OK;
}

STDAPI DllUnregisterServer()
{
    LSTATUS status = RegDeleteTreeW(HKEY_LOCAL_MACHINE, ClsidKey().c_str());
    return status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND ? S_OK : HRESULT_FROM_WIN32(status);
}

// ---- Functions used by the VCM app (VirtualCameraNative.cs) ----

using CameraCallback = void (*)(const wchar_t* name, const wchar_t* symbolicLink, void* context);

extern "C" int VcmCam_IsSupported()
{
    if (FAILED(EnsureRuntime()))
    {
        return 0;
    }
    auto isSupported = SensorGroupFunction<MFIsVirtualCameraTypeSupportedFn>("MFIsVirtualCameraTypeSupported");
    BOOL supported = FALSE;
    return isSupported && SUCCEEDED(isSupported(MFVirtualCameraType_SoftwareCameraSource, &supported)) && supported ? 1 : 0;
}

extern "C" HRESULT VcmCam_Start(const wchar_t* friendlyName, const wchar_t* sourceLink)
{
    RETURN_IF_FAILED(EnsureRuntime());
    auto create = SensorGroupFunction<MFCreateVirtualCameraFn>("MFCreateVirtualCamera");
    RETURN_HR_IF_NULL(E_NOTIMPL, create);

    std::lock_guard lock(g_cameraLock);
    if (g_camera)
    {
        g_camera->Remove();
        g_camera->Shutdown();
        g_camera.Reset();
    }

    // Session lifetime + current user: no administrator rights needed; the camera disappears when the app exits.
    ComPtr<IMFVirtualCamera> camera;
    RETURN_IF_FAILED(create(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_Session, MFVirtualCameraAccess_CurrentUser,
        friendlyName, vcm::MediaSourceClsidString, nullptr, 0, &camera));
    if (sourceLink && *sourceLink)
    {
        LOG_IF_FAILED(camera->SetString(vcm::AttributeSourceLink, sourceLink));
    }
    RETURN_IF_FAILED(camera->Start(nullptr));
    g_camera = camera;
    VCM_LOG(L"Virtual camera \"%s\" created", friendlyName);
    return S_OK;
}

extern "C" HRESULT VcmCam_Stop()
{
    std::lock_guard lock(g_cameraLock);
    if (!g_camera)
    {
        return S_FALSE;
    }
    HRESULT hr = g_camera->Remove();
    g_camera->Shutdown();
    g_camera.Reset();
    VCM_LOG(L"Virtual camera removed: 0x%08X", hr);
    return hr;
}

extern "C" HRESULT VcmCam_EnumerateCameras(CameraCallback callback, void* context)
{
    RETURN_HR_IF_NULL(E_POINTER, callback);
    RETURN_IF_FAILED(EnsureRuntime());
    for (const auto& camera : vcm::EnumerateCameras())
    {
        callback(camera.name.c_str(), camera.symbolicLink.c_str(), context);
    }
    return S_OK;
}
