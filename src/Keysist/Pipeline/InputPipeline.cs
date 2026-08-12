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
    private readonly RawInputWindow _rawInputWindow = new();
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly InputCorrelator _correlator = new(new SystemClock(), ttlMs: 15);
    private readonly ActionDispatcher _dispatcher = new(new WindowsProcessLauncher(), new SendInputKeySender(), new WindowsClipboard());
    private readonly TriggerMatcher _triggerMatcher = new();

    private readonly Dictionary<(string DevicePath, int Vkey), long> _lastDispatchedAtMs = new();
    private readonly object _lastDispatchedLock = new();
    private const long DebounceMs = 300;

    /// <summary>
    /// Pass-through subscription to the pipeline's single RawInputWindow instance, for
    /// observe-only consumers (e.g. DiscoverDeviceWindow) that must NOT create their own
    /// RawInputWindow -- RegisterRawInputDevices registration is per-process and keyed by
    /// (UsagePage, Usage), so a second registration would silently steal raw input away
    /// from this pipeline instead of adding to it.
    /// </summary>
    public event Action<int, string>? RawKeyDown
    {
        add => _rawInputWindow.RawKeyDown += value;
        remove => _rawInputWindow.RawKeyDown -= value;
    }

    public event Action<int, string>? RawKeyUp
    {
        add => _rawInputWindow.RawKeyUp += value;
        remove => _rawInputWindow.RawKeyUp -= value;
    }

    public bool IsCaptureActive { get; set; }

    public InputPipeline(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public void Start()
    {
        _rawInputWindow.RawKeyDown += OnRawKeyDown;
        _rawInputWindow.RawKeyUp += OnRawKeyUp;
        _rawInputWindow.Start();

        _hook.ShouldEatVkey = OnShouldEatVkey;
        _hook.Install();
    }

    public void Stop()
    {
        _hook.Uninstall();
        _hook.ShouldEatVkey = null;
        _rawInputWindow.RawKeyDown -= OnRawKeyDown;
        _rawInputWindow.RawKeyUp -= OnRawKeyUp;
        _rawInputWindow.Stop();
    }

    // Raw Input is device-authoritative: devicePath is known directly here, with no need
    // for hook correlation. Action dispatch happens exclusively from this path so it fires
    // reliably regardless of whether WM_INPUT or the WH_KEYBOARD_LL hook callback wins the
    // race for a given keystroke (that ordering is not guaranteed by Windows).
    private void OnRawKeyDown(int vkey, string devicePath)
    {
        _correlator.RecordRawInput(vkey, devicePath);

        var keyName = VirtualKeyToName(vkey);
        _triggerMatcher.OnKeyDown(devicePath, keyName);

        if (IsCaptureActive) return;

        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null) return;

        var (action, _) = ResolveAction(device, devicePath, keyName);
        if (action is null) return;

        if (IsDebounced(devicePath, vkey)) return;

        Logger.Log($"Dispatching action: device='{device.Name}' key='{keyName}' actionType='{action.Type}'");

        // Dispatch off the raw-input thread -- WndProc callbacks must return fast.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                _dispatcher.Dispatch(action);
            }
            catch (Exception ex)
            {
                // A single bad config entry (unknown action type, bad launch path, unknown
                // VK name, etc.) must never crash the process on a thread-pool thread.
                Logger.Log($"Dispatch failed for device='{device.Name}' key='{keyName}' actionType='{action.Type}': {ex}");
            }
        });
    }

    private void OnRawKeyUp(int vkey, string devicePath)
    {
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

    // The hook's ONLY job is to decide swallow-or-not. It requires correlation with a
    // recent Raw Input event because only the hook can eat a keystroke, and correlation is
    // a fail-safe: if the raw-input/hook ordering doesn't line up in time, default to
    // "never eat" so normal typing is never broken.
    private bool OnShouldEatVkey(int vkey)
    {
        if (IsCaptureActive) return false;

        if (!_correlator.TryCorrelate(vkey, out var devicePath) || devicePath is null)
            return false; // no confirmed device -- never eat (spec: never break normal typing)

        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null) return false;

        var keyName = VirtualKeyToName(vkey);
        var (action, swallow) = ResolveAction(device, devicePath, keyName);
        if (action is null) return false;

        return swallow;
    }

    private (ActionConfig? Action, bool Swallow) ResolveAction(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return (device.AnyKeyAction, device.DefaultSwallow);

        var mapping = _triggerMatcher.Match(device, devicePath, keyNameJustPressed);
        if (mapping is null) return (null, false);

        return (mapping.Action, mapping.Swallow ?? device.DefaultSwallow);
    }

    private static string VirtualKeyToName(int vkey) => ((System.Windows.Forms.Keys)vkey).ToString();

    public void Dispose() => Stop();
}
