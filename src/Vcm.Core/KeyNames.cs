namespace Vcm.Core;

/// <summary>Display names for Win32 virtual-key codes (US layout fallback).</summary>
public static class KeyNames
{
    public const int VK_TAB = 0x09;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;

    private static readonly Dictionary<int, string> Names = BuildNames();

    public static bool IsModifier(int vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
        or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU;

    public static string GetName(int vk) => Names.TryGetValue(vk, out var name) ? name : $"0x{vk:X2}";

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "Caps Lock",
            [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PgUp", [0x22] = "PgDn", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "Print Screen",
            [0x2D] = "Insert", [0x2E] = "Delete", [0x5D] = "Apps",
            [0x6A] = "NumPad *", [0x6B] = "NumPad +", [0x6D] = "NumPad -", [0x6E] = "NumPad .", [0x6F] = "NumPad /",
            [0x90] = "Num Lock", [0x91] = "Scroll Lock",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
            [0xAD] = "Volume Mute", [0xAE] = "Volume Down", [0xAF] = "Volume Up",
            [0xB0] = "Next Track", [0xB1] = "Previous Track", [0xB2] = "Stop", [0xB3] = "Play/Pause",
            [VK_SHIFT] = "Shift", [VK_CONTROL] = "Ctrl", [VK_MENU] = "Alt", [VK_LWIN] = "Win", [VK_RWIN] = "Win",
        };
        for (int c = '0'; c <= '9'; c++) names[c] = ((char)c).ToString();
        for (int c = 'A'; c <= 'Z'; c++) names[c] = ((char)c).ToString();
        for (int i = 0; i < 10; i++) names[0x60 + i] = $"NumPad {i}";
        for (int i = 1; i <= 24; i++) names[0x70 + i - 1] = $"F{i}";
        return names;
    }
}
