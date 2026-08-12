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
    };
    private readonly Button _newMappingButton = new() { Text = "New Mapping", Width = 110 };
    private readonly Button _editButton = new() { Text = "Edit", Width = 80 };
    private readonly Button _deleteButton = new() { Text = "Delete", Width = 80 };
    private readonly Button _registerButton = new() { Text = "Register Device", Width = 110 };
    private readonly CheckBox _swallowBox = new() { Text = "Swallow keystroke", AutoSize = true };
    private bool _suppressCheckedChanged;

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

        _deviceList.DisplayMember = nameof(DeviceConfig.Name);
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
        var selected = _deviceList.SelectedItem as DeviceConfig;
        _deviceList.DataSource = null;
        _deviceList.DataSource = _configStore.Current.Devices.ToList();
        if (selected is not null)
        {
            var index = _configStore.Current.Devices.FindIndex(d => ReferenceEquals(d, selected) || d.DevicePathMatch == selected.DevicePathMatch);
            if (index >= 0)
                _deviceList.SelectedIndex = index;
        }
        else if (_deviceList.Items.Count > 0)
        {
            _deviceList.SelectedIndex = 0;
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
            item.Tag = (device, (KeyMapping?)null);
            _mappingList.Items.Add(item);
        }

        foreach (var mapping in device.Mappings ?? [])
        {
            var item = new ListViewItem(FormatTriggerSummary(mapping));
            item.SubItems.Add(FormatActionSummary(mapping.Action));
            item.Tag = (device, mapping);
            _mappingList.Items.Add(item);
        }

        SyncSwallowCheckbox();
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var hasDevice = _deviceList.SelectedItem is DeviceConfig;
        var hasMapping = _mappingList.SelectedItems.Count > 0;
        _newMappingButton.Enabled = hasDevice;
        _editButton.Enabled = hasMapping;
        _deleteButton.Enabled = hasDevice;
        _swallowBox.Enabled = hasMapping;
    }

    private void SyncSwallowCheckbox()
    {
        _suppressCheckedChanged = true;
        try
        {
            if (_mappingList.SelectedItems.Count > 0 &&
                _mappingList.SelectedItems[0].Tag is (DeviceConfig device, var mappingObj))
            {
                var mapping = (KeyMapping?)mappingObj;
                _swallowBox.Checked = mapping?.Swallow ?? device.DefaultSwallow;
            }
            else
            {
                _swallowBox.Checked = false;
            }
        }
        finally
        {
            _suppressCheckedChanged = false;
        }
    }

    private void ApplySwallowChange()
    {
        if (_suppressCheckedChanged) return;
        if (_mappingList.SelectedItems.Count == 0) return;
        if (_mappingList.SelectedItems[0].Tag is not (DeviceConfig device, var mappingObj)) return;

        var mapping = (KeyMapping?)mappingObj;
        if (mapping is not null)
            mapping.Swallow = _swallowBox.Checked;
        else
            device.DefaultSwallow = _swallowBox.Checked;

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
        if (_mappingList.SelectedItems.Count == 0) return;
        if (_mappingList.SelectedItems[0].Tag is not (DeviceConfig device, var mappingObj)) return;

        var mapping = (KeyMapping?)mappingObj;
        var action = mapping?.Action ?? device.AnyKeyAction;
        if (action is null) return;

        var triggerSummary = mapping is not null ? FormatTriggerSummary(mapping) : "[any key]";
        using var dialog = new ActionEditorDialog(action, triggerSummary);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _configStore.Save();
            PopulateMappings();
        }
    }

    private void DeleteSelected()
    {
        if (_mappingList.SelectedItems.Count > 0 &&
            _mappingList.SelectedItems[0].Tag is (DeviceConfig owner, var mappingObj))
        {
            var mapping = (KeyMapping?)mappingObj;
            if (mapping is not null)
                owner.Mappings?.Remove(mapping);
            else
                owner.AnyKeyAction = null;
        }
        else if (_deviceList.SelectedItem is DeviceConfig device)
        {
            var confirm = MessageBox.Show(
                this,
                $"Delete device \"{device.Name}\" and all of its mappings?",
                "Delete Device",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            _configStore.Current.Devices.Remove(device);
        }
        else
        {
            return;
        }

        _configStore.Save();
        PopulateDevices();
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
