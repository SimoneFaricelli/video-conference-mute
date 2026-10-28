using System.Text.Json.Serialization;

namespace Vcm.Core;

/// <summary>
/// A keyboard shortcut, serialized with the same shape PowerToys uses:
/// <c>{"win":true,"ctrl":false,"alt":false,"shift":true,"code":81,"key":"Q"}</c>.
/// </summary>
public sealed record HotkeySettings
{
    [JsonPropertyName("win")] public bool Win { get; init; }
    [JsonPropertyName("ctrl")] public bool Ctrl { get; init; }
    [JsonPropertyName("alt")] public bool Alt { get; init; }
    [JsonPropertyName("shift")] public bool Shift { get; init; }

    /// <summary>Win32 virtual-key code of the non-modifier key.</summary>
    [JsonPropertyName("code")] public int Code { get; init; }

    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    public HotkeySettings() { }

    public HotkeySettings(bool win, bool ctrl, bool alt, bool shift, int code)
    {
        Win = win;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Code = code;
        Key = KeyNames.GetName(code);
    }

    [JsonIgnore]
    public bool HasModifier => Win || Ctrl || Alt || Shift;

    [JsonIgnore]
    public bool IsEmpty => Code == 0 && !HasModifier;

    /// <summary>
    /// Same rule as the PowerToys ShortcutControl: at least one modifier plus one non-modifier key,
    /// and not one of the shortcuts reserved for keyboard navigation (Tab / Shift+Tab).
    /// </summary>
    [JsonIgnore]
    public bool IsValid => HasModifier && Code != 0 && !KeyNames.IsModifier(Code) && !IsAccessibleShortcut;

    [JsonIgnore]
    private bool IsAccessibleShortcut => Code == KeyNames.VK_TAB && !Win && !Ctrl && !Alt;

    /// <summary>The keys in display order (Win, Ctrl, Alt, Shift, key), as shown on the key caps.</summary>
    public IReadOnlyList<string> GetKeyList()
    {
        var keys = new List<string>(5);
        if (Win) keys.Add("Win");
        if (Ctrl) keys.Add("Ctrl");
        if (Alt) keys.Add("Alt");
        if (Shift) keys.Add("Shift");
        if (Code != 0) keys.Add(string.IsNullOrEmpty(Key) ? KeyNames.GetName(Code) : Key);
        return keys;
    }

    /// <summary>True when both shortcuts are triggered by the same key combination.</summary>
    public bool SameCombination(HotkeySettings? other) =>
        other is not null && other.Code == Code && other.Win == Win && other.Ctrl == Ctrl && other.Alt == Alt && other.Shift == Shift;

    public override string ToString() => string.Join(" + ", GetKeyList());
}
