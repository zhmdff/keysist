using Keysist.Capture;
using Keysist.Pipeline;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUiUserControl = Microsoft.UI.Xaml.Controls.UserControl;

namespace Keysist.Views;

public sealed partial class CapturePanel : WinUiUserControl
{
    private readonly CaptureSession _session = new();
    private InputPipeline? _pipeline;
    private bool _subscribed;
    private bool _modeLocked;
    private string? _devicePathFilter;
    private readonly DispatcherQueue _dispatcher;

    public CapturePanel()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Unloaded += (_, _) => EndCapture();
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

    internal CaptureResult? Result => _session.Result;

    public string? DevicePathFilter
    {
        get => _devicePathFilter;
        set => _devicePathFilter = value;
    }

    public CaptureMode SelectedMode => ModeCombo.SelectedIndex switch
    {
        1 => CaptureMode.Chord,
        2 => CaptureMode.Sequence,
        _ => CaptureMode.Key,
    };

    public void SetMode(CaptureMode mode, bool locked = false)
    {
        ModeCombo.SelectedIndex = mode switch
        {
            CaptureMode.Chord => 1,
            CaptureMode.Sequence => 2,
            _ => 0,
        };
        _modeLocked = locked;
        ModeCombo.IsEnabled = !locked;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => StartCapture();

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopCapture();

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_session.IsCapturing)
            return;
        UpdateUi();
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
        if (!_dispatcher.TryEnqueue(() => HandleRawKeyDown(vkey, devicePath)))
            HandleRawKeyDown(vkey, devicePath);
    }

    private void OnRawKeyUp(int vkey, string devicePath)
    {
        if (!_dispatcher.TryEnqueue(() => HandleRawKeyUp(vkey, devicePath)))
            HandleRawKeyUp(vkey, devicePath);
    }

    private void HandleRawKeyDown(int vkey, string devicePath)
    {
        _session.OnRawKeyDown(vkey, devicePath);
        CheckAutoStop();
    }

    private void HandleRawKeyUp(int vkey, string devicePath)
    {
        _session.OnRawKeyUp(vkey, devicePath);
        CheckAutoStop();
    }

    private void CheckAutoStop()
    {
        if (_session.IsCapturing)
            return;

        if (_subscribed)
        {
            EndCapture();
            UpdateUi();
        }
    }

    private void UpdateUi()
    {
        var capturing = _session.IsCapturing;
        StartButton.IsEnabled = !capturing;
        StopButton.IsEnabled = capturing;
        ModeCombo.IsEnabled = !capturing && !_modeLocked;

        if (capturing)
        {
            CaptureBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["KeysistAccentBrush"];
            CaptureBorder.BorderThickness = new Thickness(2);
        }
        else
        {
            CaptureBorder.ClearValue(Border.BorderBrushProperty);
            CaptureBorder.BorderThickness = new Thickness(1);
        }

        if (_session.Result is { } result)
        {
            var keys = string.Join(" + ", result.Keys);
            ResultText.Text = $"{result.Mode}: {keys}";
        }
        else if (capturing)
        {
            ResultText.Text = SelectedMode switch
            {
                CaptureMode.Key => "Press a key…",
                CaptureMode.Chord => "Hold modifier(s), then press the final key…",
                CaptureMode.Sequence => "Press keys in order, then Stop…",
                _ => "Capturing…",
            };
        }
        else
        {
            ResultText.Text = "Press Start, then input on the target device.";
        }
    }
}
