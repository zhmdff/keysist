using System.IO;
using Keysist.Config;
using Keysist.Pipeline;
using Keysist.Tray;

namespace Keysist;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
