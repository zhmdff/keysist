// src/Keysist/Ui/EditMappingsWindow.cs
using System.Windows.Forms;
using Keysist.Config;

namespace Keysist.Ui;

public class EditMappingsWindow : Form
{
    private readonly ConfigStore _configStore;
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill };
    private readonly Button _deleteButton = new() { Dock = DockStyle.Bottom, Text = "Delete selected" };
    private readonly CheckBox _swallowBox = new() { Dock = DockStyle.Bottom, Text = "Swallow keystroke" };

    public EditMappingsWindow(ConfigStore configStore)
    {
        _configStore = configStore;
        Text = "Edit Mappings";
        Width = 500;
        Height = 400;

        Controls.Add(_tree);
        Controls.Add(_swallowBox);
        Controls.Add(_deleteButton);

        _deleteButton.Click += (_, _) => DeleteSelected();
        _swallowBox.CheckedChanged += (_, _) => ApplySwallowChange();
        _tree.AfterSelect += (_, _) => SyncSwallowCheckbox();

        Populate();
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
    }

    private void SyncSwallowCheckbox()
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

    private void ApplySwallowChange()
    {
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
}
