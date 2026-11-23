// src/Keysist/Actions/WindowsProcessLauncher.cs
using System.Diagnostics;
using System.IO;

namespace Keysist.Actions;

public class WindowsProcessLauncher : IProcessLauncher
{
    public void Launch(string path, string? args, string? workingDirectory)
    {
        // .ps1 has no run verb (ShellExecute opens an editor or nothing); host it in powershell.
        var isPs1 = path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        // Not an existing file (e.g. "claude"): run as a command line in powershell.
        var isCommand = !isPs1 && !File.Exists(path);
        var psi = new ProcessStartInfo
        {
            FileName = isPs1 || isCommand ? "powershell.exe" : path,
            Arguments = isPs1 ? $"-NoExit -ExecutionPolicy Bypass -File \"{path}\" {args}"
                : isCommand ? $"-NoExit -Command \"{path} {args}\""
                : args ?? "",
            WorkingDirectory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory
                : isCommand ? "" : Path.GetDirectoryName(path) ?? "",
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}
