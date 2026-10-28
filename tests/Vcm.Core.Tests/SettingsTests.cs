using Vcm.Core;

namespace Vcm.Core.Tests;

public class SettingsTests
{
    // A settings.json written by PowerToys v0.87.1.
    private const string PowerToysJson = """
        {"properties":{
          "mute_camera_and_microphone_hotkey":{"value":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":81,"key":"Q"}},
          "mute_microphone_hotkey":{"value":{"win":false,"ctrl":true,"alt":true,"shift":false,"code":77,"key":"M"}},
          "push_to_talk_microphone_hotkey":{"value":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":73,"key":"I"}},
          "mute_camera_hotkey":{"value":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":79,"key":"O"}},
          "push_to_reverse_enabled":{"value":true},
          "selected_camera":{"value":"Integrated Camera"},
          "selected_mic":{"value":"Microphone Array (Realtek)"},
          "toolbar_position":{"value":"Bottom center"},
          "toolbar_monitor":{"value":"All monitors"},
          "camera_overlay_image_path":{"value":"C:\\Users\\me\\Pictures\\away.png"},
          "toolbar_hide":{"value":"After timeout"},
          "startup_action":{"value":"Mute"},
          "theme":{"value":null}
        },"name":"Video Conference","version":"1"}
        """;

    [Fact]
    public void Defaults_MatchPowerToys()
    {
        var s = new VcmSettings();
        Assert.Equal("Win + Shift + Q", s.MuteCameraAndMicrophoneHotkey.ToString());
        Assert.Equal("Win + Shift + A", s.MuteMicrophoneHotkey.ToString());
        Assert.Equal("Win + Shift + I", s.PushToTalkMicrophoneHotkey.ToString());
        Assert.Equal("Win + Shift + O", s.MuteCameraHotkey.ToString());
        Assert.Equal(81, s.MuteCameraAndMicrophoneHotkey.Code);
        Assert.False(s.PushToReverseEnabled);
        Assert.Equal(VcmSettings.AllMicrophones, s.SelectedMicrophone);
        Assert.Equal(ToolbarPositions.TopRight, s.ToolbarPosition);
        Assert.Equal(ToolbarMonitors.Main, s.ToolbarMonitor);
        Assert.Equal(ToolbarHideModes.WhenBothUnmuted, s.ToolbarHide);
        Assert.Equal(StartupActions.Nothing, s.StartupAction);
        Assert.True(s.CameraPowerOffWhenMuted);
        Assert.False(s.PlaySoundOnToggle);
        Assert.False(s.EnforceMicrophoneMute);
    }

    [Fact]
    public void Reads_PowerToysFile()
    {
        var s = SettingsSerializer.Deserialize(PowerToysJson);
        Assert.Equal(new HotkeySettings(false, true, true, false, 'M'), s.MuteMicrophoneHotkey);
        Assert.True(s.PushToReverseEnabled);
        Assert.Equal("Integrated Camera", s.SelectedCamera);
        Assert.Equal("Microphone Array (Realtek)", s.SelectedMicrophone);
        Assert.Equal(ToolbarPositions.BottomCenter, s.ToolbarPosition);
        Assert.Equal(ToolbarMonitors.All, s.ToolbarMonitor);
        Assert.Equal(@"C:\Users\me\Pictures\away.png", s.CameraOverlayImagePath);
        Assert.Equal(ToolbarHideModes.AfterTimeout, s.ToolbarHide);
        Assert.Equal(StartupActions.Mute, s.StartupAction);
        // Keys that PowerToys does not have keep their defaults.
        Assert.True(s.Enabled);
        Assert.True(s.CameraPowerOffWhenMuted);
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var s = SettingsSerializer.Deserialize(PowerToysJson);
        s.PlaySoundOnToggle = true;
        s.EnforceMicrophoneMute = true;
        s.CameraPowerOffWhenMuted = false;
        s.TrayIconShowsState = false;
        s.Enabled = false;

        var again = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(s));

        Assert.Equivalent(s, again, strict: true);
    }

    [Fact]
    public void Serialize_UsesPowerToysShape()
    {
        string json = SettingsSerializer.Serialize(new VcmSettings());
        Assert.Contains("\"name\": \"Video Conference\"", json);
        Assert.Contains("\"mute_camera_hotkey\": {", json);
        Assert.Contains("\"code\": 79", json);
        Assert.Contains("\"toolbar_hide\": {\n      \"value\": \"When both camera and microphone are unmuted\"", json.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"properties\":5}")]
    public void InvalidInput_GivesDefaults(string json)
    {
        Assert.Equivalent(new VcmSettings(), SettingsSerializer.Deserialize(json), strict: true);
    }

    [Fact]
    public void UnknownValues_AreNormalized()
    {
        const string json = """
            {"properties":{"toolbar_position":{"value":"Middle"},"toolbar_hide":{"value":"Sometimes"},
            "startup_action":{"value":"Explode"},"toolbar_monitor":{"value":"Monitor under cursor"},"selected_mic":{"value":""}}}
            """;
        var s = SettingsSerializer.Deserialize(json);
        Assert.Equal(ToolbarPositions.TopRight, s.ToolbarPosition);
        Assert.Equal(ToolbarHideModes.WhenBothUnmuted, s.ToolbarHide);
        Assert.Equal(StartupActions.Nothing, s.StartupAction);
        Assert.Equal(ToolbarMonitors.Main, s.ToolbarMonitor);
        Assert.Equal(VcmSettings.AllMicrophones, s.SelectedMicrophone);
    }

    [Theory]
    [InlineData(true, false, false, false, 'Q', true)]
    [InlineData(false, true, false, false, 'M', true)]
    [InlineData(false, false, false, true, 'M', true)]
    [InlineData(false, false, false, false, 'M', false)]  // no modifier
    [InlineData(true, false, false, true, 0, false)]      // no key
    [InlineData(true, false, false, false, 0x10, false)]  // modifier as main key
    [InlineData(false, false, false, true, 0x09, false)]  // Shift+Tab is reserved for navigation
    [InlineData(false, true, false, false, 0x09, true)]   // Ctrl+Tab is fine
    public void Hotkey_Validation(bool win, bool ctrl, bool alt, bool shift, int code, bool valid)
    {
        Assert.Equal(valid, new HotkeySettings(win, ctrl, alt, shift, code).IsValid);
    }

    [Fact]
    public void Hotkey_KeyListOrder()
    {
        var h = new HotkeySettings(true, true, true, true, 0x70);
        Assert.Equal(["Win", "Ctrl", "Alt", "Shift", "F1"], h.GetKeyList());
    }

    [Fact]
    public void Hotkey_SameCombinationIgnoresKeyName()
    {
        var a = new HotkeySettings(true, false, false, true, 'Q');
        var b = a with { Key = "q" };
        Assert.True(a.SameCombination(b));
        Assert.False(a.SameCombination(a with { Alt = true }));
    }
}
