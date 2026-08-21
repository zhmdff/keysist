using Keysist.Actions;
using Keysist.Config;
using Keysist.Diagnostics;
using Keysist.Hooking;
using Keysist.RawInputInterop;

namespace Keysist.Pipeline;

public class InputPipeline : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly RawInputTargetWindow _rawInputWindow = new();
    private readonly IRawEventResolver _resolver;
    private readonly LowLevelKeyboardHook _hook = new();
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

    public bool IsCaptureActive { get; set; }

    public InputPipeline(ConfigStore configStore) : this(configStore, new RawInputBufferReader())
    {
    }

    public InputPipeline(ConfigStore configStore, IRawEventResolver resolver)
    {
        _configStore = configStore;
        _resolver = resolver;
        _dispatcher = new ActionDispatcher(new WindowsProcessLauncher(), _keySender, new WindowsClipboard());
    }

    public void Start()
    {
        _uiContext = SynchronizationContext.Current;
        Logger.Log($"Pipeline.Start: uiContext={(_uiContext is null ? "null" : "ok")} devices={_configStore.Current.Devices.Count}");

        _rawInputWindow.Start();
        if (_resolver is RawInputBufferReader realReader)
            realReader.Register(_rawInputWindow.Handle);

        _hook.ShouldEatVkey = OnShouldEatVkey;
        _hook.ShouldEatVkeyUp = OnShouldEatVkeyUp;
        _hook.Install();
        Logger.Log("Pipeline.Start: hook armed (device-first resolution, no correlation wait)");
    }

    public void Stop()
    {
        Logger.Log("Pipeline.Stop");
        _hook.Uninstall();
        _hook.ShouldEatVkey = null;
        _hook.ShouldEatVkeyUp = null;
        _rawInputWindow.Stop();

        lock (_hookAteLock)
        {
            _heldSwallowedVkeys.Clear();
        }
    }

    /// <summary>
    /// The capture UI (see CapturePanel) temporarily steals the process-wide raw-input
    /// keyboard registration for its own WM_INPUT-driven listener while a capture is in
    /// progress (RegisterRawInputDevices allows only one target window per device class
    /// per process). Call this after capture ends to point registration back at this
    /// pipeline's window so GetRawInputBuffer-based device resolution resumes working.
    /// </summary>
    public void ReclaimRawInputRegistration()
    {
        if (_resolver is RawInputBufferReader realReader)
            realReader.Register(_rawInputWindow.Handle);
    }

    /// <summary>
    /// Called synchronously from the LL hook thread. Must never block: resolves the
    /// source device immediately from whatever raw input is already queued, decides
    /// swallow against ONLY that device's mappings, and — if a match fires — posts
    /// the actual action dispatch to the UI thread (dispatch itself is not time-critical).
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

        if (SwallowPlanner.IsModifierKey(keyName))
            return false;

        var startTicks = Environment.TickCount64;
        var devicePath = _resolver.TryResolveDevicePath(vkey, isKeyUp: false);
        var elapsedMs = Environment.TickCount64 - startTicks;
        if (elapsedMs > 5)
            Logger.Log($"HookLatency: OnShouldEatVkey device-resolve took {elapsedMs}ms (should be ~0)");

        if (devicePath is null)
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} result=PASS reason=noRawInputQueued");
            return false;
        }

        var pathShort = ShortPath(devicePath);
        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null)
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} path={pathShort} result=PASS reason=unknownDevice");
            return false;
        }

        _triggerMatcher.OnKeyDown(devicePath, keyName);

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

        var keyName = VirtualKeyToName(vkey);
        var devicePath = _resolver.TryResolveDevicePath(vkey, isKeyUp: true);
        if (devicePath is not null)
            _triggerMatcher.OnKeyUp(devicePath, keyName);

        return false;
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

    // Test-only seam: exercises the exact same synchronous decision path the LL hook
    // calls, without needing a real Windows hook or focus/window plumbing.
    internal bool TestOnlyShouldEatVkey(int vkeyForKeyA) => OnShouldEatVkey(vkeyForKeyA);

    public void Dispose() => Stop();
}
