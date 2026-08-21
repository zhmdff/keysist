namespace Keysist.Config;

/// <summary>
/// Modifier-key detection used to skip modifier keys from swallow logic so normal
/// Ctrl/Shift/Alt typing is never delayed.
/// </summary>
public static class SwallowPlanner
{
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
