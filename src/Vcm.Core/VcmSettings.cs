namespace Vcm.Core;

/// <summary>Values of <c>toolbar_position</c>, identical to the PowerToys strings.</summary>
public static class ToolbarPositions
{
    public const string TopLeft = "Top left corner";
    public const string TopCenter = "Top center";
    public const string TopRight = "Top right corner";
    public const string BottomLeft = "Bottom left corner";
    public const string BottomCenter = "Bottom center";
    public const string BottomRight = "Bottom right corner";

    public static readonly IReadOnlyList<string> All = [TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight];
}

/// <summary>Values of <c>toolbar_monitor</c>.</summary>
public static class ToolbarMonitors
{
    public const string Main = "Main monitor";
    public const string All = "All monitors";

    public static readonly IReadOnlyList<string> Values = [Main, All];
}

/// <summary>Values of <c>toolbar_hide</c>.</summary>
public static class ToolbarHideModes
{
    public const string Never = "Never";
    public const string WhenBothUnmuted = "When both camera and microphone are unmuted";
    public const string WhenBothMuted = "When both camera and microphone are muted";
    public const string AfterTimeout = "After timeout";

    public static readonly IReadOnlyList<string> All = [Never, WhenBothUnmuted, WhenBothMuted, AfterTimeout];
}

/// <summary>Values of <c>startup_action</c>.</summary>
public static class StartupActions
{
    public const string Nothing = "Nothing";
    public const string Unmute = "Unmute";
    public const string Mute = "Mute";

    public static readonly IReadOnlyList<string> All = [Nothing, Unmute, Mute];
}

public sealed class VcmSettings
{
    public const string AllMicrophones = "[All]";

    public static HotkeySettings DefaultMuteCameraAndMicrophoneHotkey => new(true, false, false, true, 'Q');
    public static HotkeySettings DefaultMuteMicrophoneHotkey => new(true, false, false, true, 'A');
    public static HotkeySettings DefaultPushToTalkMicrophoneHotkey => new(true, false, false, true, 'I');
    public static HotkeySettings DefaultMuteCameraHotkey => new(true, false, false, true, 'O');

    // Settings of the original PowerToys module.
    public HotkeySettings MuteCameraAndMicrophoneHotkey { get; set; } = DefaultMuteCameraAndMicrophoneHotkey;
    public HotkeySettings MuteMicrophoneHotkey { get; set; } = DefaultMuteMicrophoneHotkey;
    public HotkeySettings PushToTalkMicrophoneHotkey { get; set; } = DefaultPushToTalkMicrophoneHotkey;
    public HotkeySettings MuteCameraHotkey { get; set; } = DefaultMuteCameraHotkey;
    public bool PushToReverseEnabled { get; set; }
    public string SelectedCamera { get; set; } = string.Empty;
    public string SelectedMicrophone { get; set; } = AllMicrophones;
    public string ToolbarPosition { get; set; } = ToolbarPositions.TopRight;
    public string ToolbarMonitor { get; set; } = ToolbarMonitors.Main;
    public string CameraOverlayImagePath { get; set; } = string.Empty;
    public string ToolbarHide { get; set; } = ToolbarHideModes.WhenBothUnmuted;
    public string StartupAction { get; set; } = StartupActions.Nothing;

    // Settings added by the standalone app.
    public bool Enabled { get; set; } = true;
    public bool CameraPowerOffWhenMuted { get; set; } = true;
    public bool TrayIconShowsState { get; set; } = true;
    public bool PlaySoundOnToggle { get; set; }
    public bool EnforceMicrophoneMute { get; set; }

    public VcmSettings Clone() => (VcmSettings)MemberwiseClone();

    /// <summary>Replaces values that are not among the allowed ones with their defaults.</summary>
    public void Normalize()
    {
        var defaults = new VcmSettings();
        if (!ToolbarPositions.All.Contains(ToolbarPosition)) ToolbarPosition = defaults.ToolbarPosition;
        if (!ToolbarMonitors.Values.Contains(ToolbarMonitor)) ToolbarMonitor = defaults.ToolbarMonitor;
        if (!ToolbarHideModes.All.Contains(ToolbarHide)) ToolbarHide = defaults.ToolbarHide;
        if (!StartupActions.All.Contains(StartupAction)) StartupAction = defaults.StartupAction;
        // PowerToys left this empty until its settings page was first opened; that page then chose "[All]".
        if (string.IsNullOrEmpty(SelectedMicrophone)) SelectedMicrophone = AllMicrophones;
        SelectedCamera ??= string.Empty;
        CameraOverlayImagePath ??= string.Empty;
        MuteCameraAndMicrophoneHotkey ??= DefaultMuteCameraAndMicrophoneHotkey;
        MuteMicrophoneHotkey ??= DefaultMuteMicrophoneHotkey;
        PushToTalkMicrophoneHotkey ??= DefaultPushToTalkMicrophoneHotkey;
        MuteCameraHotkey ??= DefaultMuteCameraHotkey;
    }
}
