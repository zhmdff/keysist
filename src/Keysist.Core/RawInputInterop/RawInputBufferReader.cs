// src/Keysist.Core/RawInputInterop/RawInputBufferReader.cs
using System.Runtime.InteropServices;
using Keysist.Diagnostics;

namespace Keysist.RawInputInterop;

public interface IRawEventResolver
{
    string? TryResolveDevicePath(int vkey, bool isKeyUp);
}

/// <summary>
/// Resolves which HID device sent a keystroke by draining the OS raw-input queue
/// synchronously via GetRawInputBuffer — callable from the LL keyboard hook thread
/// with no waiting, no other thread, no message pump involved.
/// </summary>
public class RawInputBufferReader : IRawEventResolver
{
    private readonly int _rawinputSize = Marshal.SizeOf<RawInputNative.RAWINPUT>();
    private readonly List<RawKeyEvent> _recent = new();
    private const long RecentTtlMs = 250;

    public void Register(IntPtr targetWindowHandle)
    {
        var devices = new[]
        {
            new RawInputNative.RAWINPUTDEVICE
            {
                UsagePage = RawInputNative.HID_USAGE_PAGE_GENERIC,
                Usage = RawInputNative.HID_USAGE_GENERIC_KEYBOARD,
                Flags = RawInputNative.RIDEV_INPUTSINK,
                Target = targetWindowHandle,
            },
        };
        var registered = RawInputNative.RegisterRawInputDevices(
            devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTDEVICE>());
        Logger.Log(registered
            ? "RawInputBufferReader.Register: RegisterRawInputDevices succeeded."
            : "RawInputBufferReader.Register: RegisterRawInputDevices FAILED -- raw input will not be received.");
    }

    public string? TryResolveDevicePath(int vkey, bool isKeyUp)
    {
        Drain();

        var now = Environment.TickCount64;
        for (var i = _recent.Count - 1; i >= 0; i--)
        {
            var evt = _recent[i];
            if (now - evt.TimestampMs > RecentTtlMs)
            {
                _recent.RemoveAt(i);
                continue;
            }
            if (evt.Vkey == vkey && evt.IsKeyUp == isKeyUp)
            {
                _recent.RemoveAt(i);
                return DeviceIdentity.ResolveDevicePath(evt.DeviceHandle);
            }
        }
        return null;
    }

    private void Drain()
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>();
        var probe = RawInputNative.GetRawInputBuffer(IntPtr.Zero, ref size, headerSize);
        if (size == 0)
            return;

        // GetRawInputBuffer wants a buffer sized for several records; ask for a handful
        // up front rather than looping single records to keep this call count low.
        var stride = RawInputBufferParser.AlignedStride((int)size);
        var capacity = 16;
        var buffer = new byte[stride * capacity];
        uint bufSize = (uint)buffer.Length;

        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var count = RawInputNative.GetRawInputBuffer(pinned.AddrOfPinnedObject(), ref bufSize, headerSize);
            if (count == unchecked((uint)-1) || count == 0)
                return;

            var now = Environment.TickCount64;
            var parsed = RawInputBufferParser.Parse(buffer, (int)count, (int)size, now);
            _recent.AddRange(parsed);
        }
        finally
        {
            pinned.Free();
        }
    }
}
