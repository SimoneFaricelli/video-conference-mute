# Manual test checklist (Windows 11)

The build machine is Linux: unit tests cover `Vcm.Core`, but everything touching Windows APIs must be checked on a
real PC. Keep `%TEMP%\VCM.log` and `%ProgramData%\VCM\camera.log` at hand when something fails.

## Install

- [ ] `VCM-Setup-1.0.0.exe` installs the .NET 10 Desktop Runtime when it is missing.
- [ ] `HKLM\SOFTWARE\Classes\CLSID\{D0AB47C7-9EBA-4D7A-AF72-4769A4D62109}\InprocServer32` points to
      `C:\Program Files\VCM\VcmVirtualCamera.dll`.
- [ ] The app starts after setup and opens the settings window. The tray icon appears.
- [ ] With PowerToys VCM settings present, they are imported on first run.

## Shortcuts

- [ ] Win+Shift+A toggles the microphone. Check it in the volume mixer or Sound settings.
- [ ] Win+Shift+O toggles the camera.
- [ ] Win+Shift+Q, camera idle: toggles the microphone only.
- [ ] Win+Shift+Q, camera in use, both on: mutes both. Both off: unmutes both. One muted: mutes the other.
- [ ] Push to talk (Win+Shift+I), microphone muted: unmuted while held, muted again on release.
- [ ] Push to talk with the microphone open: nothing happens (fixed behaviour).
- [ ] Push to reverse on, microphone open: muted while held.
- [ ] Shortcuts work while an elevated window (e.g. admin Terminal) has focus.
- [ ] Changing a shortcut:
  - the dialog captures Win combinations without opening Start;
  - an invalid combination shows the error;
  - Ctrl+Alt shows the Alt Gr warning;
  - a combination owned by another app shows the conflict warning;
  - Save, Reset and Cancel behave as expected.
- [ ] A shortcut already used elsewhere shows the warning on its card.

## Microphone

- [ ] "[All]" mutes every active microphone; the toolbar follows the default communications device.
- [ ] A specific microphone: only that one is muted; switching selection unmutes the previous one.
- [ ] Plugging a microphone in while muted: it gets muted too. Unplugging it: no errors.
- [ ] Unplugging and replugging the selected microphone: it is controlled again.
- [ ] Unmuting from Sound settings is reflected by toolbar and tray. With "Keep microphone muted" on, VCM mutes it again.

## Camera (virtual camera "VideoConference Mute")

- [ ] It appears in the Camera app, Teams, Zoom and the browser (e.g. webcamtests.com).
- [ ] It streams the camera chosen in "Selected camera"; switching camera while streaming switches the picture.
- [ ] Muted, overlay image set: the image is shown, stretched to the frame. Without an image: black.
- [ ] Changing the image while muted and streaming updates the picture.
- [ ] "Turn off the webcam when muted" on: the webcam light goes off while muted and comes back on unmute
      (overlay shown during the warm-up).
- [ ] The same option off: the light stays on and only the picture changes.
- [ ] Closing the app using the camera: VCM unmutes everything (except with "hide when both are muted").
- [ ] Exiting VCM removes the virtual camera.

## Toolbar

- [ ] All six positions, at 100% and 150% scaling, on the main monitor and with "All monitors".
- [ ] Hide modes:
  - "Never" always visible;
  - "unmuted" visible while something is muted;
  - "muted" visible while something is on;
  - "After timeout" only flashes after changes.
- [ ] Every change flashes the toolbar for half a second.
- [ ] Clicking the left half toggles the microphone, the right half the camera; clicks do not steal focus.
- [ ] Dark and light theme follow Windows. The toolbar is not visible in a Teams/Zoom screen share.
- [ ] "Camera not in use" is shown when no app streams from the virtual camera.

## Tray and behaviour

- [ ] Tray icon:
  - shows microphone off (red) and the camera-off badge;
  - a left click toggles the microphone;
  - the menu entries all work.
- [ ] Pause: shortcuts and toolbar off, devices unmuted, tray shows the pause badge.
- [ ] Startup action Mute / Unmute / Nothing applied at app start.
- [ ] Confirmation sound only when enabled and only for shortcuts and clicks.
- [ ] Run at startup: the app starts in the tray after sign-in, without opening the settings window.
- [ ] Starting VCM again opens the settings of the running instance.
- [ ] Uninstall unmutes, closes the app and removes the virtual camera registration.
