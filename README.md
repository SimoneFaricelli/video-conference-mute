# VCM — VideoConference Mute

A standalone Windows replacement for **Video Conference Mute**, the PowerToys utility removed after v0.87.1.
One shortcut mutes microphone and camera system-wide, whatever app has focus.

## Features

From the original module, with the same settings and the same `settings.json` format:

- Shortcuts:

  | Action | Default |
  |---|---|
  | Mute camera & microphone | Win+Shift+Q |
  | Mute microphone | Win+Shift+A |
  | Push to talk | Win+Shift+I |
  | Mute camera | Win+Shift+O |

- "Mute camera & microphone" acts on the microphone only while the camera is idle. If only one of the two is muted,
  it mutes the other.
- Microphone: "[All]" or a specific device, plus *push to reverse*.
- Camera: the **"VideoConference Mute"** virtual camera wraps the selected physical camera and shows an image of your
  choice (or black) while muted.
- Toolbar:
  - six positions;
  - main monitor or all monitors;
  - hide never, when both are unmuted, when both are muted, or after a timeout;
  - a click on either half toggles that device.
- Startup action: nothing / unmute / mute.
- Disabling VCM or closing it unmutes everything. Everything is also unmuted when the last app stops using the camera.
- On first run, existing PowerToys Video Conference settings are imported.

Improvements over the original:

- **Media Foundation virtual camera** instead of a DirectShow filter. It also works in modern apps: Camera app, new
  Teams, browsers.
- **The webcam can really turn off while muted** (light off). This is an option; the original behaviour keeps the
  camera running.
- **Camera and overlay changes apply while streaming**: switching the physical camera or the overlay image no
  longer requires restarting the app that uses the camera.
- Shortcuts through `RegisterHotKey`:
  - they work over elevated windows;
  - they cannot be silently dropped by Windows;
  - a combination already used by Windows or another app is taken over through a low-level keyboard hook, as the
    original did. That hook only exists while such a combination is configured, and those shortcuts do not work
    while an elevated window has focus.
- Vector toolbar: sharp at any DPI, localized, excluded from screen sharing.
- Tray icon:
  - shows the microphone and camera state;
  - a click toggles the microphone;
  - the menu offers the toggles, pause, run at startup and exit.
- Optional confirmation sound.
- Optional "keep microphone muted": if another app unmutes the microphone, VCM mutes it again.
- Italian and English UI, chosen from the Windows language.
- Fixed bugs of the original:
  - Alt-based shortcuts did not work;
  - push-to-talk muted an already-open microphone on release;
  - disabling VCM left microphones muted;
  - removed or reconnected microphones were not handled.

## Requirements

- Windows 10 2004 or later for the microphone features. **The virtual camera requires Windows 11** (22000+).
- .NET 10 Desktop Runtime (the installer downloads it if missing).

## Install

Run `out/VCM-Setup-1.0.0.exe`.

- Administrator rights are needed once, to register the virtual camera media source.
- The app itself runs as a normal user from the tray.
- Then select **VideoConference Mute** as camera in your video-call apps.

The files are not code-signed:
- SmartScreen shows a warning ("More info" → "Run anyway");
- with **Smart App Control** enabled, Windows blocks unsigned apps.

## Build (Linux + Docker)

```bash
./build/build.sh
```

| Component | Built with |
|---|---|
| `src/Vcm.VirtualCamera` (C++ media source DLL) | MSVC + Windows SDK under wine, image from `build/Dockerfile.msvc` ([msvc-wine](https://github.com/mstorsjo/msvc-wine)) |
| `src/Vcm.Core` + tests, `src/Vcm.App` (WPF, WPF-UI) | `mcr.microsoft.com/dotnet/sdk:10.0` |
| `installer/vcm.iss` | Inno Setup under wine (`amake/innosetup`) |

Building the MSVC image (first run only, ~15–30 min, ~4 GB) downloads Visual Studio Build Tools and the Windows SDK
and accepts the [Microsoft license](https://go.microsoft.com/fwlink/?LinkId=2086102); that image must not be
redistributed.

On Windows you can also build the C# projects with `dotnet build` and the DLL with CMake + Visual Studio 2022.

## Layout

| Path | Content |
|---|---|
| `src/Vcm.Core` | Platform-independent logic: settings (PowerToys format), mute engine, toolbar rules, pipe protocol |
| `src/Vcm.App` | WPF app: tray, shortcuts, Core Audio microphones, toolbar, settings window, virtual camera control |
| `src/Vcm.VirtualCamera` | Media source loaded by the Windows Frame Server (named pipe client to the app) |
| `tests/Vcm.Core.Tests` | xUnit tests (run on Linux) |
| `docs/TESTING.md` | Manual test checklist for Windows |

## Logs

| Component | Log file |
|---|---|
| App | `%TEMP%\VCM.log` |
| Virtual camera | `%ProgramData%\VCM\camera.log` |

Settings are stored in `%LOCALAPPDATA%\VCM\settings.json`.

## License

MIT. See `THIRD-PARTY-NOTICES.md` for PowerToys, the Windows-Camera sample, WPF-UI and WIL.
