#pragma once

#include <guiddef.h>

namespace vcm
{
    // CLSID of the media source activator. Must match VirtualCameraNative.MediaSourceClsid in the app.
    // {D0AB47C7-9EBA-4D7A-AF72-4769A4D62109}
    inline constexpr wchar_t MediaSourceClsidString[] = L"{D0AB47C7-9EBA-4D7A-AF72-4769A4D62109}";
    inline constexpr GUID MediaSourceClsid = { 0xd0ab47c7, 0x9eba, 0x4d7a, { 0xaf, 0x72, 0x47, 0x69, 0xa4, 0xd6, 0x21, 0x09 } };

    // Activation attribute set by VcmCam_Start: symbolic link of the physical camera chosen when the virtual
    // camera was created. The pipe state, when available, takes precedence.
    // {9636FFF7-3A24-4ECF-AB27-FDD54968EBB3}
    inline constexpr GUID AttributeSourceLink = { 0x9636fff7, 0x3a24, 0x4ecf, { 0xab, 0x27, 0xfd, 0xd5, 0x49, 0x68, 0xeb, 0xb3 } };

    inline constexpr wchar_t MediaSourceFriendlyName[] = L"VCM Virtual Camera Media Source";
}
