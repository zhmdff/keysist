// src/Keysist.Core/RawInputInterop/RawInputBufferParser.cs
using System.Runtime.InteropServices;

namespace Keysist.RawInputInterop;

public readonly record struct RawKeyEvent(int Vkey, bool IsKeyUp, IntPtr DeviceHandle, long TimestampMs);

public static class RawInputBufferParser
{
    public static int AlignedStride(int rawinputSize)
    {
        var ptrSize = IntPtr.Size;
        return (rawinputSize + ptrSize - 1) / ptrSize * ptrSize;
    }

    public static List<RawKeyEvent> Parse(byte[] buffer, int recordCount, int rawinputSize, long nowMs)
    {
        var results = new List<RawKeyEvent>(recordCount);
        var stride = AlignedStride(rawinputSize);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            for (var i = 0; i < recordCount; i++)
            {
                var recordPtr = IntPtr.Add(basePtr, i * stride);
                var raw = Marshal.PtrToStructure<RawInputNative.RAWINPUT>(recordPtr);
                if (raw.Header.Type != RawInputNative.RIM_TYPEKEYBOARD)
                    continue;

                var isKeyUp = (raw.Keyboard.Flags & 1) != 0;
                results.Add(new RawKeyEvent(raw.Keyboard.VKey, isKeyUp, raw.Header.Device, nowMs));
            }
        }
        finally
        {
            handle.Free();
        }
        return results;
    }
}
