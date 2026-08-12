using System.Runtime.InteropServices;
using Keysist.Actions;
using Keysist.Diagnostics;

namespace Keysist.Hooking;

public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Given a vkey, returns true if the key-down should be eaten (swallowed).</summary>
    public Func<int, bool>? ShouldEatVkey;

    /// <summary>Given a vkey, returns true if the key-up should be eaten.</summary>
    public Func<int, bool>? ShouldEatVkeyUp;

    private IntPtr _hookHandle = IntPtr.Zero;
    private HookProc? _hookProcDelegate; // keep alive to prevent GC of the delegate

    public void Install()
    {
        _hookProcDelegate = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProcDelegate, IntPtr.Zero, 0);
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
        var isUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
        if (!isDown && !isUp)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Ignore only our own synthetic SendInput — Bluetooth remotes often arrive as
        // LLKHF_INJECTED and must still be swallowable.
        if (data.ExtraInfo == SyntheticInputMarker.Value)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var eat = isDown
            ? ShouldEatVkey?.Invoke((int)data.VkCode) == true
            : ShouldEatVkeyUp?.Invoke((int)data.VkCode) == true;

        if (eat)
        {
            var injected = (data.Flags & 0x10) != 0;
            Logger.Log(
                $"SwallowTrace: LLHook EAT vkey={data.VkCode} scan={data.ScanCode} up={!isDown} injected={injected}");
            return (IntPtr)1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
