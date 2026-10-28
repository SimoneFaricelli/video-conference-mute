using Vcm.Core;

namespace Vcm.Core.Tests;

public class VcmEngineTests
{
    [Theory]
    // mic muted, camera muted -> expected mic, expected camera (camera in use)
    [InlineData(false, false, true, true)]   // both on -> both off
    [InlineData(true, true, false, false)]   // both off -> both on
    [InlineData(false, true, true, true)]    // camera already muted -> mute the microphone
    [InlineData(true, false, true, true)]    // microphone already muted -> mute the camera
    public void ToggleAll_WithCameraInUse(bool micMuted, bool camMuted, bool expectedMic, bool expectedCam)
    {
        var f = new EngineFixture();
        f.Camera.IsInUse = true;
        f.Mic.IsMuted = micMuted;
        f.Camera.IsMuted = camMuted;

        f.Engine.ToggleAll();

        Assert.Equal(expectedMic, f.Mic.IsMuted);
        Assert.Equal(expectedCam, f.Camera.IsMuted);
    }

    [Fact]
    public void ToggleAll_WithCameraIdle_TogglesOnlyMicrophone()
    {
        var f = new EngineFixture();
        f.Engine.ToggleAll();

        Assert.True(f.Mic.IsMuted);
        Assert.False(f.Camera.IsMuted);
    }

    [Fact]
    public void ToggleAll_IdleCameraMuteFlagIsIgnored()
    {
        // Camera not in use: its raw flag does not count as "muted" (CameraEffectivelyMuted).
        var f = new EngineFixture();
        f.Camera.IsMuted = true;
        f.Engine.ToggleAll();

        Assert.True(f.Mic.IsMuted);
        Assert.True(f.Camera.IsMuted);
    }

    [Fact]
    public void PushToTalk_WhenMuted_UnmutesWhileHeld()
    {
        var f = new EngineFixture();
        f.Mic.IsMuted = true;

        f.Engine.PushToTalkDown();
        Assert.False(f.Mic.IsMuted);
        f.Engine.PushToTalkDown(); // auto-repeat is ignored
        Assert.False(f.Mic.IsMuted);
        f.Engine.PushToTalkUp();
        Assert.True(f.Mic.IsMuted);
    }

    [Fact]
    public void PushToTalk_WhenUnmutedWithoutReverse_DoesNothing()
    {
        // The original muted the microphone on release in this case; that bug is fixed.
        var f = new EngineFixture();

        f.Engine.PushToTalkDown();
        Assert.False(f.Mic.IsMuted);
        f.Engine.PushToTalkUp();
        Assert.False(f.Mic.IsMuted);
        Assert.Equal(0, f.Mic.SetCalls);
    }

    [Fact]
    public void PushToReverse_WhenUnmuted_MutesWhileHeld()
    {
        var f = new EngineFixture();
        f.Settings.PushToReverseEnabled = true;

        f.Engine.PushToTalkDown();
        Assert.True(f.Mic.IsMuted);
        f.Engine.PushToTalkUp();
        Assert.False(f.Mic.IsMuted);
    }

    [Fact]
    public void PushToTalkUp_WithoutDown_IsIgnored()
    {
        var f = new EngineFixture();
        f.Engine.PushToTalkUp();
        Assert.Equal(0, f.Mic.SetCalls);
    }

    [Theory]
    [InlineData(StartupActions.Mute, true)]
    [InlineData(StartupActions.Unmute, false)]
    public void StartupAction_AppliesToBothDevices(string action, bool expected)
    {
        var f = new EngineFixture();
        f.Settings.StartupAction = action;
        f.Mic.IsMuted = !expected;
        f.Camera.IsMuted = !expected;

        f.Engine.ApplyMicrophoneStartupAction();
        f.Engine.ApplyCameraStartupAction();

        Assert.Equal(expected, f.Mic.IsMuted);
        Assert.Equal(expected, f.Camera.IsMuted);
        Assert.All(f.Changes, c => Assert.False(c.UserInitiated));
    }

    [Fact]
    public void StartupAction_Nothing_LeavesStateAlone()
    {
        var f = new EngineFixture();
        f.Mic.IsMuted = true;
        f.Engine.ApplyMicrophoneStartupAction();
        f.Engine.ApplyCameraStartupAction();

        Assert.True(f.Mic.IsMuted);
        Assert.Equal(0, f.Mic.SetCalls);
        Assert.Empty(f.Changes);
    }

    [Theory]
    [InlineData(ToolbarHideModes.Never, true)]
    [InlineData(ToolbarHideModes.WhenBothUnmuted, true)]
    [InlineData(ToolbarHideModes.AfterTimeout, true)]
    [InlineData(ToolbarHideModes.WhenBothMuted, false)]
    public void CameraStoppingUse_UnmutesAllExceptInMutedHideMode(string hideMode, bool expectUnmute)
    {
        var f = new EngineFixture();
        f.Settings.ToolbarHide = hideMode;
        f.Mic.IsMuted = true;
        f.Camera.IsMuted = true;

        f.Engine.OnCameraInUseChanged(wasInUse: true, isInUse: false);

        Assert.Equal(!expectUnmute, f.Mic.IsMuted);
        Assert.Equal(!expectUnmute, f.Camera.IsMuted);
    }

    [Fact]
    public void CameraStartingUse_DoesNotUnmute()
    {
        var f = new EngineFixture();
        f.Mic.IsMuted = true;
        f.Engine.OnCameraInUseChanged(wasInUse: false, isInUse: true);
        Assert.True(f.Mic.IsMuted);
    }

    [Fact]
    public void ExternalUnmute_IsAccepted_WhenNotEnforced()
    {
        var f = new EngineFixture();
        f.Engine.SetMicrophoneMuted(true, userInitiated: true);
        f.Changes.Clear();

        f.Mic.IsMuted = false; // another application unmuted the device
        f.Engine.OnMicrophoneMuteReported(false);

        Assert.False(f.Mic.IsMuted);
        Assert.Equal(new VcmStateChange(VcmDevice.Microphone, false, false), Assert.Single(f.Changes));
    }

    [Fact]
    public void ExternalUnmute_IsReverted_WhenEnforced()
    {
        var f = new EngineFixture();
        f.Settings.EnforceMicrophoneMute = true;
        f.Engine.SetMicrophoneMuted(true, userInitiated: true);

        f.Mic.IsMuted = false;
        f.Engine.OnMicrophoneMuteReported(false);

        Assert.True(f.Mic.IsMuted);
    }

    [Fact]
    public void EnforcedMute_DoesNotBlockOwnUnmute()
    {
        var f = new EngineFixture();
        f.Settings.EnforceMicrophoneMute = true;
        f.Engine.SetMicrophoneMuted(true, userInitiated: true);
        f.Engine.ToggleMicrophone();
        f.Engine.OnMicrophoneMuteReported(false); // echo of our own change

        Assert.False(f.Mic.IsMuted);
    }

    [Fact]
    public void OwnChangeEcho_DoesNotRaiseSecondEvent()
    {
        var f = new EngineFixture();
        f.Engine.ToggleMicrophone();
        f.Engine.OnMicrophoneMuteReported(true);
        Assert.Single(f.Changes);
        Assert.True(f.Changes[0].UserInitiated);
    }

    [Fact]
    public void MuteAll_And_UnmuteAll()
    {
        var f = new EngineFixture();
        f.Engine.MuteAll();
        Assert.True(f.Mic.IsMuted);
        Assert.True(f.Camera.IsMuted);
        f.Engine.UnmuteAll();
        Assert.False(f.Mic.IsMuted);
        Assert.False(f.Camera.IsMuted);
    }
}
