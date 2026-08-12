// src/Keysist/Actions/WindowsProcessLauncher.cs
using System.Diagnostics;
using System.IO;

namespace Keysist.Actions;

public class WindowsProcessLauncher : IProcessLauncher
{
    public void Launch(string path, string? args, string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args ?? "",
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Path.GetDirectoryName(path) ?? "" : workingDirectory,
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}
