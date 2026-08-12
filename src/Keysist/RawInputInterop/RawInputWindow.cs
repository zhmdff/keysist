// src/Keysist/RawInputInterop/RawInputWindow.cs
using System.Runtime.InteropServices;
using Keysist.Diagnostics;
using Keysist.RawInputInterop;

namespace Keysist.RawInputInterop;

public class RawInputWindow : System.Windows.Forms.NativeWindow, IDisposable
{
    public event Action<int, string>? RawKeyDown;
    public event Action<int, string>? RawKeyUp;

    public void Start()
    {
        CreateHandle(new System.Windows.Forms.CreateParams
        {
            Caption = "KeysistRawInput",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });

        var devices = new[]
        {
            new RawInputNative.RAWINPUTDEVICE
            {
                UsagePage = RawInputNative.HID_USAGE_PAGE_GENERIC,
                Usage = RawInputNative.HID_USAGE_GENERIC_KEYBOARD,
                Flags = RawInputNative.RIDEV_INPUTSINK,
                Target = Handle
            }
        };
        var registered = RawInputNative.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTDEVICE>());
        Logger.Log(registered
            ? "RawInputWindow.Start: RegisterRawInputDevices succeeded."
            : "RawInputWindow.Start: RegisterRawInputDevices FAILED -- raw input will not be received.");
    }

    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        if (m.Msg == RawInputNative.WM_INPUT)
        {
            HandleInput(m.LParam);
        }
        base.WndProc(ref m);
    }

    private void HandleInput(IntPtr hRawInput)
    {
        uint size = 0;
        RawInputNative.GetRawInputData(hRawInput, RawInputNative.RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>());
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (RawInputNative.GetRawInputData(hRawInput, RawInputNative.RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>()) != size)
                return;

            var raw = Marshal.PtrToStructure<RawInputNative.RAWINPUT>(buffer);
            if (raw.Header.Type != RawInputNative.RIM_TYPEKEYBOARD) return;

            var devicePath = DeviceIdentity.ResolveDevicePath(raw.Header.Device);
            if ((raw.Keyboard.Flags & 1) != 0)
            {
                RawKeyUp?.Invoke(raw.Keyboard.VKey, devicePath);
                return;
            }

            RawKeyDown?.Invoke(raw.Keyboard.VKey, devicePath);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Stop() => DestroyHandle();

    public void Dispose() => Stop();
}
