using Keysist.Config;
using Keysist.RawInputInterop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Keysist.Views;

public sealed partial class RegisterDevicePage : Page
{
    private readonly IKeysistWorkspace _workspace = App.Workspace;

    public RegisterDevicePage()
    {
        InitializeComponent();
        CapturePanel.Pipeline = _workspace.Pipeline;
        CapturePanel.SetMode(Capture.CaptureMode.Key, locked: true);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CapturePanel.Result is null)
        {
            await ShowMessageAsync("Capture a key on the target device first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            await ShowMessageAsync("Enter a device name.");
            return;
        }

        var match = DeviceIdentity.ShortMatch(CapturePanel.Result.DevicePath);
        var device = _workspace.Config.Current.Devices.FirstOrDefault(d => d.DevicePathMatch == match);
        if (device is null)
        {
            device = new DeviceConfig
            {
                Name = NameBox.Text.Trim(),
                DevicePathMatch = match,
                DefaultSwallow = true,
                Mappings = [],
            };
            _workspace.Config.Current.Devices.Add(device);
        }
        else
        {
            device.Name = NameBox.Text.Trim();
        }

        _workspace.Config.Save();
        _workspace.SetSelectedDevice(device);
        MainWindow.Instance?.GoBackOrNavigateToHub("Devices");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.GoBackOrNavigateToHub("Devices");
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Register Device",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
