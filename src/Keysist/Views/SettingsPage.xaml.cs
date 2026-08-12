using Keysist.Diagnostics;
using Keysist.Startup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Keysist.Views;

public sealed partial class SettingsPage : Page
{
    private bool _suppressToggle;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Refresh();
    }

    private void Refresh()
    {
        _suppressToggle = true;
        try
        {
            var enabled = AutostartRegistrar.IsRegistered();
            StartupToggle.IsOn = enabled;
            var path = AutostartRegistrar.ResolveAutostartPath();
            StatusText.Text = enabled
                ? $"Enabled. Startup target: {path}"
                : $"Disabled. Would start: {path}";
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle)
            return;

        var enabled = StartupToggle.IsOn;
        Logger.Log($"Settings: Start with Windows -> {enabled}");
        AutostartRegistrar.SetEnabled(enabled);
        Refresh();
    }
}
