using System.Diagnostics;
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
    private readonly InputCorrelator _correlator = new(new SystemClock(), ttlMs: 300);
    private readonly IKeySender _keySender = new SendInputKeySender();
    private readonly ActionDispatcher _dispatcher;
    private readonly TriggerMatcher _triggerMatcher = new();
    private SynchronizationContext? _uiContext;

    private readonly Dictionary<(string DevicePath, int Vkey), long> _lastDispatchedAtMs = new();
    private readonly object _lastDispatchedLock = new();
    private const long DebounceMs = 300;

    private readonly object _hookAteLock = new();
    /// <summary>Vkeys whose KEYDOWN was eaten for the current hold (cleared on KEYUP).</summary>
    private readonly HashSet<int> _heldSwallowedVkeys = new();
    private readonly Dictionary<int, ManualResetEventSlim> _rawArrived = new();

    /// <summary>
    /// Upper bound on how long the LL hook may block waiting to learn which device sent a
    /// keystroke. Measured empirically: an idle raw-input pump thread can take 30-60ms+ to
    /// get rescheduled and deliver WM_INPUT for a deliberate, unhurried keypress (confirmed
    /// via keysist.log — isolated presses reliably missed a 25ms window; rapid repeats, which
    /// keep the thread warm, did not). 80ms is comfortably under the ~300ms default
    /// LowLevelHooksTimeout Windows uses before silently unhooking a slow LL hook, and is
    /// only ever paid for key NAMES that are actually mapped somewhere (see
    /// SwallowPlanner.IsTentativeSwallowCandidate) — not for every keystroke system-wide.
    /// </summary>
    private const int CorrelateWaitMs = 80;

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
        Logger.Log($"Pipeline.Start: uiContext={(_uiContext is null ? "null" : "ok")} devices={_configStore.Current.Devices.Count}");

        _rawInput.RawKeyDown += OnRawKeyDown;
        _rawInput.RawKeyUp += OnRawKeyUp;
        _rawInput.Start();

        _hook.ShouldEatVkey = OnShouldEatVkey;
        _hook.ShouldEatVkeyUp = OnShouldEatVkeyUp;
        _hook.Install();
        Logger.Log("Pipeline.Start: raw+hook armed");
    }

    public void Stop()
    {
        Logger.Log("Pipeline.Stop");
        _hook.Uninstall();
        _hook.ShouldEatVkey = null;
        _hook.ShouldEatVkeyUp = null;
        _rawInput.RawKeyDown -= OnRawKeyDown;
        _rawInput.RawKeyUp -= OnRawKeyUp;
        _rawInput.Stop();

        lock (_hookAteLock)
        {
            foreach (var wait in _rawArrived.Values)
                wait.Dispose();
            _rawArrived.Clear();
            _heldSwallowedVkeys.Clear();
        }
    }

    private void OnRawKeyDown(int vkey, string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return;

        // Unblocks a waiting LL hook — must be first.
        _correlator.RecordRawInput(vkey, devicePath);
        SignalRawArrived(vkey);

        if (IsCaptureActive)
            return;

        _triggerMatcher.OnKeyDown(devicePath, VirtualKeyToName(vkey));
    }

    private void OnRawKeyUp(int vkey, string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return;

        _correlator.RecordRawInput(vkey, devicePath);
        SignalRawArrived(vkey);

        if (IsCaptureActive)
            return;

        _triggerMatcher.OnKeyUp(devicePath, VirtualKeyToName(vkey));
    }

    /// <summary>
    /// Called synchronously from the LL hook thread. For key NAMES that are mapped on at
    /// least one device (see SwallowPlanner.IsTentativeSwallowCandidate — this is a coarse,
    /// device-agnostic filter, since the device isn't known yet), waits briefly (bounded by
    /// <see cref="CorrelateWaitMs"/>) for Raw Input to identify the actual source device,
    /// then matches ONLY that device's mappings — never any other device's. Keys that don't
    /// match any mapping's name never wait at all, so ordinary typing pays nothing.
    /// </summary>
    private bool OnShouldEatVkey(int vkey)
    {
        if (IsCaptureActive)
            return false;

        var keyName = VirtualKeyToName(vkey);

        if (IsHeldSwallowed(vkey))
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} result=EAT reason=heldRepeat");
            return true;
        }

        if (!SwallowPlanner.IsTentativeSwallowCandidate(_configStore.Current.Devices, keyName))
            return false;

        var devicePath = CorrelateDevice(vkey);
        if (devicePath is null)
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} result=PASS reason=noCorrelate");
            return false;
        }

        var pathShort = ShortPath(devicePath);
        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null)
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} path={pathShort} result=PASS reason=unknownDevice");
            return false;
        }

        var (action, swallow) = ResolveAction(device, devicePath, keyName);
        if (action is null)
        {
            Logger.Log($"MatchTrace: device='{device.Name}' key={keyName} path={pathShort} match=none");
            return false;
        }

        if (IsDebounced(devicePath, vkey))
        {
            Logger.Log($"MatchTrace: device='{device.Name}' key={keyName} action={action.Type} — debounced");
            return swallow;
        }

        Logger.Log($"MatchTrace: device='{device.Name}' key={keyName} action={DescribeAction(action)} swallow={swallow} — dispatch");

        if (swallow)
            MarkHeldSwallowed(vkey);

        DispatchAsync(device, keyName, action);
        return swallow;
    }

    private bool OnShouldEatVkeyUp(int vkey)
    {
        if (IsCaptureActive)
            return false;

        if (ClearHeldSwallowed(vkey))
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={VirtualKeyToName(vkey)} result=EAT reason=heldUp");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Blocks the calling (LL hook) thread for at most <see cref="CorrelateWaitMs"/>
    /// waiting for Raw Input to report which device sent <paramref name="vkey"/>.
    /// </summary>
    private string? CorrelateDevice(int vkey)
    {
        if (_correlator.TryCorrelate(vkey, out var immediate) && immediate is not null)
            return immediate;

        var wait = GetRawWaitHandle(vkey);
        wait.Reset();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (_correlator.TryCorrelate(vkey, out var devicePath) && devicePath is not null)
                return devicePath;

            var remaining = CorrelateWaitMs - (int)sw.ElapsedMilliseconds;
            if (remaining <= 0)
                return null;

            wait.Wait(remaining);
        }
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

    private (ActionConfig? Action, bool Swallow) ResolveAction(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return (device.AnyKeyAction, device.DefaultSwallow);

        var mapping = _triggerMatcher.Match(device, devicePath, keyNameJustPressed);
        if (mapping is null) return (null, false);

        return (mapping.Action, mapping.Swallow ?? device.DefaultSwallow);
    }

    private void DispatchAsync(DeviceConfig device, string keyName, ActionConfig action)
    {
        void Dispatch()
        {
            try
            {
                Logger.Log($"DispatchTrace: begin device='{device.Name}' key={keyName} action={DescribeAction(action)}");
                _dispatcher.Dispatch(action);
                Logger.Log($"DispatchTrace: ok device='{device.Name}' key={keyName}");
            }
            catch (Exception ex)
            {
                Logger.Log($"DispatchTrace: FAIL device='{device.Name}' key={keyName}: {ex}");
            }
        }

        if (_uiContext is not null)
            _uiContext.Post(_ => Dispatch(), null);
        else
            ThreadPool.QueueUserWorkItem(_ => Dispatch());
    }

    private void MarkHeldSwallowed(int vkey)
    {
        lock (_hookAteLock)
            _heldSwallowedVkeys.Add(vkey);
    }

    private bool IsHeldSwallowed(int vkey)
    {
        lock (_hookAteLock)
            return _heldSwallowedVkeys.Contains(vkey);
    }

    private bool ClearHeldSwallowed(int vkey)
    {
        lock (_hookAteLock)
            return _heldSwallowedVkeys.Remove(vkey);
    }

    private void SignalRawArrived(int vkey)
    {
        lock (_hookAteLock)
        {
            if (_rawArrived.TryGetValue(vkey, out var wait))
                wait.Set();
        }
    }

    private ManualResetEventSlim GetRawWaitHandle(int vkey)
    {
        lock (_hookAteLock)
        {
            if (!_rawArrived.TryGetValue(vkey, out var wait))
            {
                wait = new ManualResetEventSlim(false);
                _rawArrived[vkey] = wait;
            }

            return wait;
        }
    }

    private static string VirtualKeyToName(int vkey) => ((System.Windows.Forms.Keys)vkey).ToString();

    private static string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "(empty)";

        var hid = path.IndexOf("HID#", StringComparison.OrdinalIgnoreCase);
        if (hid >= 0)
        {
            var slice = path[hid..];
            var end = slice.IndexOf('#', 4);
            if (end < 0) end = Math.Min(slice.Length, 48);
            return slice[..end];
        }

        return path.Length <= 48 ? path : path[^48..];
    }

    private static string DescribeAction(ActionConfig action) =>
        action.Type?.ToLowerInvariant() switch
        {
            "pastetext" => $"pasteText(len={action.Text?.Length ?? 0},method={action.Method})",
            "sendkeys" => $"sendKeys(steps={action.Steps?.Count ?? 0})",
            "launch" => $"launch({action.Path})",
            _ => action.Type ?? "(null)",
        };

    public void Dispose() => Stop();
}
