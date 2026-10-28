using Microsoft.Win32;

namespace Vcm.App.Services;

internal static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>App theme (used by the toolbar, like WindowsColors::is_dark_mode in PowerToys).</summary>
    public static bool AppsUseDarkTheme => ReadDword("AppsUseLightTheme") == 0;

    /// <summary>Taskbar theme, which decides the tray icon colour.</summary>
    public static bool TaskbarUsesDarkTheme => ReadDword("SystemUsesLightTheme") == 0;

    private static int ReadDword(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int value ? value : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
