using System.IO;
using System.Threading;
using Keysist.Config;
using Keysist.Diagnostics;
using Keysist.Pipeline;
using WinRT;

namespace Keysist;

internal static class Program
{
    // Fixed GUID -- do not randomize. Two running instances would mean two competing
    // LL hooks and two Raw Input registrations fighting each other.
    private const string SingleInstanceMutexName = "Keysist-SingleInstance-{6E3F1B2A-6E77-4E86-9C0A-6B6E3E9F9B41}";

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Log($"AppDomain.UnhandledException: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Log($"TaskScheduler.UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.Forms.MessageBox.Show(
                "Keysist is already running.", "Keysist",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            return;
        }

        Logger.Log("Program.Main: starting");

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Keysist", "config.json");

        var configStore = new ConfigStore(configPath);
        configStore.Load();
        configStore.StartWatching();

        var pipeline = new InputPipeline(configStore);
        pipeline.Start();

        var workspace = new KeysistWorkspace(configStore, pipeline);

        try
        {
            ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(p =>
            {
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                App.InitializeWorkspace(workspace);
                new App();
                Logger.Log("Program.Main: WinUI Application started");
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Program.Main: WinUI Application.Start failed: {ex}");
            throw;
        }
        finally
        {
            Logger.Log("Program.Main: shutting down pipeline");
            pipeline.Stop();
            configStore.StopWatching();
        }
    }
}
