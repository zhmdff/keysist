namespace Keysist.Config;

/// <summary>
/// Decides whether a key should be tentatively eaten in the LL hook before Raw Input
/// identifies the device. Non-modifier trigger keys only — modifiers stay passthrough
/// so normal Ctrl/Shift/Alt typing is never delayed.
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
