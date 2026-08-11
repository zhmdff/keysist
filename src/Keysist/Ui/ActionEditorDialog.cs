// src/Keysist/Ui/ActionEditorDialog.cs
using System.Windows.Forms;
using Keysist.Actions;
using Keysist.Config;

namespace Keysist.Ui;

/// <summary>
/// Reusable modal dialog for creating or editing an <see cref="ActionConfig"/> (launch or
/// sendKeys). Two modes:
///  - Add mode (ctor taking a DeviceConfig): creates a brand-new ActionConfig and, on OK,
///    either sets device.AnyKeyAction or adds/replaces a per-key KeyMapping on the device.
///  - Edit mode (ctor taking an existing ActionConfig): mutates that same ActionConfig
///    instance in place on OK, so the caller doesn't need to splice a new object back into
///    the config graph.
/// </summary>
public class ActionEditorDialog : Form
{
    private readonly bool _editMode;
    private readonly ActionConfig _action;
    private readonly DeviceConfig? _device;
    private readonly bool _anyKey;
    private readonly List<SendStep> _steps = new();

    // Key selection (add mode, per-key only)
    private readonly Label _keyLabel = new() { AutoSize = true, Top = 10, Left = 10, Text = "Key:" };
    private readonly ComboBox _keyCombo = new() { Top = 28, Left = 10, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };

    // Action-type selection
    private readonly RadioButton _launchRadio = new() { Text = "Launch a program", AutoSize = true };
    private readonly RadioButton _sendKeysRadio = new() { Text = "Send keys", AutoSize = true };

    // Launch panel
    private readonly Panel _launchPanel = new();
    private readonly Label _pathLabel = new() { AutoSize = true, Text = "Path:" };
    private readonly TextBox _pathBox = new() { Width = 360 };
    private readonly Label _argsLabel = new() { AutoSize = true, Text = "Arguments (optional):" };
    private readonly TextBox _argsBox = new() { Width = 360 };
    private readonly Label _workDirLabel = new() { AutoSize = true, Text = "Working directory (optional):" };
    private readonly TextBox _workDirBox = new() { Width = 360 };

    // SendKeys panel
    private readonly Panel _sendKeysPanel = new();
    private readonly ListView _stepsListView = new() { View = View.Details, FullRowSelect = true, Width = 360, Height = 140 };
    private readonly ComboBox _stepSendCombo = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _stepValueBox = new() { Width = 150 };
    private readonly NumericUpDown _stepDelayUpDown = new() { Width = 70, Minimum = 0, Maximum = 60000 };
    private readonly Button _addStepButton = new() { Text = "Add Step", Width = 80 };
    private readonly Button _removeStepButton = new() { Text = "Remove Step", Width = 90 };

    private readonly Button _okButton = new() { Text = "OK", DialogResult = DialogResult.None };
    private readonly Button _cancelButton = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    /// <summary>Add mode: creates a new ActionConfig and, on OK, attaches it to <paramref name="device"/>.</summary>
    public ActionEditorDialog(DeviceConfig device, bool anyKey)
    {
        _editMode = false;
        _device = device;
        _anyKey = anyKey;
        _action = new ActionConfig { Type = "launch" };

        BuildLayout(showKeyCombo: !anyKey);
        _launchRadio.Checked = true;
        UpdatePanels();
    }

    /// <summary>Edit mode: mutates <paramref name="action"/> in place on OK.</summary>
    public ActionEditorDialog(ActionConfig action)
    {
        _editMode = true;
        _action = action;

        BuildLayout(showKeyCombo: false);

        if (string.Equals(action.Type, "sendKeys", StringComparison.OrdinalIgnoreCase))
        {
            _sendKeysRadio.Checked = true;
            if (action.Steps is not null) _steps.AddRange(action.Steps);
            RefreshStepsList();
        }
        else
        {
            _launchRadio.Checked = true;
            _pathBox.Text = action.Path ?? "";
            _argsBox.Text = action.Args ?? "";
            _workDirBox.Text = action.WorkingDirectory ?? "";
        }

        UpdatePanels();
    }

