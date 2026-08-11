// src/Keysist/Diagnostics/Logger.cs
using System.IO;

namespace Keysist.Diagnostics;

/// <summary>
/// Minimal append-only rolling log file at %AppData%\Keysist\keysist.log.
/// No levels, no rotation, no config -- just enough to debug "why isn't this working"
/// on first real-hardware runs. All file I/O is best-effort: failures here must never
/// crash the app.
/// </summary>
public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Keysist", "keysist.log");

    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
