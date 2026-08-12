using Keysist.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Keysist.Views;

public sealed partial class DevicesPage : Page
{
    private readonly IKeysistWorkspace _workspace = App.Workspace;
    private bool _suppressSelectionChanged;

    public DevicesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        var selectedMatch = _workspace.SelectedDevice?.DevicePathMatch;
        var devices = _workspace.Config.Current.Devices;

        DeviceList.ItemsSource = devices;
        EmptyState.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeviceList.Visibility = devices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        _suppressSelectionChanged = true;
        try
        {
            if (!string.IsNullOrEmpty(selectedMatch))
            {
                var index = devices.FindIndex(d => d.DevicePathMatch == selectedMatch);
                DeviceList.SelectedIndex = index >= 0 ? index : devices.Count > 0 ? 0 : -1;
            }
            else
            {
                DeviceList.SelectedIndex = devices.Count > 0 ? 0 : -1;
            }

            if (DeviceList.SelectedItem is DeviceConfig device)
                _workspace.SetSelectedDevice(device);
            else
                _workspace.SetSelectedDevice(null);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        UpdateButtonStates();
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        _workspace.SetSelectedDevice(DeviceList.SelectedItem as DeviceConfig);
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        DeleteDeviceButton.IsEnabled = DeviceList.SelectedItem is DeviceConfig;
    }

    private async void DeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceConfig device)
            return;

        var dialog = new ContentDialog
        {
            Title = "Delete Device",
            Content = $"Delete device \"{device.Name}\" and all of its mappings?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        _workspace.Config.Current.Devices.Remove(device);
        _workspace.Config.Save();
        if (ReferenceEquals(_workspace.SelectedDevice, device))
            _workspace.SetSelectedDevice(null);

        RefreshDevices();
    }

    private void RegisterDevice_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateToPage(typeof(RegisterDevicePage));
    }
}
