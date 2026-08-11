// src/Keysist/Ui/EditMappingsWindow.cs
using System.Windows.Forms;
using Keysist.Config;

namespace Keysist.Ui;

public class EditMappingsWindow : Form
{
    private readonly ConfigStore _configStore;
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill };
    private readonly Button _deleteButton = new() { Dock = DockStyle.Bottom, Text = "Delete selected" };
    private readonly Button _addMappingButton = new() { Dock = DockStyle.Bottom, Text = "Add Mapping" };
    private readonly Button _editButton = new() { Dock = DockStyle.Bottom, Text = "Edit" };
    private readonly CheckBox _swallowBox = new() { Dock = DockStyle.Bottom, Text = "Swallow keystroke" };
    private bool _suppressCheckedChanged;

    public EditMappingsWindow(ConfigStore configStore)
    {
        _configStore = configStore;
        Text = "Edit Mappings";
        Width = 500;
        Height = 400;

        Controls.Add(_tree);
        Controls.Add(_swallowBox);
        Controls.Add(_editButton);
        Controls.Add(_addMappingButton);
        Controls.Add(_deleteButton);

        _deleteButton.Click += (_, _) => DeleteSelected();
        _addMappingButton.Click += (_, _) => AddMapping();
        _editButton.Click += (_, _) => EditSelected();
        _swallowBox.CheckedChanged += (_, _) => ApplySwallowChange();
        _tree.AfterSelect += (_, _) =>
        {
            SyncSwallowCheckbox();
            UpdateButtonStates();
        };

        Populate();
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var tag = _tree.SelectedNode?.Tag;
        _addMappingButton.Enabled = tag is DeviceConfig;
        _editButton.Enabled = tag is (DeviceConfig, var _);
    }

    private void Populate()
    {
        _tree.Nodes.Clear();
        foreach (var device in _configStore.Current.Devices)
        {
            var deviceNode = new TreeNode($"{device.Name} ({device.DevicePathMatch})") { Tag = device };
            if (device.AnyKeyAction is not null)
            {
                deviceNode.Nodes.Add(new TreeNode($"[any key] -> {device.AnyKeyAction.Type}") { Tag = (device, (KeyMapping?)null) });
            }
            foreach (var mapping in device.Mappings ?? new List<KeyMapping>())
            {
                deviceNode.Nodes.Add(new TreeNode($"{mapping.Key} -> {mapping.Action.Type}") { Tag = (device, mapping) });
            }
            _tree.Nodes.Add(deviceNode);
        }
        _tree.ExpandAll();
        UpdateButtonStates();
    }

    private void SyncSwallowCheckbox()
    {
        _suppressCheckedChanged = true;
        try
        {
            if (_tree.SelectedNode?.Tag is (DeviceConfig device, var mappingObj))
            {
                var mapping = (KeyMapping?)mappingObj;
                _swallowBox.Enabled = true;
                _swallowBox.Checked = mapping?.Swallow ?? device.DefaultSwallow;
            }
            else
            {
                _swallowBox.Enabled = false;
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

        if (_tree.SelectedNode?.Tag is (DeviceConfig _, KeyMapping mapping))
        {
            mapping.Swallow = _swallowBox.Checked;
            _configStore.Save();
        }
    }

    private void DeleteSelected()
    {
        if (_tree.SelectedNode?.Tag is DeviceConfig device)
        {
            _configStore.Current.Devices.Remove(device);
        }
        else if (_tree.SelectedNode?.Tag is (DeviceConfig owner, var mappingObj))
        {
            var mapping = (KeyMapping?)mappingObj;
            if (mapping is not null) owner.Mappings?.Remove(mapping);
            else owner.AnyKeyAction = null;
        }
        else
        {
            return;
        }

        _configStore.Save();
        Populate();
    }

    private void AddMapping()
    {
        if (_tree.SelectedNode?.Tag is not DeviceConfig device) return;

        var choice = MessageBox.Show(
            "Add an any-key mapping (applies to every key on this device)?\n\nYes = any-key mapping\nNo = mapping for a specific key",
            "Add Mapping",
            MessageBoxButtons.YesNoCancel);
        if (choice == DialogResult.Cancel) return;

        var anyKey = choice == DialogResult.Yes;

        if (anyKey && device.Mappings is { Count: > 0 })
        {
            MessageBox.Show("This device already has per-key mappings. Delete them first before adding an any-key mapping.");
            return;
        }
        if (!anyKey && device.AnyKeyAction is not null)
        {
            MessageBox.Show("This device already has an any-key mapping. Delete it first before adding per-key mappings.");
            return;
        }

        using var dialog = new ActionEditorDialog(device, anyKey);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _configStore.Save();
            Populate();
        }
    }

    private void EditSelected()
    {
        if (_tree.SelectedNode?.Tag is not (DeviceConfig device, var mappingObj)) return;

        var mapping = (KeyMapping?)mappingObj;
        var action = mapping?.Action ?? device.AnyKeyAction;
        if (action is null) return;

        using var dialog = new ActionEditorDialog(action);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _configStore.Save();
            Populate();
        }
    }
}
