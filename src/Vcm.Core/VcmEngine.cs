namespace Vcm.Core;

public interface IMicrophoneController
{
    /// <summary>Mute state of the tracked microphone (or the last requested state when none is connected).</summary>
    bool IsMuted { get; }

    /// <summary>Applies the state to every controlled microphone.</summary>
    void SetMuted(bool muted);
}

public interface ICameraController
{
    /// <summary>True while an application is streaming from the virtual camera.</summary>
    bool IsInUse { get; }

    /// <summary>True when the virtual camera shows the overlay image instead of the real camera.</summary>
    bool IsMuted { get; }

    void SetMuted(bool muted);
}

public enum VcmDevice
{
    Microphone,
    Camera,
}

/// <param name="Device">Which device changed.</param>
/// <param name="Muted">The new mute state.</param>
/// <param name="UserInitiated">True for shortcuts and clicks; false for startup actions, automatic changes and
/// changes made by other applications.</param>
public readonly record struct VcmStateChange(VcmDevice Device, bool Muted, bool UserInitiated);

/// <summary>
/// The mute logic of the PowerToys VideoConferenceModule, independent of Windows APIs.
/// </summary>
public sealed class VcmEngine
{
    private readonly IMicrophoneController _mic;
    private readonly ICameraController _camera;
    private readonly Func<VcmSettings> _settings;

    private bool _pushToTalkPressed;
    private bool _pushToTalkToggled;
    private bool? _expectedMicMuted;

    public VcmEngine(IMicrophoneController mic, ICameraController camera, Func<VcmSettings> settings)
    {
        _mic = mic;
        _camera = camera;
        _settings = settings;
    }

    public event Action<VcmStateChange>? StateChanged;

    public bool MicrophoneMuted => _mic.IsMuted;

    public bool CameraInUse => _camera.IsInUse;

    /// <summary>Raw camera mute flag; it is kept even while no application uses the camera.</summary>
    public bool CameraMuted => _camera.IsMuted;

    /// <summary>Camera counts as muted only while it is in use, as in the original module.</summary>
    public bool CameraEffectivelyMuted => _camera.IsInUse && _camera.IsMuted;

    public bool PushToTalkPressed => _pushToTalkPressed;

    public void ToggleMicrophone() => SetMicrophoneMuted(!_mic.IsMuted, userInitiated: true);

    public void ToggleCamera() => SetCameraMuted(!_camera.IsMuted, userInitiated: true);

    /// <summary>
    /// "Mute camera &amp; microphone". With the camera idle only the microphone is toggled. When the two states
    /// differ, the one that is still active gets muted.
    /// </summary>
    public void ToggleAll()
    {
        if (!_camera.IsInUse)
        {
            ToggleMicrophone();
            return;
        }

        bool micMuted = _mic.IsMuted;
        bool cameraMuted = CameraEffectivelyMuted;
        if (micMuted == cameraMuted)
        {
            ToggleMicrophone();
            ToggleCamera();
        }
        else if (cameraMuted)
        {
            ToggleMicrophone();
        }
        else
        {
            ToggleCamera();
        }
    }

    /// <summary>
    /// Push-to-talk key pressed. A muted microphone is unmuted while the key is held; with "push to reverse" an
    /// unmuted microphone is muted while the key is held.
    /// </summary>
    public void PushToTalkDown()
    {
        if (_pushToTalkPressed)
        {
            return;
        }

        _pushToTalkPressed = true;
        _pushToTalkToggled = _settings().PushToReverseEnabled || _mic.IsMuted;
        if (_pushToTalkToggled)
        {
            ToggleMicrophone();
        }
    }

    /// <summary>Push-to-talk key released: restores the state the microphone had before the key was pressed.</summary>
    public void PushToTalkUp()
    {
        if (!_pushToTalkPressed)
        {
            return;
        }

        _pushToTalkPressed = false;
        if (_pushToTalkToggled)
        {
            _pushToTalkToggled = false;
            ToggleMicrophone();
        }
    }

    public void MuteAll(bool userInitiated = true)
    {
        if (!_mic.IsMuted) SetMicrophoneMuted(true, userInitiated);
        if (!_camera.IsMuted) SetCameraMuted(true, userInitiated);
    }

    public void UnmuteAll(bool userInitiated = true)
    {
        if (_mic.IsMuted) SetMicrophoneMuted(false, userInitiated);
        if (_camera.IsMuted) SetCameraMuted(false, userInitiated);
    }

    /// <summary>Applies <c>startup_action</c> to the microphones (at start and whenever the selection changes).</summary>
    public void ApplyMicrophoneStartupAction()
    {
        switch (_settings().StartupAction)
        {
            case StartupActions.Mute: SetMicrophoneMuted(true, userInitiated: false); break;
            case StartupActions.Unmute: SetMicrophoneMuted(false, userInitiated: false); break;
        }
    }

    /// <summary>Applies <c>startup_action</c> to the camera (at start and whenever the selected camera changes).</summary>
    public void ApplyCameraStartupAction()
    {
        switch (_settings().StartupAction)
        {
            case StartupActions.Mute: SetCameraMuted(true, userInitiated: false); break;
            case StartupActions.Unmute: SetCameraMuted(false, userInitiated: false); break;
        }
    }

    /// <summary>
    /// When the last application stops using the camera, the call is assumed to be over and everything is unmuted,
    /// except with "hide when both are muted" (same rule as the original toolbar loop).
    /// </summary>
    public void OnCameraInUseChanged(bool wasInUse, bool isInUse)
    {
        if (wasInUse && !isInUse && _settings().ToolbarHide != ToolbarHideModes.WhenBothMuted)
        {
            UnmuteAll(userInitiated: false);
        }
        else
        {
            StateChanged?.Invoke(new VcmStateChange(VcmDevice.Camera, _camera.IsMuted, UserInitiated: false));
        }
    }

    /// <summary>
    /// Called when the tracked microphone reports a mute change, which may come from VCM itself or from another
    /// application. With "keep mute" enabled an external unmute is reverted.
    /// </summary>
    public void OnMicrophoneMuteReported(bool muted)
    {
        if (_expectedMicMuted == muted)
        {
            return;
        }

        if (_expectedMicMuted == true && !muted && _settings().EnforceMicrophoneMute)
        {
            _mic.SetMuted(true);
            StateChanged?.Invoke(new VcmStateChange(VcmDevice.Microphone, true, UserInitiated: false));
            return;
        }

        _expectedMicMuted = muted;
        StateChanged?.Invoke(new VcmStateChange(VcmDevice.Microphone, muted, UserInitiated: false));
    }

    /// <summary>Re-reads the microphone state after the controlled devices changed.</summary>
    public void SyncMicrophoneState()
    {
        _expectedMicMuted = _mic.IsMuted;
    }

    public void SetMicrophoneMuted(bool muted, bool userInitiated)
    {
        _expectedMicMuted = muted;
        _mic.SetMuted(muted);
        StateChanged?.Invoke(new VcmStateChange(VcmDevice.Microphone, muted, userInitiated));
    }

    public void SetCameraMuted(bool muted, bool userInitiated)
    {
        _camera.SetMuted(muted);
        StateChanged?.Invoke(new VcmStateChange(VcmDevice.Camera, muted, userInitiated));
    }
}
