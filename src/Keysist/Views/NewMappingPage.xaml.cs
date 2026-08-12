using Keysist.Capture;
using Keysist.Config;
using Keysist.RawInputInterop;
using Keysist.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keysist.Views;

public sealed partial class NewMappingPage : Page
{
    private readonly IKeysistWorkspace _workspace = App.Workspace;

    private int _step;
    private CaptureResult? _triggerResult;
    private DeviceConfig? _targetDevice;
    private ActionConfig? _action;

    public NewMappingPage()
    {
        InitializeComponent();
        TriggerCapture.Pipeline = _workspace.Pipeline;
        ActionCapture.Pipeline = _workspace.Pipeline;
        PopulateDeviceFilter();
        ShowStep(0);
    }

    private void PopulateDeviceFilter()
    {
        DeviceFilterCombo.Items.Clear();
        DeviceFilterCombo.Items.Add("(Any device)");
        foreach (var device in _workspace.Config.Current.Devices)
            DeviceFilterCombo.Items.Add(device);

        var selected = _workspace.SelectedDevice;
        if (selected is not null)
        {
            var index = _workspace.Config.Current.Devices.IndexOf(selected);
            DeviceFilterCombo.SelectedIndex = index >= 0 ? index + 1 : 0;
        }
        else
        {
            DeviceFilterCombo.SelectedIndex = 0;
        }

        ApplyDeviceFilter();
    }

    private void DeviceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyDeviceFilter();

    private void ApplyDeviceFilter()
    {
        if (DeviceFilterCombo.SelectedItem is DeviceConfig device)
            TriggerCapture.DevicePathFilter = device.DevicePathMatch;
        else
            TriggerCapture.DevicePathFilter = null;
    }

    private void ActionType_Changed(object sender, RoutedEventArgs e) => UpdateActionPanels();

    private void UpdateActionPanels()
    {
        SendKeysPanel.Visibility = SendKeysRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PastePanel.Visibility = PasteRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LaunchPanel.Visibility = LaunchRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStep(int step)
    {
        _step = step;
        TriggerStepPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionStepPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ReviewStepPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

        StepIndicator.Text = step switch
        {
            0 => "Step 1 of 3 — Trigger",
            1 => "Step 2 of 3 — Action",
            _ => "Step 3 of 3 — Review",
        };

        BackButton.IsEnabled = step > 0;
        NextButton.Content = step == 2 ? "Save" : "Next";

        if (step == 2 && _triggerResult is not null && _action is not null)
        {
            var keys = string.Join(" + ", _triggerResult.Keys);
            ReviewTrigger.Text = $"Trigger: {_triggerResult.Mode} — {keys}";
            ReviewAction.Text = $"Action: {MappingSummaryHelper.FormatActionSummary(_action)}";
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
            ShowStep(_step - 1);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            if (!await TryCompleteTriggerStepAsync())
                return;
            ShowStep(1);
            return;
        }

        if (_step == 1)
        {
            if (!TryCompleteActionStep(out _))
            {
                await ShowMessageAsync("Action", "Complete the action configuration.");
                return;
            }
            ShowStep(2);
            return;
        }

        SaveMapping();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.GoBackOrNavigateToHub("Mappings");
    }

    private async Task<bool> TryCompleteTriggerStepAsync()
    {
        if (TriggerCapture.Result is not { } result)
        {
            await ShowMessageAsync("Trigger", "Capture a trigger first.");
            return false;
        }

        if (result.Mode == CaptureMode.Sequence && result.Keys.Count < 2)
        {
            await ShowMessageAsync("Trigger", "Sequence triggers need at least two keys.");
            return false;
        }

        _triggerResult = result;
        _targetDevice = await ResolveDeviceAsync(result.DevicePath);
        return _targetDevice is not null;
    }

    private async Task<DeviceConfig?> ResolveDeviceAsync(string devicePath)
    {
        var existing = DeviceMatcher.FindMatch(_workspace.Config.Current.Devices, devicePath);
        if (existing is not null)
            return existing;

        var name = await PromptDeviceNameAsync();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var device = new DeviceConfig
        {
            Name = name.Trim(),
            DevicePathMatch = DeviceIdentity.ShortMatch(devicePath),
            DefaultSwallow = true,
            Mappings = [],
        };
        _workspace.Config.Current.Devices.Add(device);
        return device;
    }

    private async Task<string?> PromptDeviceNameAsync()
    {
        var nameBox = new Microsoft.UI.Xaml.Controls.TextBox { PlaceholderText = "Device name", Width = 320 };
        var dialog = new ContentDialog
        {
            Title = "Unknown Device",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "This device is not registered. Enter a name:",
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                    nameBox,
                },
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return nameBox.Text;
    }

    private bool TryCompleteActionStep(out ActionConfig action)
    {
        action = new ActionConfig();

        if (SendKeysRadio.IsChecked == true)
        {
            if (!ActionConfigBuilder.TryBuildSendKeys(ActionCapture.Result, out action))
                return false;
            _action = action;
            return true;
        }

        if (PasteRadio.IsChecked == true)
        {
            var method = (PasteMethodCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "type";
            if (!ActionConfigBuilder.TryBuildPasteText(PasteTextBox.Text, method, out action))
                return false;
            _action = action;
            return true;
        }

        if (!ActionConfigBuilder.TryBuildLaunch(PathBox.Text, ArgsBox.Text, WorkDirBox.Text, out action))
            return false;

        _action = action;
        return true;
    }

    private void SaveMapping()
    {
        if (_triggerResult is null || _targetDevice is null)
            return;

        if (!TryCompleteActionStep(out var action))
            return;

        _targetDevice.Mappings ??= [];
        _targetDevice.Mappings.Add(new KeyMapping
        {
            Key = _triggerResult.Keys[0],
            Trigger = new TriggerConfig
            {
                Mode = ActionConfigBuilder.TriggerModeFromCapture(_triggerResult.Mode),
                Keys = _triggerResult.Keys.ToList(),
            },
            Action = action,
            Swallow = SwallowToggle.IsOn,
        });

        _workspace.Config.Save();
        _workspace.SetSelectedDevice(_targetDevice);
        MainWindow.Instance?.GoBackOrNavigateToHub("Mappings");
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
