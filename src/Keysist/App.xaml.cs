using Keysist.Tray;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUiApplication = Microsoft.UI.Xaml.Application;

namespace Keysist;

public partial class App : WinUiApplication
{
    private TrayIconController? _tray;
    private MainWindow? _mainWindow;

    public static IKeysistWorkspace Workspace { get; private set; } = null!;

    public static void InitializeWorkspace(IKeysistWorkspace workspace) => Workspace = workspace;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _tray = new TrayIconController(Workspace, this);
        _tray.Show();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Activate();
    }

    public void ExitApplication()
    {
        _tray?.Dispose();
        Workspace.Pipeline.Stop();
        Workspace.Config.StopWatching();
        Exit();
    }

    public void EnqueueOnUiThread(DispatcherQueueHandler callback)
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        if (!queue.TryEnqueue(callback))
            callback();
    }
}
