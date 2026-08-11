// src/Keysist/Startup/AutostartRegistrar.cs
using Microsoft.Win32;

namespace Keysist.Startup;

public static class AutostartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Keysist";

    public static void Register()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)!;
        key.SetValue(ValueName, System.Windows.Forms.Application.ExecutablePath);
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)!;
        if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName);
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false)!;
        return key.GetValue(ValueName) is not null;
    }
}
