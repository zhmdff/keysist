// src/Keysist/Startup/AutostartRegistrar.cs
using Microsoft.Win32;

namespace Keysist.Startup;

public static class AutostartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Keysist";

    public static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, System.Windows.Forms.Application.ExecutablePath);
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
}
