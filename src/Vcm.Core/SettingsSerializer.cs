using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcm.Core;

/// <summary>
/// Reads and writes settings in the PowerToys module format:
/// <c>{"name":"Video Conference","version":"1","properties":{"key":{"value":...}}}</c>.
/// A settings.json from PowerToys can therefore be read as-is.
/// </summary>
public static class SettingsSerializer
{
    public const string ModuleName = "Video Conference";
    public const string Version = "1";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static VcmSettings Deserialize(string json)
    {
        var settings = new VcmSettings();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return settings;
        }

        if (root?["properties"] is not JsonObject props)
        {
            return settings;
        }

        settings.MuteCameraAndMicrophoneHotkey = ReadHotkey(props, "mute_camera_and_microphone_hotkey") ?? settings.MuteCameraAndMicrophoneHotkey;
        settings.MuteMicrophoneHotkey = ReadHotkey(props, "mute_microphone_hotkey") ?? settings.MuteMicrophoneHotkey;
        settings.PushToTalkMicrophoneHotkey = ReadHotkey(props, "push_to_talk_microphone_hotkey") ?? settings.PushToTalkMicrophoneHotkey;
        settings.MuteCameraHotkey = ReadHotkey(props, "mute_camera_hotkey") ?? settings.MuteCameraHotkey;
        settings.PushToReverseEnabled = ReadBool(props, "push_to_reverse_enabled") ?? settings.PushToReverseEnabled;
        settings.SelectedCamera = ReadString(props, "selected_camera") ?? settings.SelectedCamera;
        settings.SelectedMicrophone = ReadString(props, "selected_mic") ?? settings.SelectedMicrophone;
        settings.ToolbarPosition = ReadString(props, "toolbar_position") ?? settings.ToolbarPosition;
        settings.ToolbarMonitor = ReadString(props, "toolbar_monitor") ?? settings.ToolbarMonitor;
        settings.CameraOverlayImagePath = ReadString(props, "camera_overlay_image_path") ?? settings.CameraOverlayImagePath;
        settings.ToolbarHide = ReadString(props, "toolbar_hide") ?? settings.ToolbarHide;
        settings.StartupAction = ReadString(props, "startup_action") ?? settings.StartupAction;

        settings.Enabled = ReadBool(props, "enabled") ?? settings.Enabled;
        settings.CameraPowerOffWhenMuted = ReadBool(props, "camera_power_off_when_muted") ?? settings.CameraPowerOffWhenMuted;
        settings.TrayIconShowsState = ReadBool(props, "tray_icon_shows_state") ?? settings.TrayIconShowsState;
        settings.PlaySoundOnToggle = ReadBool(props, "play_sound_on_toggle") ?? settings.PlaySoundOnToggle;
        settings.EnforceMicrophoneMute = ReadBool(props, "enforce_microphone_mute") ?? settings.EnforceMicrophoneMute;

        settings.Normalize();
        return settings;
    }

    public static string Serialize(VcmSettings settings)
    {
        var props = new JsonObject
        {
            ["mute_camera_and_microphone_hotkey"] = Value(HotkeyNode(settings.MuteCameraAndMicrophoneHotkey)),
            ["mute_microphone_hotkey"] = Value(HotkeyNode(settings.MuteMicrophoneHotkey)),
            ["push_to_talk_microphone_hotkey"] = Value(HotkeyNode(settings.PushToTalkMicrophoneHotkey)),
            ["mute_camera_hotkey"] = Value(HotkeyNode(settings.MuteCameraHotkey)),
            ["push_to_reverse_enabled"] = Value(settings.PushToReverseEnabled),
            ["selected_camera"] = Value(settings.SelectedCamera),
            ["selected_mic"] = Value(settings.SelectedMicrophone),
            ["toolbar_position"] = Value(settings.ToolbarPosition),
            ["toolbar_monitor"] = Value(settings.ToolbarMonitor),
            ["camera_overlay_image_path"] = Value(settings.CameraOverlayImagePath),
            ["toolbar_hide"] = Value(settings.ToolbarHide),
            ["startup_action"] = Value(settings.StartupAction),
            ["enabled"] = Value(settings.Enabled),
            ["camera_power_off_when_muted"] = Value(settings.CameraPowerOffWhenMuted),
            ["tray_icon_shows_state"] = Value(settings.TrayIconShowsState),
            ["play_sound_on_toggle"] = Value(settings.PlaySoundOnToggle),
            ["enforce_microphone_mute"] = Value(settings.EnforceMicrophoneMute),
        };

        var root = new JsonObject
        {
            ["properties"] = props,
            ["name"] = ModuleName,
            ["version"] = Version,
        };
        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject Value(JsonNode? node) => new() { ["value"] = node };

    private static JsonNode HotkeyNode(HotkeySettings h) => new JsonObject
    {
        ["win"] = h.Win,
        ["ctrl"] = h.Ctrl,
        ["alt"] = h.Alt,
        ["shift"] = h.Shift,
        ["code"] = h.Code,
        ["key"] = h.Key,
    };

    private static JsonNode? GetValue(JsonObject props, string key) =>
        props[key] is JsonObject wrapper ? wrapper["value"] : null;

    private static string? ReadString(JsonObject props, string key) =>
        GetValue(props, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? ReadBool(JsonObject props, string key) =>
        GetValue(props, key) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static HotkeySettings? ReadHotkey(JsonObject props, string key)
    {
        if (GetValue(props, key) is not JsonObject o)
        {
            return null;
        }

        static bool Flag(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        int code = o["code"] is JsonValue c && c.TryGetValue<int>(out var ci) ? ci : 0;
        string? name = o["key"] is JsonValue k && k.TryGetValue<string>(out var ks) ? ks : null;
        return new HotkeySettings
        {
            Win = Flag(o, "win"),
            Ctrl = Flag(o, "ctrl"),
            Alt = Flag(o, "alt"),
            Shift = Flag(o, "shift"),
            Code = code,
            Key = string.IsNullOrEmpty(name) && code != 0 ? KeyNames.GetName(code) : name ?? string.Empty,
        };
    }
}
