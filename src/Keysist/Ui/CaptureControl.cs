using System.Windows.Forms;
using Keysist.Capture;
using Keysist.Pipeline;

namespace Keysist.Ui;

public class CaptureControl : UserControl
{
    private readonly CaptureSession _session = new();
    private readonly ComboBox _modeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Button _startButton = new() { Text = "Start" };
    private readonly Button _stopButton = new() { Text = "Stop", Enabled = false };
    private readonly Label _resultLabel = new() { AutoSize = true, Text = "Press Start, then input on the target device." };

    private InputPipeline? _pipeline;
    private bool _subscribed;
    private bool _modeLocked;
    private string? _devicePathFilter;

    public CaptureControl()
    {
        _modeCombo.Items.AddRange(["Key", "Chord", "Sequence"]);
        _modeCombo.SelectedIndex = 0;

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
        };
        row.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        row.Controls.Add(_modeCombo);
        row.Controls.Add(_startButton);
        row.Controls.Add(_stopButton);

        _resultLabel.Dock = DockStyle.Top;
        _resultLabel.Padding = new Padding(0, 8, 0, 0);

        Controls.Add(_resultLabel);
        Controls.Add(row);

        _startButton.Click += (_, _) => StartCapture();
        _stopButton.Click += (_, _) => StopCapture();
    }

    public CaptureControl(InputPipeline pipeline) : this()
    {
        Pipeline = pipeline;
    }

    public InputPipeline? Pipeline
    {
        get => _pipeline;
        set
        {
            if (_subscribed && _pipeline is not null)
                EndCapture();
            _pipeline = value;
        }
    }

    public CaptureResult? Result => _session.Result;

    public string? DevicePathFilter
    {
        get => _devicePathFilter;
        set => _devicePathFilter = value;
    }

    public void SetDevicePathFilter(string? devicePath) => DevicePathFilter = devicePath;

    public CaptureMode SelectedMode => _modeCombo.SelectedIndex switch
    {
        1 => CaptureMode.Chord,
        2 => CaptureMode.Sequence,
        _ => CaptureMode.Key,
    };

    public void SetMode(CaptureMode mode, bool locked = false)
    {
        _modeCombo.SelectedIndex = mode switch
        {
            CaptureMode.Chord => 1,
            CaptureMode.Sequence => 2,
            _ => 0,
        };
        _modeLocked = locked;
        _modeCombo.Enabled = !locked;
    }

    private void StartCapture()
    {
        if (_pipeline is null)
            throw new InvalidOperationException("Pipeline must be set before capture.");

        _session.Start(SelectedMode, _devicePathFilter);
        _pipeline.IsCaptureActive = true;
        Subscribe();
        UpdateUi();
    }

    private void StopCapture()
    {
        _session.Stop();
        EndCapture();
        UpdateUi();
    }

    private void Subscribe()
    {
        if (_pipeline is null || _subscribed)
            return;

        _pipeline.RawKeyDown += OnRawKeyDown;
        _pipeline.RawKeyUp += OnRawKeyUp;
        _subscribed = true;
    }

    private void EndCapture()
    {
        if (_pipeline is not null)
            _pipeline.IsCaptureActive = false;

        if (!_subscribed || _pipeline is null)
            return;

        _pipeline.RawKeyDown -= OnRawKeyDown;
        _pipeline.RawKeyUp -= OnRawKeyUp;
        _subscribed = false;
    }

    private void OnRawKeyDown(int vkey, string devicePath)
    {
        _session.OnRawKeyDown(vkey, devicePath);
        CheckAutoStop();
    }

    private void OnRawKeyUp(int vkey, string devicePath)
    {
        _session.OnRawKeyUp(vkey, devicePath);
        CheckAutoStop();
    }

    private void CheckAutoStop()
    {
        if (_session.IsCapturing)
            return;

        if (InvokeRequired)
            BeginInvoke(UpdateAfterCapture);
        else
            UpdateAfterCapture();
    }

    private void UpdateAfterCapture()
    {
        if (_subscribed)
        {
            EndCapture();
            UpdateUi();
        }
    }

    private void UpdateUi()
    {
        var capturing = _session.IsCapturing;
        _startButton.Enabled = !capturing;
        _stopButton.Enabled = capturing;
        _modeCombo.Enabled = !capturing && !_modeLocked;

        if (_session.Result is { } result)
        {
            var keys = string.Join(" + ", result.Keys);
            _resultLabel.Text = $"{result.Mode}: {keys}";
        }
        else if (capturing)
        {
            _resultLabel.Text = SelectedMode switch
            {
                CaptureMode.Key => "Press a key…",
                CaptureMode.Chord => "Hold modifier(s), then press the final key…",
                CaptureMode.Sequence => "Press keys in order, then Stop…",
                _ => "Capturing…",
            };
        }
        else
        {
            _resultLabel.Text = "Press Start, then input on the target device.";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            EndCapture();
        base.Dispose(disposing);
    }
}
