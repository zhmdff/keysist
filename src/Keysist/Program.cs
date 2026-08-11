using System.IO;
using System.Threading;
using Keysist.Config;
using Keysist.Pipeline;
using Keysist.Tray;

namespace Keysist;

internal static class Program
{
    // Fixed GUID -- do not randomize. Two running instances would mean two competing
    // LL hooks and two Raw Input registrations fighting each other.
    private const string SingleInstanceMutexName = "Keysist-SingleInstance-{6E3F1B2A-6E77-4E86-9C0A-6B6E3E9F9B41}";

    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.Forms.MessageBox.Show(
                "Keysist is already running.", "Keysist",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Keysist", "config.json");

        var configStore = new ConfigStore(configPath);
        configStore.Load();
        configStore.StartWatching();

        var pipeline = new InputPipeline(configStore);
        pipeline.Start();

        using var tray = new TrayIconController(configStore, pipeline);
        tray.Show();

        System.Windows.Forms.Application.Run();

        pipeline.Stop();
        configStore.StopWatching();
    }
}
