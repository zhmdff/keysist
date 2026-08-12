namespace Keysist.Actions;

/// <summary>
/// ExtraInfo stamped on Keysist SendInput events so the LL hook can ignore our own
/// synthetic keys while still swallowing injected keys from Bluetooth remotes, etc.
/// </summary>
public static class SyntheticInputMarker
{
    public static readonly IntPtr Value = new(unchecked((int)0x4B455953)); // 'KEYS'
}
