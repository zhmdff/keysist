// src/Keysist/Pipeline/InputPipeline.cs
using Keysist.Actions;
using Keysist.Config;
using Keysist.Correlation;
using Keysist.Hooking;
using Keysist.RawInputInterop;

namespace Keysist.Pipeline;

public class InputPipeline : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly RawInputWindow _rawInputWindow = new();
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly InputCorrelator _correlator = new(new SystemClock(), ttlMs: 15);
    private readonly ActionDispatcher _dispatcher = new(new WindowsProcessLauncher(), new SendInputKeySender());

    public InputPipeline(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public void Start()
    {
        _rawInputWindow.RawKeyDown += OnRawKeyDown;
        _rawInputWindow.Start();

        _hook.ShouldEatVkey = OnShouldEatVkey;
        _hook.Install();
    }

    public void Stop()
    {
        _hook.Uninstall();
        _rawInputWindow.Stop();
    }

    private void OnRawKeyDown(int vkey, string devicePath)
    {
        _correlator.RecordRawInput(vkey, devicePath);
    }

    private bool OnShouldEatVkey(int vkey)
    {
        if (!_correlator.TryCorrelate(vkey, out var devicePath) || devicePath is null)
            return false; // no confirmed device -- never eat (spec: never break normal typing)

        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null) return false;

        var keyName = VirtualKeyToName(vkey);
        var (action, swallow) = ResolveAction(device, keyName);
        if (action is null) return false;

        // Dispatch off the hook thread -- hook callbacks must return fast.
        ThreadPool.QueueUserWorkItem(_ => _dispatcher.Dispatch(action));

        return swallow;
    }

    private static (ActionConfig? Action, bool Swallow) ResolveAction(DeviceConfig device, string keyName)
    {
        if (device.AnyKeyAction is not null)
            return (device.AnyKeyAction, device.DefaultSwallow);

        var mapping = device.Mappings?.FirstOrDefault(m => string.Equals(m.Key, keyName, StringComparison.OrdinalIgnoreCase));
        if (mapping is null) return (null, false);

        return (mapping.Action, mapping.Swallow ?? device.DefaultSwallow);
    }

    private static string VirtualKeyToName(int vkey) => ((System.Windows.Forms.Keys)vkey).ToString();

    public void Dispose() => Stop();
}
