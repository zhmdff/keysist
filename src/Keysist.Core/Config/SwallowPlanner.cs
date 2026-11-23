namespace Keysist.Config;

/// <summary>
/// Decides whether a key is even worth the correlate-and-wait in OnShouldEatVkey. Gating on
/// key NAME across all devices (not the specific device -- that isn't known yet) is a
/// coarse filter: it can't tell "mapped on device A, pressed on device B" from "mapped on
/// device A, pressed on device A" ahead of time, so that case still pays the wait. What it
/// does buy is that ordinary typing of key names nobody has mapped anywhere never waits at
/// all -- without this, EVERY keystroke system-wide pays the correlate wait, which is its
/// own regression (see Keysist.Pipeline.InputPipeline's CorrelateWaitMs doc comment).
/// </summary>
public static class SwallowPlanner
{
    public static bool IsTentativeSwallowCandidate(IEnumerable<DeviceConfig> devices, string keyName)
    {
        if (IsModifierKey(keyName))
            return false;

        foreach (var device in devices)
        {
            if (device.AnyKeyAction is not null && device.DefaultSwallow)
                return true;

            foreach (var mapping in device.Mappings ?? [])
            {
                // Non-swallow mappings are dispatched from the raw-input path, not the hook.
                if (!(mapping.Swallow ?? device.DefaultSwallow))
                    continue;

                var trigger = mapping.GetEffectiveTrigger();
                foreach (var key in trigger.Keys)
                {
                    if (string.Equals(key, keyName, StringComparison.OrdinalIgnoreCase)
                        && !IsModifierKey(key))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool IsModifierKey(string keyName) =>
        keyName.Equals("ControlKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LControlKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RControlKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("ShiftKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LShiftKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RShiftKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("Menu", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LMenu", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RMenu", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LWin", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RWin", StringComparison.OrdinalIgnoreCase);
}
