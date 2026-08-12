using Keysist.Startup;
using Xunit;

public class AutostartRegistrarTests
{
    [Fact]
    public void ResolveAutostartPath_PrefersRootLauncher_WhenRunningFromAppFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "keysist-autostart-" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "app");
        Directory.CreateDirectory(appDir);

        var launcher = Path.Combine(root, "Keysist.exe");
        var appExe = Path.Combine(appDir, "Keysist.exe");
        File.WriteAllText(launcher, "launcher");
        File.WriteAllText(appExe, "app");

        try
        {
            var resolved = AutostartRegistrar.ResolveAutostartPath(appExe);
            Assert.Equal(launcher, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAutostartPath_FallsBack_WhenNoLauncher()
    {
        var root = Path.Combine(Path.GetTempPath(), "keysist-autostart-" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "app");
        Directory.CreateDirectory(appDir);
        var appExe = Path.Combine(appDir, "Keysist.exe");
        File.WriteAllText(appExe, "app");

        try
        {
            var resolved = AutostartRegistrar.ResolveAutostartPath(appExe);
            Assert.Equal(appExe, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
