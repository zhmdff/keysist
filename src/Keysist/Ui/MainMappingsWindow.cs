using System.Windows.Forms;
using Keysist.Config;
using Keysist.Pipeline;

namespace Keysist.Ui;

public sealed class MainMappingsWindow : Form
{
    private readonly ConfigStore _configStore;
    private readonly InputPipeline _pipeline;

    private readonly ListBox _deviceList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListView _mappingList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };
    private readonly Button _newMappingButton = new() { Text = "New Mapping", Width = 110 };
    private readonly Button _editButton = new() { Text = "Edit", Width = 80 };
    private readonly Button _deleteButton = new() { Text = "Delete", Width = 80 };
    private readonly Button _registerButton = new() { Text = "Register Device", Width = 110 };
    private readonly CheckBox _swallowBox = new() { Text = "Swallow keystroke", AutoSize = true };
    private bool _suppressCheckedChanged;

    private sealed record MappingRow(DeviceConfig Device, KeyMapping? Mapping);

    public MainMappingsWindow(ConfigStore configStore, InputPipeline pipeline)
    {
        _configStore = configStore;
        _pipeline = pipeline;

        Text = "Devices & Mappings";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;

        _mappingList.Columns.Add("Trigger", 220);
        _mappingList.Columns.Add("Action", 360);

        _deviceList.SelectedIndexChanged += (_, _) =>
        {
            PopulateMappings();
            UpdateButtonStates();
        };
        _mappingList.SelectedIndexChanged += (_, _) =>
        {
            SyncSwallowCheckbox();
            UpdateButtonStates();
        };

        _newMappingButton.Click += (_, _) => OpenNewMapping();
        _editButton.Click += (_, _) => EditSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _registerButton.Click += (_, _) => OpenRegisterDevice();
        _swallowBox.CheckedChanged += (_, _) => ApplySwallowChange();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 200,
        };
        split.Panel1.Controls.Add(_deviceList);
        split.Panel2.Controls.Add(_mappingList);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(8),
            WrapContents = false,
        };
        buttonRow.Controls.AddRange([
            _newMappingButton,
            _editButton,
            _deleteButton,
            _registerButton,
            _swallowBox,
        ]);

        Controls.Add(split);
        Controls.Add(buttonRow);

        PopulateDevices();
        UpdateButtonStates();
    }

    private void PopulateDevices()
    {
        var selectedMatch = (_deviceList.SelectedItem as DeviceConfig)?.DevicePathMatch;
        _deviceList.BeginUpdate();
        try
        {
            _deviceList.Items.Clear();
            foreach (var device in _configStore.Current.Devices)
                _deviceList.Items.Add(device);

            if (!string.IsNullOrEmpty(selectedMatch))
            {
                for (var i = 0; i < _deviceList.Items.Count; i++)
                {
                    if (_deviceList.Items[i] is DeviceConfig d && d.DevicePathMatch == selectedMatch)
                    {
                        _deviceList.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (_deviceList.Items.Count > 0)
            {
                _deviceList.SelectedIndex = 0;
            }
        }
        finally
        {
            _deviceList.EndUpdate();
        }

        PopulateMappings();
    }

    private void PopulateMappings()
    {
        _mappingList.Items.Clear();
        if (_deviceList.SelectedItem is not DeviceConfig device)
            return;

        if (device.AnyKeyAction is not null)
        {
            var item = new ListViewItem("[any key]");
            item.SubItems.Add(FormatActionSummary(device.AnyKeyAction));
            item.Tag = new MappingRow(device, null);
            _mappingList.Items.Add(item);
        }

        foreach (var mapping in device.Mappings ?? [])
        {
            var item = new ListViewItem(FormatTriggerSummary(mapping));
            item.SubItems.Add(FormatActionSummary(mapping.Action));
            item.Tag = new MappingRow(device, mapping);
            _mappingList.Items.Add(item);
        }

        SyncSwallowCheckbox();
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var hasDevice = _deviceList.SelectedItem is DeviceConfig;
        var hasMapping = TryGetSelectedMappingRow(out _);
        _newMappingButton.Enabled = hasDevice;
        _editButton.Enabled = hasMapping;
        // Prefer mapping delete when a row is selected; otherwise device delete.
        _deleteButton.Enabled = hasMapping || hasDevice;
        _swallowBox.Enabled = hasMapping;
    }

    private void SyncSwallowCheckbox()
    {
        _suppressCheckedChanged = true;
        try
        {
            if (TryGetSelectedMappingRow(out var row))
                _swallowBox.Checked = row.Mapping?.Swallow ?? row.Device.DefaultSwallow;
            else
                _swallowBox.Checked = false;
        }
        finally
        {
            _suppressCheckedChanged = false;
        }
    }

    private void ApplySwallowChange()
    {
        if (_suppressCheckedChanged) return;
        if (!TryGetSelectedMappingRow(out var row)) return;

        if (row.Mapping is not null)
            row.Mapping.Swallow = _swallowBox.Checked;
        else
            row.Device.DefaultSwallow = _swallowBox.Checked;

        _configStore.Save();
    }

    private void OpenNewMapping()
    {
        using var wizard = new NewMappingWizard(_configStore, _pipeline);
        if (wizard.ShowDialog(this) == DialogResult.OK)
            PopulateDevices();
    }

    private void OpenRegisterDevice()
    {
        using var window = new RegisterDeviceWindow(_configStore, _pipeline);
        window.ShowDialog(this);
        PopulateDevices();
    }

    private void EditSelected()
    {
        if (!TryGetSelectedMappingRow(out var row)) return;

        var action = row.Mapping?.Action ?? row.Device.AnyKeyAction;
        if (action is null) return;

        var triggerSummary = row.Mapping is not null ? FormatTriggerSummary(row.Mapping) : "[any key]";
        using var dialog = new ActionEditorDialog(action, triggerSummary);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _configStore.Save();
            PopulateMappings();
        }
    }

    private void DeleteSelected()
    {
        // Snapshot selection before any UI churn from MessageBox / focus changes.
        if (TryGetSelectedMappingRow(out var row))
        {
            if (row.Mapping is not null)
            {
                row.Device.Mappings?.Remove(row.Mapping);
            }
            else
            {
                row.Device.AnyKeyAction = null;
            }

            _configStore.Save();
            PopulateMappings();
            return;
        }

        if (_deviceList.SelectedItem is not DeviceConfig device)
            return;

        var confirm = MessageBox.Show(
            this,
            $"Delete device \"{device.Name}\" and all of its mappings?",
            "Delete Device",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        _configStore.Current.Devices.Remove(device);
        _configStore.Save();
        PopulateDevices();
    }

    private bool TryGetSelectedMappingRow(out MappingRow row)
    {
        row = null!;
        if (_mappingList.SelectedItems.Count == 0)
            return false;
        if (_mappingList.SelectedItems[0].Tag is not MappingRow selected)
            return false;
        row = selected;
        return true;
    }

    internal static string FormatTriggerSummary(KeyMapping mapping)
    {
        var trigger = mapping.GetEffectiveTrigger();
        return trigger.Mode.ToLowerInvariant() switch
        {
            "chord" => string.Join("+", trigger.Keys),
            "sequence" => string.Join(" → ", trigger.Keys),
            _ => trigger.Keys.Count > 0 ? trigger.Keys[0] : mapping.Key,
        };
    }

    internal static string FormatActionSummary(ActionConfig action)
    {
        return action.Type.ToLowerInvariant() switch
        {
            "launch" => $"Launch: {action.Path}",
            "sendkeys" => FormatSendKeysSummary(action),
            "pastetext" => FormatPasteTextSummary(action),
            _ => action.Type,
        };
    }

    private static string FormatSendKeysSummary(ActionConfig action)
    {
        if (action.Steps is not { Count: > 0 })
            return "Send keys";

        if (action.Steps.Count == 1)
            return $"Send keys: {action.Steps[0].Value}";

        var parts = action.Steps
            .Where(s => s.Send is "vkey" or "text")
            .Select(s => s.Value)
            .Take(4)
            .ToList();
        var summary = string.Join(", ", parts);
        if (action.Steps.Count > parts.Count)
            summary += ", …";
        return $"Send keys: {summary}";
    }

    private static string FormatPasteTextSummary(ActionConfig action)
    {
        var method = string.IsNullOrWhiteSpace(action.Method) ? "type" : action.Method;
        var text = action.Text ?? "";
        if (text.Length > 40)
            text = text[..37] + "…";
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return $"Paste ({method}): {text}";
    }
}
