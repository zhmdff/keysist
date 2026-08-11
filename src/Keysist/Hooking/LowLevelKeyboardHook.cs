// src/Keysist/Hooking/LowLevelKeyboardHook.cs
using System.Runtime.InteropServices;
using Keysist.Correlation;

namespace Keysist.Hooking;

public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

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

    /// <summary>Given a vkey, returns true if the key should be eaten (swallowed).</summary>
    public Func<int, bool>? ShouldEatVkey;

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
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (ShouldEatVkey?.Invoke((int)data.VkCode) == true)
            {
                return (IntPtr)1; // eat
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
