// src/Keysist/RawInputInterop/DeviceIdentity.cs
using System.Runtime.InteropServices;

namespace Keysist.RawInputInterop;

public static class DeviceIdentity
{
    /// <summary>
    /// Shortens a raw device path to the stable HID#VID_…&amp;PID_… prefix used in config matching.
    /// </summary>
    public static string ShortMatch(string devicePath)
    {
        var idx = devicePath.IndexOf("&PID_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return devicePath;
        var end = devicePath.IndexOf('#', idx);
        return devicePath[..(end > 0 ? end : devicePath.Length)];
    }

    public static string ResolveDevicePath(IntPtr hDevice)
    {
        uint size = 0;
        RawInputNative.GetRawInputDeviceInfo(hDevice, RawInputNative.RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return "";

        var buffer = Marshal.AllocHGlobal((int)size * 2); // wide chars
        try
        {
            var written = RawInputNative.GetRawInputDeviceInfo(hDevice, RawInputNative.RIDI_DEVICENAME, buffer, ref size);
            if (written <= 0) return "";
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
