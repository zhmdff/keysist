using System.Windows.Forms;
using Keysist.Config;
using Keysist.RawInputInterop;

namespace Keysist.Ui;

public class DiscoverDeviceWindow : Form
{
    private readonly ConfigStore _configStore;
    private readonly RawInputWindow _observer = new();
    private readonly Label _status = new() { AutoSize = true, Top = 10, Left = 10, Text = "Press a key on the target device..." };
    private readonly TextBox _nameBox = new() { Top = 40, Left = 10, Width = 200, PlaceholderText = "Device name" };
    private readonly CheckBox _anyKeyBox = new() { Top = 70, Left = 10, Text = "Any key on this device (ignore key identity)" };
    private readonly TextBox _launchPathBox = new() { Top = 100, Left = 10, Width = 300, PlaceholderText = "Path to launch (leave blank to skip)" };
    private readonly Button _saveButton = new() { Top = 130, Left = 10, Text = "Save mapping" };

    private string? _lastDevicePath;
    private int? _lastVkey;

    public DiscoverDeviceWindow(ConfigStore configStore)
    {
        _configStore = configStore;
        Text = "Discover Device";
        Width = 400;
        Height = 220;

        Controls.Add(_status);
        Controls.Add(_nameBox);
        Controls.Add(_anyKeyBox);
        Controls.Add(_launchPathBox);
        Controls.Add(_saveButton);

        _saveButton.Click += (_, _) => SaveMapping();

        _observer.RawKeyDown += OnRawKeyDown;
        _observer.Start();
        FormClosed += (_, _) => _observer.Stop();
    }

    private void OnRawKeyDown(int vkey, string devicePath)
    {
        _lastVkey = vkey;
        _lastDevicePath = devicePath;
        var keyName = ((Keys)vkey).ToString();
        BeginInvoke(() => _status.Text = $"Detected: {keyName} on device path containing '{ShortPath(devicePath)}'");
    }

    private static string ShortPath(string devicePath)
    {
        var idx = devicePath.IndexOf("&PID_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return devicePath;
        var end = devicePath.IndexOf('#', idx);
        return devicePath[..(end > 0 ? end : devicePath.Length)];
    }

    private void SaveMapping()
    {
        if (_lastDevicePath is null || _lastVkey is null)
        {
            MessageBox.Show("Press a key first.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Enter a device name.");
            return;
        }

        var match = ShortPath(_lastDevicePath);
        var device = _configStore.Current.Devices.FirstOrDefault(d => d.DevicePathMatch == match);
        if (device is null)
        {
            device = new DeviceConfig { Name = _nameBox.Text, DevicePathMatch = match, DefaultSwallow = true };
            _configStore.Current.Devices.Add(device);
        }

        var action = new ActionConfig
        {
            Type = "launch",
            Path = _launchPathBox.Text
        };

        if (_anyKeyBox.Checked)
        {
            device.AnyKeyAction = action;
            device.Mappings = null;
        }
        else
        {
            var keyName = ((Keys)_lastVkey.Value).ToString();
            device.Mappings ??= new List<KeyMapping>();
            device.Mappings.RemoveAll(m => m.Key == keyName);
            device.Mappings.Add(new KeyMapping { Key = keyName, Action = action, Swallow = null });
        }

        _configStore.Save();
        MessageBox.Show("Saved.");
    }
}
