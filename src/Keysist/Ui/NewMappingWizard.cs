using System.Windows.Forms;
using Keysist.Capture;
using Keysist.Config;
using Keysist.Pipeline;
using Keysist.RawInputInterop;

namespace Keysist.Ui;

public sealed class NewMappingWizard : Form
{
    private readonly ConfigStore _configStore;
    private readonly InputPipeline _pipeline;

    private readonly Panel _stepHost = new() { Dock = DockStyle.Fill };
    private readonly Button _backButton = new() { Text = "Back", Width = 80 };
    private readonly Button _nextButton = new() { Text = "Next", Width = 80 };
    private readonly Button _cancelButton = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };

    private readonly CaptureControl _triggerCapture;
    private readonly ComboBox _deviceFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ActionPickerPanel _actionPicker;
    private readonly CheckBox _swallowBox = new() { Text = "Swallow keystroke", AutoSize = true, Checked = true };

    private int _step;
    private CaptureResult? _triggerResult;
    private DeviceConfig? _targetDevice;

    public NewMappingWizard(ConfigStore configStore, InputPipeline pipeline)
    {
        _configStore = configStore;
        _pipeline = pipeline;
        _triggerCapture = new CaptureControl(pipeline) { Dock = DockStyle.Top };
        _actionPicker = new ActionPickerPanel(pipeline) { Dock = DockStyle.Fill };

        Text = "New Mapping";
        Width = 480;
        Height = 420;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        buttonRow.Controls.Add(_cancelButton);
        buttonRow.Controls.Add(_nextButton);
        buttonRow.Controls.Add(_backButton);

        Controls.Add(_stepHost);
        Controls.Add(buttonRow);

        _backButton.Click += (_, _) => GoBack();
        _nextButton.Click += (_, _) => GoNext();
        _deviceFilterCombo.SelectedIndexChanged += (_, _) => ApplyDeviceFilter();

        PopulateDeviceFilter();
        ShowStep(0);
    }

    private void PopulateDeviceFilter()
    {
        _deviceFilterCombo.Items.Clear();
        _deviceFilterCombo.Items.Add("(Any device)");
        foreach (var device in _configStore.Current.Devices)
            _deviceFilterCombo.Items.Add(device);
        _deviceFilterCombo.DisplayMember = nameof(DeviceConfig.Name);
        _deviceFilterCombo.SelectedIndex = 0;
    }

    private void ApplyDeviceFilter()
    {
        if (_deviceFilterCombo.SelectedItem is DeviceConfig device)
            _triggerCapture.SetDevicePathFilter(device.DevicePathMatch);
        else
            _triggerCapture.SetDevicePathFilter(null);
    }

    private void ShowStep(int step)
    {
        _step = step;
        _stepHost.Controls.Clear();

        switch (step)
        {
            case 0:
                Text = "New Mapping — Trigger";
                BuildTriggerStep();
                break;
            case 1:
                Text = "New Mapping — Action";
                _stepHost.Controls.Add(_actionPicker);
                break;
            case 2:
                Text = "New Mapping — Options";
                BuildOptionsStep();
                break;
        }

        _backButton.Enabled = step > 0;
        _nextButton.Text = step == 2 ? "Save" : "Next";
    }

    private void BuildTriggerStep()
    {
        var filterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8),
        };
        filterRow.Controls.Add(new Label { Text = "Device filter:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        filterRow.Controls.Add(_deviceFilterCombo);

        _stepHost.Controls.Add(_triggerCapture);
        _stepHost.Controls.Add(filterRow);
    }

    private void BuildOptionsStep()
    {
        var panel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        panel.Controls.Add(_swallowBox);
        _stepHost.Controls.Add(panel);
    }

    private void GoBack()
    {
        if (_step > 0)
            ShowStep(_step - 1);
    }

    private void GoNext()
    {
        if (_step == 0)
        {
            if (!TryCompleteTriggerStep())
                return;
            ShowStep(1);
            return;
        }

        if (_step == 1)
        {
            if (!_actionPicker.TryBuildAction(out _))
                return;
            ShowStep(2);
            return;
        }

        SaveMapping();
    }

    private bool TryCompleteTriggerStep()
    {
        if (_triggerCapture.Result is not { } result)
        {
            MessageBox.Show(this, "Capture a trigger first.", "Trigger");
            return false;
        }

        if (result.Mode == CaptureMode.Sequence && result.Keys.Count < 2)
        {
            MessageBox.Show(this, "Sequence triggers need at least two keys.", "Trigger");
            return false;
        }

        _triggerResult = result;
        _targetDevice = ResolveDevice(result.DevicePath);
        return _targetDevice is not null;
    }

    private DeviceConfig? ResolveDevice(string devicePath)
    {
        var existing = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (existing is not null)
            return existing;

        var name = PromptDeviceName();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var device = new DeviceConfig
        {
            Name = name.Trim(),
            DevicePathMatch = DeviceIdentity.ShortMatch(devicePath),
            DefaultSwallow = true,
            Mappings = new List<KeyMapping>(),
        };
        _configStore.Current.Devices.Add(device);
        PopulateDeviceFilter();
        return device;
    }

    private string? PromptDeviceName()
    {
        using var dialog = new Form
        {
            Text = "Unknown Device",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Width = 360,
            Height = 150,
        };

        var label = new Label
        {
            Text = "This device is not registered. Enter a name:",
            AutoSize = true,
            Left = 12,
            Top = 12,
        };
        var nameBox = new TextBox { Left = 12, Top = 36, Width = 320 };
        var ok = new Button { Text = "OK", Left = 168, Top = 72, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 252, Top = 72, Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange([label, nameBox, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK ? nameBox.Text : null;
    }

    private void SaveMapping()
    {
        if (_triggerResult is null || _targetDevice is null)
            return;

        if (!_actionPicker.TryBuildAction(out var action, this))
            return;

        _targetDevice.Mappings ??= new List<KeyMapping>();
        _targetDevice.Mappings.Add(new KeyMapping
        {
            Key = _triggerResult.Keys[0],
            Trigger = new TriggerConfig
            {
                Mode = TriggerModeFromCapture(_triggerResult.Mode),
                Keys = _triggerResult.Keys.ToList(),
            },
            Action = action,
            Swallow = _swallowBox.Checked,
        });

        _configStore.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string TriggerModeFromCapture(CaptureMode mode) => mode switch
    {
        CaptureMode.Chord => "chord",
        CaptureMode.Sequence => "sequence",
        _ => "key",
    };
}
