using Keysist.Config;
using Keysist.Pipeline;
using Keysist.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUiComboBox = Microsoft.UI.Xaml.Controls.ComboBox;
using WinUiRadioButton = Microsoft.UI.Xaml.Controls.RadioButton;
using WinUiTextBox = Microsoft.UI.Xaml.Controls.TextBox;

namespace Keysist.Views;

public sealed class EditActionDialog : ContentDialog
{
    private readonly ActionConfig _action;
    private readonly string _triggerSummary;
    private readonly CapturePanel _actionCapture = new();
    private readonly WinUiRadioButton _sendKeysRadio = new() { Content = "Send keys" };
    private readonly WinUiRadioButton _pasteRadio = new() { Content = "Paste text" };
    private readonly WinUiRadioButton _launchRadio = new() { Content = "Launch" };
    private readonly WinUiTextBox _pasteTextBox = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 80,
    };
    private readonly WinUiComboBox _pasteMethodCombo = new() { MinWidth = 120 };
    private readonly WinUiTextBox _pathBox = new();
    private readonly WinUiTextBox _argsBox = new();
    private readonly WinUiTextBox _workDirBox = new();
    private readonly StackPanel _sendKeysPanel = new() { Spacing = 8 };
    private readonly StackPanel _pastePanel = new() { Spacing = 8 };
    private readonly StackPanel _launchPanel = new() { Spacing = 8 };

    public EditActionDialog(ActionConfig action, string triggerSummary, InputPipeline pipeline)
    {
        _action = action;
        _triggerSummary = triggerSummary;
        Title = "Edit Action";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _actionCapture.Pipeline = pipeline;

        _pasteMethodCombo.Items.Add("type");
        _pasteMethodCombo.Items.Add("clipboard");
        _pasteMethodCombo.SelectedIndex = 0;

        BuildContent();
        LoadFromAction();
        UpdatePanels();

        _sendKeysRadio.Checked += (_, _) => UpdatePanels();
        _pasteRadio.Checked += (_, _) => UpdatePanels();
        _launchRadio.Checked += (_, _) => UpdatePanels();
    }

    private void BuildContent()
    {
        var typeRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 16 };
        typeRow.Children.Add(_sendKeysRadio);
        typeRow.Children.Add(_pasteRadio);
        typeRow.Children.Add(_launchRadio);

        _sendKeysPanel.Children.Add(new TextBlock
        {
            Text = "Re-capture keys to replace the send-keys action.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        _sendKeysPanel.Children.Add(_actionCapture);

        _pastePanel.Children.Add(new TextBlock { Text = "Text" });
        _pastePanel.Children.Add(_pasteTextBox);
        var methodRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        methodRow.Children.Add(new TextBlock { Text = "Method:", VerticalAlignment = VerticalAlignment.Center });
        methodRow.Children.Add(_pasteMethodCombo);
        _pastePanel.Children.Add(methodRow);

        _launchPanel.Children.Add(new TextBlock { Text = "Path" });
        _launchPanel.Children.Add(_pathBox);
        _launchPanel.Children.Add(new TextBlock { Text = "Arguments" });
        _launchPanel.Children.Add(_argsBox);
        _launchPanel.Children.Add(new TextBlock { Text = "Working directory" });
        _launchPanel.Children.Add(_workDirBox);

        Content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 400,
            Children =
            {
                new TextBlock
                {
                    Text = $"Trigger: {_triggerSummary}",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                typeRow,
                _sendKeysPanel,
                _pastePanel,
                _launchPanel,
            },
        };
    }

    private void LoadFromAction()
    {
        if (string.Equals(_action.Type, "sendKeys", StringComparison.OrdinalIgnoreCase))
        {
            _sendKeysRadio.IsChecked = true;
        }
        else if (string.Equals(_action.Type, "pasteText", StringComparison.OrdinalIgnoreCase))
        {
            _pasteRadio.IsChecked = true;
            _pasteTextBox.Text = _action.Text ?? "";
            _pasteMethodCombo.SelectedIndex =
                string.Equals(_action.Method, "clipboard", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        else
        {
            _launchRadio.IsChecked = true;
            _pathBox.Text = _action.Path ?? "";
            _argsBox.Text = _action.Args ?? "";
            _workDirBox.Text = _action.WorkingDirectory ?? "";
        }
    }

    private void UpdatePanels()
    {
        _sendKeysPanel.Visibility = _sendKeysRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _pastePanel.Visibility = _pasteRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _launchPanel.Visibility = _launchRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool TryApplyChanges()
    {
        if (_sendKeysRadio.IsChecked == true)
        {
            if (_actionCapture.Result is { } captured)
            {
                _action.Type = "sendKeys";
                _action.Steps = ActionConfigBuilder.CaptureToSendSteps(captured);
                ClearNonSendKeysFields();
                return true;
            }

            if (_action.Steps is { Count: > 0 })
                return true;

            return false;
        }

        if (_pasteRadio.IsChecked == true)
        {
            if (!ActionConfigBuilder.TryBuildPasteText(_pasteTextBox.Text, _pasteMethodCombo.SelectedItem as string ?? "type", out var built))
                return false;
            ApplyPaste(built);
            return true;
        }

        if (!ActionConfigBuilder.TryBuildLaunch(_pathBox.Text, _argsBox.Text, _workDirBox.Text, out var launch))
            return false;

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
