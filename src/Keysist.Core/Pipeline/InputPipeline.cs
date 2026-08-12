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
    private const int CorrelateWaitMs = 200;

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
        {
            Logger.Log($"RawTrace: DOWN vkey={vkey} path=(empty) — ignored");
            return;
        }

        // Unblocks a waiting LL hook — must be first.
        _correlator.RecordRawInput(vkey, devicePath);
        SignalRawArrived(vkey);

        var keyName = VirtualKeyToName(vkey);
        var pathShort = ShortPath(devicePath);
        var hookHolding = IsHeldSwallowed(vkey);

        if (IsCaptureActive)
        {
            Logger.Log($"RawTrace: DOWN vkey={vkey} key={keyName} path={pathShort} capture=1 — remap suppressed");
            return;
        }

        _triggerMatcher.OnKeyDown(devicePath, keyName);

        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null)
        {
            Logger.Log($"RawTrace: DOWN vkey={vkey} key={keyName} path={pathShort} device=(none) — no remap");
            return;
        }

        var (action, swallow) = ResolveAction(device, devicePath, keyName);
        if (action is null)
        {
            Logger.Log(
                $"MatchTrace: device='{device.Name}' key={keyName} path={pathShort} match=none mappings={device.Mappings?.Count ?? 0}");
            return;
        }

        if (IsDebounced(devicePath, vkey))
        {
            Logger.Log(
                $"MatchTrace: device='{device.Name}' key={keyName} action={action.Type} swallow={swallow} hookHolding={hookHolding} — debounced");
            return;
        }

        Logger.Log(
            $"MatchTrace: device='{device.Name}' key={keyName} action={DescribeAction(action)} swallow={swallow} hookHolding={hookHolding} — dispatch");

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

    private void OnRawKeyUp(int vkey, string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return;

        if (IsCaptureActive)
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
        if (IsCaptureActive)
            return false;

        var keyName = VirtualKeyToName(vkey);

        // Autorepeat while this key is already held-swallowed — do not re-wait.
        // (Eating LL events can suppress Raw for repeats; that's OK — action already fired.)
        if (IsHeldSwallowed(vkey))
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} result=EAT reason=heldRepeat");
            return true;
        }

        if (!SwallowPlanner.IsTentativeSwallowCandidate(_configStore.Current.Devices, keyName))
            return false;

        Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} candidate=1 — waiting for Raw correlate");

        var wait = GetRawWaitHandle(vkey);
        wait.Reset();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (_correlator.TryCorrelate(vkey, out var devicePath) && devicePath is not null)
            {
                var pathShort = ShortPath(devicePath);
                var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
                if (device is null)
                {
                    Logger.Log(
                        $"SwallowTrace: vkey={vkey} key={keyName} path={pathShort} result=PASS reason=otherKeyboard waitMs={sw.ElapsedMilliseconds}");
                    return false;
                }

                if (!ShouldSwallow(device, devicePath, keyName))
                {
                    Logger.Log(
                        $"SwallowTrace: vkey={vkey} key={keyName} device='{device.Name}' path={pathShort} result=PASS reason=swallowOff waitMs={sw.ElapsedMilliseconds}");
                    return false;
                }

                MarkHeldSwallowed(vkey);
                Logger.Log(
                    $"SwallowTrace: vkey={vkey} key={keyName} device='{device.Name}' path={pathShort} result=EAT reason=waitCorrelate waitMs={sw.ElapsedMilliseconds}");
                return true;
            }

            var remaining = CorrelateWaitMs - (int)sw.ElapsedMilliseconds;
            if (remaining <= 0)
                break;

            wait.Wait(Math.Min(remaining, 5));
        }

        Logger.Log(
            $"SwallowTrace: vkey={vkey} key={keyName} result=PASS reason=noCorrelate waitMs={sw.ElapsedMilliseconds} pending={_correlator.PendingCount(vkey)}");
        return false;
    }

    private bool OnShouldEatVkeyUp(int vkey)
    {
        if (IsCaptureActive)
            return false;

        // Clear hold state here (not only from Raw UP) — eating KEYUP can suppress Raw UP.
        if (ClearHeldSwallowed(vkey))
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={VirtualKeyToName(vkey)} result=EAT reason=heldUp");
            return true;
        }

        return false;
    }

    private bool ShouldSwallow(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return device.DefaultSwallow;

        var mapping = _triggerMatcher.FindSwallowMapping(device, devicePath, keyNameJustPressed);
        if (mapping is null)
            return false;

        return mapping.Swallow ?? device.DefaultSwallow;
    }

    private (ActionConfig? Action, bool Swallow) ResolveAction(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return (device.AnyKeyAction, device.DefaultSwallow);

        var mapping = _triggerMatcher.Match(device, devicePath, keyNameJustPressed);
        if (mapping is null) return (null, false);

        return (mapping.Action, mapping.Swallow ?? device.DefaultSwallow);
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
