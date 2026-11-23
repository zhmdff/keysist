using Keysist.Capture;
using Keysist.Config;
using Keysist.Diagnostics;
using Keysist.Pipeline;
using Keysist.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUiApplication = Microsoft.UI.Xaml.Application;
using WinUiButton = Microsoft.UI.Xaml.Controls.Button;
using WinUiComboBox = Microsoft.UI.Xaml.Controls.ComboBox;
using WinUiOrientation = Microsoft.UI.Xaml.Controls.Orientation;
using WinUiRadioButton = Microsoft.UI.Xaml.Controls.RadioButton;
using WinUiTextBox = Microsoft.UI.Xaml.Controls.TextBox;

namespace Keysist.Views;

/// <summary>Edit trigger, remove-key option, and action — with current values prefilled.</summary>
public sealed class EditActionDialog : ContentDialog
{
    private readonly KeyMapping? _mapping;
    private readonly DeviceConfig _device;
    private readonly ActionConfig _action;
    private readonly bool _isAnyKey;
    private CaptureResult? _capturedTrigger;
    private readonly ToggleSwitch _removeKeyToggle = new()
    {
        Header = "Remove original key",
        OffContent = "Keep",
        OnContent = "Block",
    };
    private readonly TextBlock _triggerLabel = new()
    {
        Opacity = 0.7,
        TextWrapping = TextWrapping.WrapWholeWords,
    };
    private readonly TextBlock _currentActionLabel = new()
    {
        Opacity = 0.7,
        TextWrapping = TextWrapping.WrapWholeWords,
    };
    private readonly CapturePanel _triggerCapture = new();
    private readonly CapturePanel _actionCapture = new();
    private readonly StackPanel _triggerPanel = new() { Spacing = 8 };
    private readonly RadioButtons _actionTypeRadios = new();
    private readonly WinUiTextBox _pasteTextBox = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 80,
    };
    private readonly WinUiComboBox _pasteMethodCombo = new() { MinWidth = 120 };
    private readonly WinUiTextBox _pathBox = new() { PlaceholderText = "Path to .exe, .bat, .ps1, …" };
    private readonly WinUiTextBox _argsBox = new();
    private readonly WinUiTextBox _workDirBox = new();
    private readonly StackPanel _sendKeysPanel = new() { Spacing = 8 };
    private readonly StackPanel _pastePanel = new() { Spacing = 8 };
    private readonly StackPanel _launchPanel = new() { Spacing = 8 };

    public EditActionDialog(MappingRowModel row, InputPipeline pipeline)
    {
        _device = row.Device;
        _mapping = row.Mapping;
        _action = row.Mapping?.Action ?? row.Device.AnyKeyAction ?? new ActionConfig();
        _isAnyKey = row.Mapping is null;

        Title = _isAnyKey ? "Edit Action" : "Edit Mapping";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _triggerCapture.Pipeline = pipeline;
        _triggerCapture.DevicePathFilter = string.IsNullOrWhiteSpace(_device.DevicePathMatch)
            ? null
            : _device.DevicePathMatch;
        _actionCapture.Pipeline = pipeline;
        _triggerCapture.CaptureCompleted += OnTriggerCaptured;

        _pasteMethodCombo.Items.Add("type");
        _pasteMethodCombo.Items.Add("clipboard");

        _actionTypeRadios.Items.Add(new WinUiRadioButton { Content = "Send keys" });
        _actionTypeRadios.Items.Add(new WinUiRadioButton { Content = "Paste text" });
        _actionTypeRadios.Items.Add(new WinUiRadioButton { Content = "Launch" });
        _actionTypeRadios.SelectionChanged += (_, _) => UpdatePanels();

        _triggerLabel.Text = $"Current trigger: {row.TriggerSummary}";
        _currentActionLabel.Text = $"Current action: {row.ActionSummary}";
        _removeKeyToggle.IsOn = row.Swallow;

        PrefillTriggerMode();
        BuildContent();
        PrefillActionFields();
        UpdatePanels();

        Opened += (_, _) =>
        {
            PrefillActionFields();
            UpdatePanels();
        };
    }

    private void PrefillTriggerMode()
    {
        if (_mapping is null)
            return;

        var mode = _mapping.GetEffectiveTrigger().Mode.ToLowerInvariant() switch
        {
            "chord" => CaptureMode.Chord,
            "sequence" => CaptureMode.Sequence,
            _ => CaptureMode.Key,
        };
        _triggerCapture.SetMode(mode);
        _triggerCapture.ShowExistingSummary($"Current: {_mapping.GetEffectiveTrigger().Mode} — {string.Join(" + ", _mapping.GetEffectiveTrigger().Keys)}");
    }

    private void PrefillActionFields()
    {
        var type = _action.Type?.ToLowerInvariant() ?? "";
        _actionTypeRadios.SelectedIndex = type switch
        {
            "pastetext" => 1,
            "launch" => 2,
            _ => 0,
        };

        if (type == "pastetext")
        {
            _pasteTextBox.Text = _action.Text ?? "";
            _pasteMethodCombo.SelectedIndex =
                string.Equals(_action.Method, "clipboard", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        else if (type == "launch")
        {
            _pathBox.Text = _action.Path ?? "";
            _argsBox.Text = _action.Args ?? "";
            _workDirBox.Text = _action.WorkingDirectory ?? "";
        }
        else
        {
            _actionCapture.ShowExistingSummary(
                $"Current: {MappingSummaryHelper.FormatActionSummary(_action)} (re-capture to replace)");
            if (_pasteMethodCombo.SelectedIndex < 0)
                _pasteMethodCombo.SelectedIndex = 0;
        }
    }

    private void BuildContent()
    {
        if (!_isAnyKey)
        {
            _triggerPanel.Children.Add(_triggerLabel);
            _triggerPanel.Children.Add(new TextBlock
            {
                Text = "Re-capture to change the trigger. Leave as-is to keep the current trigger.",
                Opacity = 0.7,
                TextWrapping = TextWrapping.WrapWholeWords,
            });
            _triggerPanel.Children.Add(_triggerCapture);
        }
        else
        {
            _triggerPanel.Children.Add(_triggerLabel);
        }

        var removeKeyPanel = new StackPanel { Spacing = 4 };
        removeKeyPanel.Children.Add(_removeKeyToggle);
        removeKeyPanel.Children.Add(new TextBlock
        {
            Text = "When on, the original keystroke is blocked so only your remapped action runs.",
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords,
        });

        _sendKeysPanel.Children.Add(_currentActionLabel);
        _sendKeysPanel.Children.Add(new TextBlock
        {
            Text = "Re-capture keys only if you want to change the send-keys action.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        _sendKeysPanel.Children.Add(_actionCapture);

        _pastePanel.Children.Add(new TextBlock { Text = "Text" });
        _pastePanel.Children.Add(_pasteTextBox);
        var methodRow = new StackPanel { Orientation = WinUiOrientation.Horizontal, Spacing = 8 };
        methodRow.Children.Add(new TextBlock { Text = "Method:", VerticalAlignment = VerticalAlignment.Center });
        methodRow.Children.Add(_pasteMethodCombo);
        _pastePanel.Children.Add(methodRow);

        _launchPanel.Children.Add(new TextBlock { Text = "Path" });
        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_pathBox, 0);
        pathRow.Children.Add(_pathBox);
        var browseBtn = new WinUiButton { Content = "Browse…", MinWidth = 96 };
        if (WinUiApplication.Current.Resources.TryGetValue("SecondaryButtonStyle", out var browseStyleObj)
            && browseStyleObj is Style browseStyle)
            browseBtn.Style = browseStyle;
        browseBtn.Click += async (_, _) =>
        {
            var path = await FileBrowseHelper.PickLaunchTargetAsync(XamlRoot);
            if (!string.IsNullOrEmpty(path))
                _pathBox.Text = path;
        };
        Grid.SetColumn(browseBtn, 1);
        pathRow.Children.Add(browseBtn);
        _launchPanel.Children.Add(pathRow);
        _launchPanel.Children.Add(new TextBlock { Text = "Arguments" });
        _launchPanel.Children.Add(_argsBox);
        _launchPanel.Children.Add(new TextBlock { Text = "Working directory" });
        var dirRow = new Grid { ColumnSpacing = 8 };
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dirRow.Children.Add(_workDirBox);
        var dirBtn = new WinUiButton { Content = "Browse…", MinWidth = 96 };
        if (browseBtn.Style is not null)
            dirBtn.Style = browseBtn.Style;
        dirBtn.Click += async (_, _) =>
        {
            var dir = await FileBrowseHelper.PickFolderAsync();
            if (!string.IsNullOrEmpty(dir))
                _workDirBox.Text = dir;
        };
        Grid.SetColumn(dirBtn, 1);
        dirRow.Children.Add(dirBtn);
        _launchPanel.Children.Add(dirRow);

        Content = new ScrollViewer
        {
            MaxHeight = 560,
            Content = new StackPanel
            {
                Spacing = 12,
                MinWidth = 440,
                Children =
                {
                    _triggerPanel,
                    removeKeyPanel,
                    new TextBlock { Text = "Action", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    _actionTypeRadios,
                    _sendKeysPanel,
                    _pastePanel,
                    _launchPanel,
                },
            },
        };
    }

    private void UpdatePanels()
    {
        var index = ResolveActionIndex();
        _sendKeysPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _pastePanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _launchPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int ResolveActionIndex()
    {
        if (_actionTypeRadios.SelectedIndex >= 0)
            return _actionTypeRadios.SelectedIndex;

        return (_action.Type?.ToLowerInvariant() ?? "") switch
        {
            "pastetext" => 1,
            "launch" => 2,
            _ => 0,
        };
    }

    private async void OnTriggerCaptured(CaptureResult result)
    {
        _capturedTrigger = result;

        var conflict = MappingConflictFinder.Find(App.Workspace.Config.Current.Devices, result, except: _mapping);
        if (conflict is null)
            return;

        var choice = await MappingConflictDialog.ShowAsync(XamlRoot, conflict);
        if (choice == MappingConflictChoice.Cancel)
        {
            _capturedTrigger = null;
            _triggerCapture.ClearResult();
            PrefillTriggerMode();
            return;
        }

        if (choice == MappingConflictChoice.Replace)
        {
            conflict.Device.Mappings?.Remove(conflict.Mapping);
            App.Workspace.Config.Save();
        }
    }

    public bool TryApplyChanges()
    {
        if (_mapping is not null)
            _mapping.Swallow = _removeKeyToggle.IsOn;
        else
            _device.DefaultSwallow = _removeKeyToggle.IsOn;

        var trigger = _capturedTrigger ?? _triggerCapture.Result;
        if (_mapping is not null && trigger is not null)
        {
            if (trigger.Mode == CaptureMode.Sequence && trigger.Keys.Count < 2)
            {
                Logger.Log("EditActionDialog: reject save — sequence needs 2+ keys");
                return false;
            }

            _mapping.Key = trigger.Keys[0];
            _mapping.Trigger = new TriggerConfig
            {
                Mode = ActionConfigBuilder.TriggerModeFromCapture(trigger.Mode),
                Keys = trigger.Keys.ToList(),
            };
            Logger.Log($"EditActionDialog: trigger -> {string.Join("+", trigger.Keys)} mode={trigger.Mode}");
        }

        var index = ResolveActionIndex();
        if (index == 0)
        {
            if (_actionCapture.Result is { } captured)
            {
                _action.Type = "sendKeys";
                _action.Steps = ActionConfigBuilder.CaptureToSendSteps(captured);
                ClearNonSendKeysFields();
                return true;
            }

            // Keep existing send-keys (or non-send action if radios failed to bind).
            if (string.Equals(_action.Type, "sendKeys", StringComparison.OrdinalIgnoreCase)
                && _action.Steps is { Count: > 0 })
                return true;

            if (!string.Equals(_action.Type, "sendKeys", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_action.Type))
                return true;

            Logger.Log("EditActionDialog: reject save — sendKeys needs capture or existing steps");
            return false;
        }

        if (index == 1)
        {
            var method = _pasteMethodCombo.SelectedItem as string
                         ?? _pasteMethodCombo.SelectedIndex switch
                         {
                             1 => "clipboard",
                             _ => "type",
                         };
            if (!ActionConfigBuilder.TryBuildPasteText(_pasteTextBox.Text, method, out var built))
            {
                Logger.Log("EditActionDialog: reject save — paste text empty");
                return false;
            }

            ApplyPaste(built);
            return true;
        }

        if (!ActionConfigBuilder.TryBuildLaunch(_pathBox.Text, _argsBox.Text, _workDirBox.Text, out var launch))
        {
            Logger.Log("EditActionDialog: reject save — launch path empty");
            return false;
        }

        ApplyLaunch(launch);
        return true;
    }

    private void ApplyPaste(ActionConfig built)
    {
        _action.Type = built.Type;
        _action.Text = built.Text;
        _action.Method = built.Method;
        _action.Path = null;
        _action.Args = null;
        _action.WorkingDirectory = null;
        _action.Steps = null;
    }

    private void ApplyLaunch(ActionConfig built)
    {
        _action.Type = built.Type;
        _action.Path = built.Path;
        _action.Args = built.Args;
        _action.WorkingDirectory = built.WorkingDirectory;
        _action.Text = null;
        _action.Method = null;
        _action.Steps = null;
    }

    private void ClearNonSendKeysFields()
    {
        _action.Path = null;
        _action.Args = null;
        _action.WorkingDirectory = null;
        _action.Text = null;
        _action.Method = null;
    }
}
