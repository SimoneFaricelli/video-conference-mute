using System.IO;
using Vcm.Core;

namespace Vcm.App.Services;

/// <summary>%LOCALAPPDATA%\VCM\settings.json; imports the PowerToys Video Conference settings on first run.</summary>
internal static class SettingsStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCM");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly string PowerToysPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "PowerToys", "Video Conference", "settings.json");

    public static bool IsFirstRun => !File.Exists(FilePath);

    public static VcmSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return SettingsSerializer.Deserialize(File.ReadAllText(FilePath));
            }

            if (File.Exists(PowerToysPath))
            {
                Logger.Info($"Importing PowerToys settings from {PowerToysPath}");
                var imported = SettingsSerializer.Deserialize(File.ReadAllText(PowerToysPath));
                Save(imported);
                return imported;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error("Loading settings failed", ex);
        }

        return new VcmSettings();
    }

    public static void Save(VcmSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, SettingsSerializer.Serialize(settings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error("Saving settings failed", ex);
        }
    }
}
