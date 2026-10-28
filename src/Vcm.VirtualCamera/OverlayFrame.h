#pragma once

#include "PipeClient.h"

namespace vcm
{
    // NV12 picture shown while the camera is muted: the overlay image scaled to the stream size (aspect ratio not
    // preserved, as in the original), or black. Cached per image version and size.
    class OverlayFrame
    {
    public:
        // Returns a tightly packed NV12 buffer (pitch == width) for the given size.
        const std::vector<uint8_t>& Get(const std::shared_ptr<const OverlayImage>& image, UINT32 width, UINT32 height);

    private:
        static bool Decode(const std::vector<uint8_t>& encoded, UINT32 width, UINT32 height, std::vector<uint8_t>& bgr);
        static void BgrToNv12(const std::vector<uint8_t>& bgr, UINT32 width, UINT32 height, std::vector<uint8_t>& nv12);
        static void Black(UINT32 width, UINT32 height, std::vector<uint8_t>& nv12);

        std::vector<uint8_t> m_frame;
        uint32_t m_version = UINT32_MAX;
        UINT32 m_width = 0;
        UINT32 m_height = 0;
    };

    // Copies an NV12 picture between buffers with possibly different pitches.
    HRESULT CopyNv12ToSample(const uint8_t* source, LONG sourcePitch, UINT32 width, UINT32 height, IMFSample* destination);
    HRESULT CopyNv12Sample(IMFSample* source, UINT32 width, UINT32 height, IMFSample* destination);
}
