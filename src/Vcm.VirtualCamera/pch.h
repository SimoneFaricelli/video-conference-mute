#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif

// Same include order as the Windows-Camera VirtualCamera sample: initguid.h before the KS headers so that
// PINNAME_VIDEO_CAPTURE, KSCAMERAPROFILE_Legacy etc. are defined (selectany) in this module.
#include <unknwn.h>
#include <windows.h>
#include <propvarutil.h>
#include <ole2.h>
#include <initguid.h>
#include <ks.h>
#include <ksproxy.h>
#include <ksmedia.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfobjects.h>
#include <mferror.h>
#include <mfreadwrite.h>
#include <mfvirtualcamera.h>
#include <wincodec.h>
#include <shlobj.h>
#include <knownfolders.h>

#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <wil/result.h>
#include <wil/com.h>
#include <wil/resource.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include "Log.h"
