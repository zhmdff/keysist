using Microsoft.Win32;

namespace Keysist.Startup;

public static class AutostartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Keysist";

    /// <summary>
    /// Preferred executable for autostart: root <c>Keysist.exe</c> launcher when present,
    /// otherwise the running app executable.
    /// </summary>
    public static string ResolveAutostartPath(string? runningExecutablePath = null)
    {
        var exe = runningExecutablePath ?? System.Windows.Forms.Application.ExecutablePath;
        try
        {
            var dir = Path.GetDirectoryName(exe);
            if (dir is not null &&
                string.Equals(Path.GetFileName(dir), "app", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent is not null)
                {
                    var launcher = Path.Combine(parent, "Keysist.exe");
                    if (File.Exists(launcher))
                        return launcher;
                }
            }
        }
        catch
        {
            // fall through
        }

        return exe;
    }

    public static void Register(string? executablePath = null)
    {
        try
        {
            var path = ResolveAutostartPath(executablePath);
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, QuoteIfNeeded(path));
        }
        catch (Exception)
        {
            // Non-essential feature: swallow and no-op (e.g. locked-down machine via group policy).
        }
    }

    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null) key!.DeleteValue(ValueName);
        }
        catch (Exception)
        {
            // Non-essential feature: swallow and no-op (e.g. locked-down machine via group policy).
        }
    }

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Enable or disable Run-key autostart.</summary>
    public static void SetEnabled(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }

    private static string QuoteIfNeeded(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