    private void BuildLayout(bool showKeyCombo)
    {
        Text = _editMode ? "Edit Action" : "Add Mapping";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Width = 440;
        Height = 460;

        var top = 10;

        if (showKeyCombo)
        {
            Controls.Add(_keyLabel);
            Controls.Add(_keyCombo);
            _keyCombo.Items.AddRange(Enum.GetNames<Keys>());
            top = 60;
        }

        _launchRadio.Top = top;
        _launchRadio.Left = 10;
        _sendKeysRadio.Top = top;
        _sendKeysRadio.Left = 160;
        Controls.Add(_launchRadio);
        Controls.Add(_sendKeysRadio);
        _launchRadio.CheckedChanged += (_, _) => UpdatePanels();
        _sendKeysRadio.CheckedChanged += (_, _) => UpdatePanels();

        var panelTop = top + 30;

        // Launch panel
        _launchPanel.Top = panelTop;
        _launchPanel.Left = 10;
        _launchPanel.Width = 400;
        _launchPanel.Height = 130;
        _pathLabel.Top = 0; _pathLabel.Left = 0;
        _pathBox.Top = 18; _pathBox.Left = 0;
        _argsLabel.Top = 46; _argsLabel.Left = 0;
        _argsBox.Top = 64; _argsBox.Left = 0;
        _workDirLabel.Top = 92; _workDirLabel.Left = 0;
        _workDirBox.Top = 110; _workDirBox.Left = 0;
        _launchPanel.Controls.Add(_pathLabel);
        _launchPanel.Controls.Add(_pathBox);
        _launchPanel.Controls.Add(_argsLabel);
        _launchPanel.Controls.Add(_argsBox);
        _launchPanel.Controls.Add(_workDirLabel);
        _launchPanel.Controls.Add(_workDirBox);
        Controls.Add(_launchPanel);

        // SendKeys panel
        _sendKeysPanel.Top = panelTop;
        _sendKeysPanel.Left = 10;
        _sendKeysPanel.Width = 400;
        _sendKeysPanel.Height = 230;

        _stepsListView.Top = 0;
        _stepsListView.Left = 0;
        _stepsListView.Columns.Add("Send", 70);
        _stepsListView.Columns.Add("Value", 160);
        _stepsListView.Columns.Add("Delay (ms)", 90);

        _stepSendCombo.Top = 150;
        _stepSendCombo.Left = 0;
        _stepSendCombo.Items.AddRange(new object[] { "vkey", "text" });
        _stepSendCombo.SelectedIndex = 0;

        _stepValueBox.Top = 150;
        _stepValueBox.Left = 90;
        _stepValueBox.PlaceholderText = "value";

        _stepDelayUpDown.Top = 150;
        _stepDelayUpDown.Left = 250;

        _addStepButton.Top = 180;
        _addStepButton.Left = 0;
        _addStepButton.Click += (_, _) => AddStep();

        _removeStepButton.Top = 180;
        _removeStepButton.Left = 90;
        _removeStepButton.Click += (_, _) => RemoveStep();

        _sendKeysPanel.Controls.Add(_stepsListView);
        _sendKeysPanel.Controls.Add(_stepSendCombo);
        _sendKeysPanel.Controls.Add(_stepValueBox);
        _sendKeysPanel.Controls.Add(_stepDelayUpDown);
        _sendKeysPanel.Controls.Add(_addStepButton);
        _sendKeysPanel.Controls.Add(_removeStepButton);
        Controls.Add(_sendKeysPanel);

        var buttonTop = panelTop + 240;
        _okButton.Top = buttonTop;
        _okButton.Left = 10;
        _okButton.Click += (_, _) => OnOk();

        _cancelButton.Top = buttonTop;
        _cancelButton.Left = 100;

        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Height = buttonTop + 80;
    }

    private void UpdatePanels()
    {
        var launch = _launchRadio.Checked;
        _launchPanel.Visible = launch;
        _sendKeysPanel.Visible = !launch;
    }

    private void AddStep()
    {
        if (_stepSendCombo.SelectedItem is not string send)
        {
            MessageBox.Show("Select a send type.");
            return;
        }
        var value = _stepValueBox.Text;
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show("Enter a step value.");
            return;
        }
        if (send == "vkey")
        {
            try
            {
                VirtualKeyNames.Resolve(value);
            }
            catch (ArgumentException)
            {
                var known = string.Join(", ", VirtualKeyNames.KnownNames);
                MessageBox.Show(
                    $"Unrecognized vkey name '{value}'.\nRecognized names: {known}.\nOr use a 0x-prefixed hex code (e.g. 0x1B).",
                    "Unknown vkey");
                return;
            }
        }

        _steps.Add(new SendStep { Send = send, Value = value, DelayMs = (int)_stepDelayUpDown.Value });
        RefreshStepsList();
        _stepValueBox.Clear();
    }

    private void RemoveStep()
    {
        if (_stepsListView.SelectedIndices.Count == 0) return;
        var idx = _stepsListView.SelectedIndices[0];
        _steps.RemoveAt(idx);
        RefreshStepsList();
    }

    private void RefreshStepsList()
    {
        _stepsListView.Items.Clear();
        foreach (var step in _steps)
        {
            var item = new ListViewItem(step.Send);
            item.SubItems.Add(step.Value);
            item.SubItems.Add(step.DelayMs.ToString());
            _stepsListView.Items.Add(item);
        }
    }

    private void OnOk()
    {
        if (_launchRadio.Checked)
        {
            if (string.IsNullOrWhiteSpace(_pathBox.Text))
            {
                MessageBox.Show("Enter a path to launch.");
                return;
            }

            _action.Type = "launch";
            _action.Path = _pathBox.Text;
            _action.Args = string.IsNullOrWhiteSpace(_argsBox.Text) ? null : _argsBox.Text;
            _action.WorkingDirectory = string.IsNullOrWhiteSpace(_workDirBox.Text) ? null : _workDirBox.Text;
            _action.Steps = null;
        }
        else
        {
            if (_steps.Count == 0)
            {
                MessageBox.Show("Add at least one step.");
                return;
            }

            _action.Type = "sendKeys";
            _action.Steps = new List<SendStep>(_steps);
            _action.Path = null;
            _action.Args = null;
            _action.WorkingDirectory = null;
        }

        if (!_editMode)
        {
            if (_anyKey)
            {
                _device!.AnyKeyAction = _action;
            }
            else
            {
                if (_keyCombo.SelectedItem is not string keyName)
                {
                    MessageBox.Show("Select a key.");
                    return;
                }

                _device!.Mappings ??= new List<KeyMapping>();
                _device.Mappings.RemoveAll(m => m.Key == keyName);
                _device.Mappings.Add(new KeyMapping { Key = keyName, Action = _action, Swallow = null });
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
