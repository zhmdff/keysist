using System.Diagnostics;
using System.Windows.Forms;

namespace Keysist.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var launcherDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appExe = Path.Combine(launcherDir, "app", "Keysist.exe");

        if (!File.Exists(appExe))
        {
            MessageBox.Show(
                $"Could not find the app at:\n{appExe}\n\nRe-run publish.ps1 or keep Keysist.exe next to the app\\ folder.",
                "Keysist",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe)!,
            UseShellExecute = true,
        });
    }
}
