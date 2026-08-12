using Keysist.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace Keysist;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        Closed += (_, _) => Instance = null;

        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                WinRT.Interop.WindowNative.GetWindowHandle(this)));
        if (appWindow is not null)
        {
            appWindow.Resize(new SizeInt32(960, 640));
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        }



        ContentFrame.Navigated += OnContentFrameNavigated;

        NavView.BackRequested += (_, _) =>
        {
            if (ContentFrame.CanGoBack)
                ContentFrame.GoBack();
        };

        NavView.SelectedItem = NavView.MenuItems[0];

        ContentFrame.Navigate(typeof(DevicesPage));

    }



    public void NavigateToHubPage(string tag)

    {

        var pageType = tag switch

        {

            "Devices" => typeof(DevicesPage),

            "Mappings" => typeof(MappingsPage),

            _ => typeof(DevicesPage),

        };



        ContentFrame.Navigate(pageType);

        SelectNavItem(tag);

    }



    public void NavigateToPage(Type pageType)

    {

        ContentFrame.Navigate(pageType);

    }



    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)

    {

        if (args.IsSettingsSelected)

            return;



        if (args.SelectedItem is not NavigationViewItem item)

            return;



        var tag = item.Tag?.ToString();

        if (string.IsNullOrEmpty(tag))

            return;



        var pageType = tag switch

        {

            "Devices" => typeof(DevicesPage),

            "Mappings" => typeof(MappingsPage),

            _ => null,

        };



        if (pageType is null || ContentFrame.CurrentSourcePageType == pageType)

            return;



        ContentFrame.Navigate(pageType);

    }



    public void GoBackOrNavigateToHub(string fallbackTag)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
        else
            NavigateToHubPage(fallbackTag);
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)

    {

        NavView.IsBackEnabled = ContentFrame.CanGoBack;



        var tag = e.SourcePageType switch

        {

            var t when t == typeof(DevicesPage) => "Devices",

            var t when t == typeof(MappingsPage) => "Mappings",

            _ => null,

        };



        if (tag is not null)

            SelectNavItem(tag);

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

