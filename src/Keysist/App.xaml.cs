using Keysist.Diagnostics;
using Keysist.Tray;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUiApplication = Microsoft.UI.Xaml.Application;

namespace Keysist;

public partial class App : WinUiApplication
{
    private TrayIconController? _tray;
    private MainWindow? _mainWindow;
    private DispatcherQueue? _uiDispatcherQueue;

    public static IKeysistWorkspace Workspace { get; private set; } = null!;

    public static void InitializeWorkspace(IKeysistWorkspace workspace) => Workspace = workspace;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Logger.Log($"App.OnLaunched: uiDispatcher={(_uiDispatcherQueue is null ? "null" : "ok")}");

        try
        {
            _tray = new TrayIconController(Workspace, this);
            _tray.Show();
            Logger.Log("App.OnLaunched: tray shown");
            // First launch opens the UI; closing still hides to tray.
            ShowMainWindow();
            Logger.Log("App.OnLaunched: main window opened");
        }
        catch (Exception ex)
        {
            Logger.Log($"App.OnLaunched: tray failed: {ex}");
            throw;
        }
    }

    public void ShowMainWindow(string? navigateTo = null)
    {
        Logger.Log($"App.ShowMainWindow: navigateTo='{navigateTo ?? "(hub)"}' thread={Environment.CurrentManagedThreadId}");

        try
        {
            if (_mainWindow is null)
            {
                Logger.Log("App.ShowMainWindow: creating MainWindow");
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (_, _) =>
                {
                    // Only cleared when ForceClose/Exit destroys the window.
                    if (ReferenceEquals(_mainWindow, MainWindow.Instance) || MainWindow.Instance is null)
                        _mainWindow = null;
                    Logger.Log("App: MainWindow reference cleared");
                };
            }
            else
            {
                _mainWindow.Reveal();
            }

            _mainWindow.Activate();
            Logger.Log("App.ShowMainWindow: Activate() ok");

            if (!string.IsNullOrEmpty(navigateTo))
            {
                _mainWindow.NavigateToFlow(navigateTo);
                Logger.Log($"App.ShowMainWindow: NavigateToFlow('{navigateTo}') ok");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"App.ShowMainWindow FAILED: {ex}");
            throw;
        }
    }

    public void ExitApplication()
    {
        Logger.Log("App.ExitApplication");
        try
        {
            if (_mainWindow is not null)
            {
                _mainWindow.ForceClose();
                _mainWindow = null;
            }

            _tray?.Dispose();
            Workspace.Pipeline.Stop();
            Workspace.Config.StopWatching();
        }
        catch (Exception ex)
        {
            Logger.Log($"App.ExitApplication cleanup error: {ex}");
        }

        Exit();
    }

    /// <summary>
    /// Marshals work onto the WinUI dispatcher captured at launch.
    /// Tray NotifyIcon callbacks often run off the UI thread — never use GetForCurrentThread there.
    /// </summary>
    public void EnqueueOnUiThread(DispatcherQueueHandler callback)
    {
        var queue = _uiDispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            Logger.Log("EnqueueOnUiThread: no DispatcherQueue available — invoking inline (unsafe)");
            try { callback(); }
            catch (Exception ex) { Logger.Log($"EnqueueOnUiThread inline failed: {ex}"); }
            return;
        }

        if (!queue.TryEnqueue(() =>
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    Logger.Log($"EnqueueOnUiThread callback failed: {ex}");
                }
            }))
        {
            Logger.Log("EnqueueOnUiThread: TryEnqueue returned false");
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Logger.Log($"App.UnhandledException: {e.Message}\n{e.Exception}");
    }
}
