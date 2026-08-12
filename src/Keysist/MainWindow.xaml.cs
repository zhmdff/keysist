using Keysist.Diagnostics;
using Keysist.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace Keysist;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private AppWindow? _appWindow;
    private bool _allowClose;

    public MainWindow()
    {
        Logger.Log("MainWindow.ctor: begin");
        InitializeComponent();
        Instance = this;
        Closed += (_, _) =>
        {
            Instance = null;
            Logger.Log("MainWindow.Closed (destroyed)");
        };

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow is not null)
            {
                _appWindow.Resize(new SizeInt32(960, 640));
                _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "keysist.ico");
                if (File.Exists(iconPath))
                    _appWindow.SetIcon(iconPath);
                _appWindow.Closing += OnAppWindowClosing;
            }
            Logger.Log("MainWindow.ctor: AppWindow sized; hide-on-close armed");
        }
        catch (Exception ex)
        {
            Logger.Log($"MainWindow.ctor: AppWindow setup skipped: {ex.Message}");
        }

        ContentFrame.Navigated += OnContentFrameNavigated;

        try
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            NavigateToPage(typeof(DevicesPage));
            Logger.Log("MainWindow.ctor: navigated to DevicesPage");
        }
        catch (Exception ex)
        {
            Logger.Log($"MainWindow.ctor: initial navigation FAILED: {ex}");
            throw;
        }

        Logger.Log("MainWindow.ctor: end");
    }

    public void Reveal()
    {
        _appWindow?.Show();
        Activate();
        Logger.Log("MainWindow.Reveal");
    }

    public void ForceClose()
    {
        _allowClose = true;
        if (_appWindow is not null)
            _appWindow.Closing -= OnAppWindowClosing;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
            return;

        args.Cancel = true;
        sender.Hide();
        Logger.Log("MainWindow: close -> hide (background tray continues)");
    }

    public void NavigateToFlow(string tag)
    {
        Logger.Log($"MainWindow.NavigateToFlow: {tag}");
        var pageType = tag switch
        {
            "NewMapping" => typeof(NewMappingPage),
            "RegisterDevice" => typeof(RegisterDevicePage),
            _ => typeof(DevicesPage),
        };

        NavigateToPage(pageType);
    }

    public void NavigateToHubPage(string tag)
    {
        Logger.Log($"MainWindow.NavigateToHubPage: {tag}");
        var pageType = tag switch
        {
            "Settings" => typeof(SettingsPage),
            _ => typeof(DevicesPage),
        };

        // Fresh hub page so deletes/saves always show up without stale back-stack instances.
        ContentFrame.BackStack.Clear();
        NavigateToPage(pageType);
        if (tag == "Settings")
            NavView.SelectedItem = NavView.SettingsItem;
        else
            SelectNavItem("Devices");
    }

    public void NavigateToPage(Type pageType)
    {
        Logger.Log($"MainWindow.NavigateToPage: {pageType.Name} (Instance={(Instance is null ? "null" : "ok")})");
        try
        {
            if (!ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo()))
                Logger.Log($"MainWindow.NavigateToPage: Navigate returned false for {pageType.Name}");
            else
                Logger.Log($"MainWindow.NavigateToPage: ok -> {ContentFrame.CurrentSourcePageType?.Name}");
        }
        catch (Exception ex)
        {
            Logger.Log($"MainWindow.NavigateToPage FAILED: {ex}");
            // Don't rethrow — UnhandledException was swallowing this as a silent no-op.
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                NavigateToPage(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item)
            return;

        var tag = item.Tag?.ToString();
        if (string.IsNullOrEmpty(tag))
            return;

        if (tag == "Devices" && ContentFrame.CurrentSourcePageType != typeof(DevicesPage))
            NavigateToHubPage("Devices");
    }

    public void GoBackOrNavigateToHub(string fallbackTag)
    {
        // Prefer a fresh hub refresh over a stale back-stack page.
        NavigateToHubPage(string.IsNullOrEmpty(fallbackTag) ? "Devices" : fallbackTag);
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        Logger.Log($"MainWindow.Navigated: {e.SourcePageType?.Name}");

        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        if (e.SourcePageType == typeof(DevicesPage))
            SelectNavItem("Devices");
    }

    private void SelectNavItem(string tag)
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
    }
}
