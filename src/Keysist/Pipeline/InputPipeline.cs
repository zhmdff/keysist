// src/Keysist/Pipeline/InputPipeline.cs
using Keysist.Actions;
using Keysist.Config;
using Keysist.Correlation;
using Keysist.Diagnostics;
using Keysist.Hooking;
using Keysist.RawInputInterop;

namespace Keysist.Pipeline;

public class InputPipeline : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly RawInputPump _rawInput = new();
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly InputCorrelator _correlator = new(new SystemClock(), ttlMs: 80);
    private readonly IKeySender _keySender = new SendInputKeySender();
    private readonly ActionDispatcher _dispatcher;
    private readonly TriggerMatcher _triggerMatcher = new();
    private SynchronizationContext? _uiContext;

    private readonly Dictionary<(string DevicePath, int Vkey), long> _lastDispatchedAtMs = new();
    private readonly object _lastDispatchedLock = new();
    private const long DebounceMs = 300;

    /// <summary>
    /// Pass-through subscription to the pipeline's single Raw Input pump, for
    /// observe-only consumers (e.g. CaptureControl) that must NOT create their own
    /// RawInputWindow -- RegisterRawInputDevices registration is per-process and keyed by
    /// (UsagePage, Usage), so a second registration would silently steal raw input away
    /// from this pipeline instead of adding to it.
    /// </summary>
    public event Action<int, string>? RawKeyDown
    {
        add => _rawInput.RawKeyDown += value;
        remove => _rawInput.RawKeyDown -= value;
    }

    public event Action<int, string>? RawKeyUp
    {
        add => _rawInput.RawKeyUp += value;
        remove => _rawInput.RawKeyUp -= value;
    }

    public bool IsCaptureActive { get; set; }

    public InputPipeline(ConfigStore configStore)
    {
        _configStore = configStore;
        _dispatcher = new ActionDispatcher(new WindowsProcessLauncher(), _keySender, new WindowsClipboard());
    }

    public void Start()
    {
        _uiContext = SynchronizationContext.Current;

        _rawInput.RawKeyDown += OnRawKeyDown;
        _rawInput.RawKeyUp += OnRawKeyUp;
        _rawInput.Start();

        _hook.ShouldEatVkey = OnShouldEatVkey;
        _hook.Install();
    }

    public void Stop()
    {
        _hook.Uninstall();
        _hook.ShouldEatVkey = null;
        _rawInput.RawKeyDown -= OnRawKeyDown;
        _rawInput.RawKeyUp -= OnRawKeyUp;
        _rawInput.Stop();
    }

    private void OnRawKeyDown(int vkey, string devicePath)
    {
        // Injected keys (our own SendInput) often have an empty device path — ignore.
        if (string.IsNullOrEmpty(devicePath))
            return;

        _correlator.RecordRawInput(vkey, devicePath);

        var keyName = VirtualKeyToName(vkey);
        _triggerMatcher.OnKeyDown(devicePath, keyName);

        if (IsCaptureActive)
            return;

        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null) return;

        var (action, _) = ResolveAction(device, devicePath, keyName);
        if (action is null) return;

        if (IsDebounced(devicePath, vkey)) return;

        Logger.Log($"Dispatching action: device='{device.Name}' key='{keyName}' actionType='{action.Type}'");

        // Prefer UI sync context so SendInput/clipboard land in the focused app reliably.
        void Dispatch()
        {
            try
            {
                _dispatcher.Dispatch(action);
            }
            catch (Exception ex)
            {
                Logger.Log($"Dispatch failed for device='{device.Name}' key='{keyName}' actionType='{action.Type}': {ex}");
            }
        }

        if (_uiContext is not null)
            _uiContext.Post(_ => Dispatch(), null);
        else
            ThreadPool.QueueUserWorkItem(_ => Dispatch());
    }

    private void OnRawKeyUp(int vkey, string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return;

        var keyName = VirtualKeyToName(vkey);
        _triggerMatcher.OnKeyUp(devicePath, keyName);
    }

    private bool IsDebounced(string devicePath, int vkey)
    {
        var now = Environment.TickCount64;
        var key = (devicePath, vkey);
        lock (_lastDispatchedLock)
        {
            if (_lastDispatchedAtMs.TryGetValue(key, out var lastMs) && now - lastMs < DebounceMs)
                return true;

            _lastDispatchedAtMs[key] = now;
            return false;
        }
    }

    private bool OnShouldEatVkey(int vkey)
    {
        if (IsCaptureActive) return false;

        // Raw Input runs on a dedicated thread, so correlation often succeeds here.
        if (!_correlator.TryCorrelate(vkey, out var devicePath) || devicePath is null)
            return false;

        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null) return false;

        var keyName = VirtualKeyToName(vkey);
        return ResolveSwallow(device, devicePath, keyName);
    }

    private (ActionConfig? Action, bool Swallow) ResolveAction(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return (device.AnyKeyAction, device.DefaultSwallow);

        var mapping = _triggerMatcher.Match(device, devicePath, keyNameJustPressed);
        if (mapping is null) return (null, false);

        return (mapping.Action, mapping.Swallow ?? device.DefaultSwallow);
    }

    private bool ResolveSwallow(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return device.DefaultSwallow;

        if (!_triggerMatcher.ShouldSwallow(device, devicePath, keyNameJustPressed))
            return false;

        var mapping = _triggerMatcher.FindSwallowMapping(device, devicePath, keyNameJustPressed);
        return mapping?.Swallow ?? device.DefaultSwallow;
    }

    private static string VirtualKeyToName(int vkey) => ((System.Windows.Forms.Keys)vkey).ToString();

    public void Dispose() => Stop();
}
