using Keysist.Config;
using Keysist.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Keysist.Views;

public sealed partial class MappingsPage : Page
{
    private readonly IKeysistWorkspace _workspace = App.Workspace;
    private readonly List<MappingRowModel> _rows = [];

    public MappingsPage()
    {
        InitializeComponent();
        _workspace.SelectedDeviceChanged += OnSelectedDeviceChanged;
        Unloaded += (_, _) => _workspace.SelectedDeviceChanged -= OnSelectedDeviceChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshMappings();
    }

    private void OnSelectedDeviceChanged() =>
        DispatcherQueue.TryEnqueue(RefreshMappings);

    private void RefreshMappings()
    {
        _rows.Clear();
        var selected = _workspace.SelectedDevice;

        if (selected is not null)
        {
            AddDeviceMappings(selected, showDeviceLabel: false);
            FilterLabel.Text = $"Showing mappings for {DeviceDisplayName(selected)}";
            EmptyStateBody.Text = $"Create a mapping for {DeviceDisplayName(selected)}.";
        }
        else
        {
            foreach (var device in _workspace.Config.Current.Devices)
                AddDeviceMappings(device, showDeviceLabel: true);

            FilterLabel.Text = "Showing all devices";
            EmptyStateBody.Text = "Create a mapping to remap keys on your registered devices.";
        }

        MappingsRepeater.ItemsSource = null;
        MappingsRepeater.ItemsSource = _rows;

        EmptyState.Visibility = _rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddDeviceMappings(DeviceConfig device, bool showDeviceLabel)
    {
        if (device.AnyKeyAction is not null)
        {
            _rows.Add(new MappingRowModel
            {
                Device = device,
                Mapping = null,
                ShowDeviceLabel = showDeviceLabel,
            });
        }

        foreach (var mapping in device.Mappings ?? [])
        {
            _rows.Add(new MappingRowModel
            {
                Device = device,
                Mapping = mapping,
                ShowDeviceLabel = showDeviceLabel,
            });
        }
    }

    private static string DeviceDisplayName(DeviceConfig device) =>
        string.IsNullOrWhiteSpace(device.Name) ? device.DevicePathMatch : device.Name;

    private void SwallowToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle)
            return;

        if (toggle.Tag is not MappingRowModel row)
            return;

        if (row.Mapping is not null)
            row.Mapping.Swallow = toggle.IsOn;
        else
            row.Device.DefaultSwallow = toggle.IsOn;

        _workspace.Config.Save();
    }

    private async void EditMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: MappingRowModel row })
            return;

        var action = row.Mapping?.Action ?? row.Device.AnyKeyAction;
        if (action is null)
            return;

        var trigger = row.Mapping is not null
            ? MappingSummaryHelper.FormatTriggerSummary(row.Mapping)
            : "[any key]";

        var dialog = new EditActionDialog(action, trigger, _workspace.Pipeline)
        {
            XamlRoot = XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!dialog.TryApplyChanges())
                args.Cancel = true;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        _workspace.Config.Save();
        RefreshMappings();
    }

    private async void DeleteMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: MappingRowModel row })
            return;

        var trigger = row.Mapping is not null
            ? MappingSummaryHelper.FormatTriggerSummary(row.Mapping)
            : "[any key]";

        var dialog = new ContentDialog
        {
            Title = "Delete Mapping",
            Content = $"Delete mapping \"{trigger}\"?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (row.Mapping is not null)
            row.Device.Mappings?.Remove(row.Mapping);
        else
            row.Device.AnyKeyAction = null;

        _workspace.Config.Save();
        RefreshMappings();
    }

    private void NewMapping_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateToPage(typeof(NewMappingPage));
    }
}

public sealed class MappingRowModel
{
    public required DeviceConfig Device { get; init; }

    public KeyMapping? Mapping { get; init; }

    public bool ShowDeviceLabel { get; init; }

    public Visibility ShowDeviceLabelVisibility =>
        ShowDeviceLabel ? Visibility.Visible : Visibility.Collapsed;

    public string DeviceName =>
        string.IsNullOrWhiteSpace(Device.Name) ? Device.DevicePathMatch : Device.Name;

    public string TriggerSummary => Mapping is not null
        ? MappingSummaryHelper.FormatTriggerSummary(Mapping)
        : "[any key]";

    public string ActionSummary => MappingSummaryHelper.FormatActionSummary(
        Mapping?.Action ?? Device.AnyKeyAction ?? new ActionConfig());

    public bool Swallow => Mapping?.Swallow ?? Device.DefaultSwallow;
}
