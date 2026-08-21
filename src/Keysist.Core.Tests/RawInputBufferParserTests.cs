using System.Runtime.InteropServices;
using Keysist.RawInputInterop;

namespace Keysist.Core.Tests;

public class RawInputBufferParserTests
{
    [Theory]
    [InlineData(40, 40)]   // already 8-aligned
    [InlineData(37, 40)]   // rounds up to next multiple of 8
    [InlineData(41, 48)]
    public void AlignedStride_rounds_up_to_pointer_size(int rawSize, int expectedStride)
    {
        Assert.Equal(expectedStride, RawInputBufferParser.AlignedStride(rawSize));
    }

    [Fact]
    public void Parse_extracts_keyboard_records_and_skips_others()
    {
        var rawinputSize = Marshal.SizeOf<RawInputNative.RAWINPUT>();
        var stride = RawInputBufferParser.AlignedStride(rawinputSize);
        var buffer = new byte[stride * 2];

        WriteKeyboardRecord(buffer, offset: 0, vkey: 65, isKeyUp: false, device: new IntPtr(0x1111));
        WriteNonKeyboardRecord(buffer, offset: stride);

        var events = RawInputBufferParser.Parse(buffer, recordCount: 2, rawinputSize, nowMs: 1000);

        var e = Assert.Single(events);
        Assert.Equal(65, e.Vkey);
        Assert.False(e.IsKeyUp);
        Assert.Equal(new IntPtr(0x1111), e.DeviceHandle);
        Assert.Equal(1000, e.TimestampMs);
    }

    private static void WriteKeyboardRecord(byte[] buffer, int offset, int vkey, bool isKeyUp, IntPtr device)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = IntPtr.Add(handle.AddrOfPinnedObject(), offset);
            var raw = new RawInputNative.RAWINPUT
            {
                Header = new RawInputNative.RAWINPUTHEADER
                {
                    Type = RawInputNative.RIM_TYPEKEYBOARD,
                    Device = device,
                },
                Keyboard = new RawInputNative.RAWKEYBOARD
                {
                    VKey = (ushort)vkey,
                    Flags = (ushort)(isKeyUp ? 1 : 0),
                },
            };
            Marshal.StructureToPtr(raw, ptr, false);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void WriteNonKeyboardRecord(byte[] buffer, int offset)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = IntPtr.Add(handle.AddrOfPinnedObject(), offset);
            var raw = new RawInputNative.RAWINPUT
            {
                Header = new RawInputNative.RAWINPUTHEADER { Type = 2 /* RIM_TYPEMOUSE */ },
            };
            Marshal.StructureToPtr(raw, ptr, false);
        }
        finally
        {
            handle.Free();
        }
    }
}
