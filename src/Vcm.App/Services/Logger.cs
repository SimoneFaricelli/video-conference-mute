using System.IO;

namespace Vcm.App.Services;

/// <summary>Plain text log in %TEMP%\VCM.log, truncated above 10 MB like the original module log.</summary>
internal static class Logger
{
    private const long MaxSize = 10 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "VCM.log");

    public static string FilePath => LogPath;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxSize)
                {
                    info.Delete();
                }
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never break the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
