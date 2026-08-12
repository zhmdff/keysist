using System.Windows.Forms;
using Keysist.Capture;
using Keysist.Config;
using Keysist.Pipeline;
using Keysist.RawInputInterop;

namespace Keysist.Ui;

public class RegisterDeviceWindow : Form
{
    private readonly ConfigStore _configStore;
    private readonly CaptureControl _captureControl;
    private readonly TextBox _nameBox = new() { Width = 280, PlaceholderText = "Device name" };
    private readonly Button _saveButton = new() { Text = "Save" };

    public RegisterDeviceWindow(ConfigStore configStore, InputPipeline pipeline)
    {
        _configStore = configStore;
        _captureControl = new CaptureControl(pipeline) { Dock = DockStyle.Top };
        _captureControl.SetMode(CaptureMode.Key, locked: true);

        Text = "Register Device";
        Width = 420;
        Height = 220;

        var nameRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        nameRow.Controls.Add(new Label { Text = "Name:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        nameRow.Controls.Add(_nameBox);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttonRow.Controls.Add(_saveButton);

        Controls.Add(buttonRow);
        Controls.Add(nameRow);
        Controls.Add(_captureControl);

        _saveButton.Click += (_, _) => SaveDevice();
    }

    private void SaveDevice()
    {
        if (_captureControl.Result is null)
        {
            MessageBox.Show("Capture a key on the target device first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Enter a device name.");
            return;
        }

        var match = DeviceIdentity.ShortMatch(_captureControl.Result.DevicePath);
        var device = _configStore.Current.Devices.FirstOrDefault(d => d.DevicePathMatch == match);
        if (device is null)
        {
            device = new DeviceConfig
            {
                Name = _nameBox.Text.Trim(),
                DevicePathMatch = match,
                DefaultSwallow = true,
            };
            _configStore.Current.Devices.Add(device);
        }
        else
        {
            device.Name = _nameBox.Text.Trim();
        }

        _configStore.Save();
        MessageBox.Show("Saved.");
    }
}
