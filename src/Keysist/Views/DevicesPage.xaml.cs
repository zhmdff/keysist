using Keysist.Config;
using Keysist.Diagnostics;
using Keysist.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using XamlApplication = Microsoft.UI.Xaml.Application;
using XamlBrush = Microsoft.UI.Xaml.Media.Brush;
using XamlButton = Microsoft.UI.Xaml.Controls.Button;
using XamlColor = Windows.UI.Color;
using XamlHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using XamlOrientation = Microsoft.UI.Xaml.Controls.Orientation;
using XamlVerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;

namespace Keysist.Views;

public sealed partial class DevicesPage : Page
{
    private readonly IKeysistWorkspace _workspace = App.Workspace;

    public DevicesPage()
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
        DevicesHost.Children.Clear();
        var devices = _workspace.Config.Current.Devices;
        EmptyState.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var device in devices)
            DevicesHost.Children.Add(BuildDeviceCard(device));

        Logger.Log($"DevicesPage.Refresh: devices={devices.Count}");
    }

    private Border BuildDeviceCard(DeviceConfig device)
    {
        var name = string.IsNullOrWhiteSpace(device.Name) ? device.DevicePathMatch : device.Name;
        var mappings = new List<MappingRowModel>();

        if (device.AnyKeyAction is not null)
        {
            mappings.Add(new MappingRowModel
            {
                Device = device,
                Mapping = null,
                ShowDeviceLabel = false,
            });
        }

        foreach (var mapping in device.Mappings ?? [])
        {
            mappings.Add(new MappingRowModel
            {
                Device = device,
                Mapping = mapping,
                ShowDeviceLabel = false,
            });
        }

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new StackPanel { Spacing = 2 };
        titleBlock.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = device.DevicePathMatch,
            FontSize = 12,
            Opacity = 0.55,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = mappings.Count == 0
                ? "No mappings"
                : $"{mappings.Count} mapping{(mappings.Count == 1 ? "" : "s")}",
            FontSize = 12,
            Opacity = 0.7,
        });
        Grid.SetColumn(titleBlock, 0);
        header.Children.Add(titleBlock);

        var actions = new StackPanel { Orientation = XamlOrientation.Horizontal, Spacing = 8 };
        var addBtn = new XamlButton { Content = "Add mapping", Tag = device };
        if (XamlApplication.Current.Resources.TryGetValue("SecondaryButtonStyle", out var addStyle) && addStyle is Style s1)
            addBtn.Style = s1;
        addBtn.Click += AddMappingForDevice_Click;
        actions.Children.Add(addBtn);
        actions.Children.Add(CreateIconButton(
            glyph: "\uE74D",
            toolTip: "Delete device",
            foreground: DangerBrush(),
            tag: device,
            onClick: DeleteDevice_Click));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(header);

        foreach (var row in mappings)
            body.Children.Add(BuildMappingRow(row));

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            HorizontalAlignment = XamlHorizontalAlignment.Stretch,
            Child = body,
        };

        if (XamlApplication.Current.Resources.TryGetValue("KeysistSurfaceBrush", out var bg) && bg is XamlBrush bgBrush)
            card.Background = bgBrush;
        if (XamlApplication.Current.Resources.TryGetValue("KeysistBorderBrush", out var stroke) && stroke is XamlBrush strokeBrush)
            card.BorderBrush = strokeBrush;

        return card;
    }

    private Border BuildMappingRow(MappingRowModel row)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // One line: trigger  →  action
        var line = new StackPanel
        {
            Orientation = XamlOrientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = XamlVerticalAlignment.Center,
        };
        line.Children.Add(new TextBlock
        {
            Text = row.TriggerSummary,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = XamlVerticalAlignment.Center,
        });
        line.Children.Add(new TextBlock
        {
            Text = "→",
            Opacity = 0.45,
            VerticalAlignment = XamlVerticalAlignment.Center,
        });
        line.Children.Add(new TextBlock
        {
            Text = row.ActionSummary,
            FontSize = 14,
            Opacity = 0.8,
            VerticalAlignment = XamlVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        Grid.SetColumn(line, 0);
        grid.Children.Add(line);

        var buttons = new StackPanel
        {
            Orientation = XamlOrientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = XamlVerticalAlignment.Center,
        };
        buttons.Children.Add(CreateIconButton(
            glyph: "\uE70F",
            toolTip: "Edit",
            foreground: TextBrush(),
            tag: row,
            onClick: EditMapping_Click));
        buttons.Children.Add(CreateIconButton(
            glyph: "\uE74D",
            toolTip: "Delete",
            foreground: DangerBrush(),
            tag: row,
            onClick: DeleteMapping_Click));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = XamlHorizontalAlignment.Stretch,
            Child = grid,
        };

        if (XamlApplication.Current.Resources.TryGetValue("KeysistBgBrush", out var bg) && bg is XamlBrush bgBrush)
            border.Background = bgBrush;
        if (XamlApplication.Current.Resources.TryGetValue("KeysistBorderBrush", out var stroke) && stroke is XamlBrush strokeBrush)
            border.BorderBrush = strokeBrush;

        return border;
    }

    private static XamlButton CreateIconButton(
        string glyph,
        string toolTip,
        XamlBrush foreground,
        object tag,
        RoutedEventHandler onClick)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = foreground,
            IsHitTestVisible = false,
        };
        var btn = new XamlButton
        {
            Tag = tag,
            Content = icon,
            ClickMode = ClickMode.Release,
        };
        if (XamlApplication.Current.Resources.TryGetValue("IconButtonStyle", out var style) && style is Style s)
            btn.Style = s;
        ToolTipService.SetToolTip(btn, toolTip);
        btn.Click += onClick;
        return btn;
    }

    private static XamlBrush TextBrush()
    {
        if (XamlApplication.Current.Resources.TryGetValue("KeysistTextBrush", out var b) && b is XamlBrush brush)
            return brush;
        return new SolidColorBrush(XamlColor.FromArgb(255, 27, 27, 27));
    }

    private static XamlBrush DangerBrush()
    {
        if (XamlApplication.Current.Resources.TryGetValue("KeysistDangerBrush", out var b) && b is XamlBrush brush)
            return brush;
        return new SolidColorBrush(XamlColor.FromArgb(255, 196, 43, 28));
    }

    private void RegisterDevice_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("DevicesPage: Register Device");
        MainWindow.Instance?.NavigateToPage(typeof(RegisterDevicePage));
    }

    private void NewMapping_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("DevicesPage: New Mapping");
        MainWindow.Instance?.NavigateToPage(typeof(NewMappingPage));
    }

    private void AddMappingForDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not XamlButton { Tag: DeviceConfig device })
            return;

        _workspace.SetSelectedDevice(device);
        Logger.Log($"DevicesPage: Add mapping for '{device.Name}'");
        MainWindow.Instance?.NavigateToPage(typeof(NewMappingPage));
    }

    private async void DeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not XamlButton { Tag: DeviceConfig device })
            return;

        // Let the mouse release finish so it doesn't dismiss the dialog instantly.
        await Task.Delay(50);

        var name = string.IsNullOrWhiteSpace(device.Name) ? device.DevicePathMatch : device.Name;
        var dialog = new ContentDialog
        {
            Title = "Delete Device",
            Content = $"Delete device \"{name}\" and all of its mappings?",
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

        Logger.Log($"DevicesPage: deleted device '{name}'");
        Refresh();
    }

    private async void EditMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not XamlButton { Tag: MappingRowModel row })
            return;

        var action = row.Mapping?.Action ?? row.Device.AnyKeyAction;
        if (action is null)
            return;

        await Task.Delay(50);

        var dialog = new EditActionDialog(row, _workspace.Pipeline)
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
        var trigger = row.Mapping is not null
            ? MappingSummaryHelper.FormatTriggerSummary(row.Mapping)
            : "[any key]";
        Logger.Log(
            $"DevicesPage: edited device='{row.DeviceName}' trigger='{trigger}' " +
            $"action='{row.ActionSummary}' swallow={row.Swallow}");
        Refresh();
    }

    private async void DeleteMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not XamlButton { Tag: MappingRowModel row })
            return;

        await Task.Delay(50);

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
        Logger.Log($"DevicesPage: deleted mapping '{trigger}' on '{row.DeviceName}'");
        Refresh();
    }
}
