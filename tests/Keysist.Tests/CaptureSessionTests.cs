using System.Windows.Forms;
using Keysist.Capture;
using Xunit;

public class CaptureSessionTests
{
    private const string DeviceA = @"\\?\HID#VID_046D&PID_C52B";
    private const string DeviceB = @"\\?\HID#VID_045E&PID_07B2";

    private static string KeyName(Keys key) => key.ToString();

    [Fact]
    public void EventsBeforeStart_AreIgnored()
    {
        var session = new CaptureSession();
        session.OnRawKeyDown((int)Keys.A, DeviceA);
        session.OnRawKeyUp((int)Keys.A, DeviceA);

        Assert.False(session.IsCapturing);
        Assert.Null(session.Result);
    }

    [Fact]
    public void EventsAfterStop_DoNotOverwriteResult()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Key);
        session.OnRawKeyDown((int)Keys.B, DeviceA);
        var first = session.Result;

        session.OnRawKeyDown((int)Keys.C, DeviceA);

        Assert.Same(first, session.Result);
        Assert.Equal(KeyName(Keys.B), session.Result!.Keys[0]);
        Assert.False(session.IsCapturing);
    }

    [Fact]
    public void KeyMode_FirstKeyDown_AutoStopsWithResult()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Key);

        session.OnRawKeyDown((int)Keys.NumPad1, DeviceA);

        Assert.False(session.IsCapturing);
        Assert.NotNull(session.Result);
        Assert.Equal(CaptureMode.Key, session.Result!.Mode);
        Assert.Equal(DeviceA, session.Result.DevicePath);
        Assert.Equal(new[] { KeyName(Keys.NumPad1) }, session.Result.Keys);
    }

    [Fact]
    public void DeviceFilter_IgnoresOtherDevices()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Key, DeviceA);

        session.OnRawKeyDown((int)Keys.X, DeviceB);
        Assert.True(session.IsCapturing);
        Assert.Null(session.Result);

        session.OnRawKeyDown((int)Keys.Y, DeviceA);

        Assert.False(session.IsCapturing);
        Assert.Equal(DeviceA, session.Result!.DevicePath);
        Assert.Equal(new[] { KeyName(Keys.Y) }, session.Result.Keys);
    }

    [Fact]
    public void NoDeviceFilter_PinsDeviceOnFirstKey()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Sequence);

        session.OnRawKeyDown((int)Keys.A, DeviceA);
        session.OnRawKeyDown((int)Keys.B, DeviceB);
        session.Stop();

        Assert.NotNull(session.Result);
        Assert.Equal(DeviceA, session.Result!.DevicePath);
        Assert.Equal(new[] { KeyName(Keys.A) }, session.Result.Keys);
    }

    [Fact]
    public void SequenceMode_AppendsKeyDownsUntilStop()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Sequence, DeviceA);

        session.OnRawKeyDown((int)Keys.LControlKey, DeviceA);
        session.OnRawKeyDown((int)Keys.C, DeviceA);
        session.OnRawKeyUp((int)Keys.LControlKey, DeviceA);
        session.OnRawKeyDown((int)Keys.V, DeviceA);
        session.Stop();

        Assert.False(session.IsCapturing);
        Assert.Equal(
            new[] { KeyName(Keys.LControlKey), KeyName(Keys.C), KeyName(Keys.V) },
            session.Result!.Keys);
        Assert.Equal(CaptureMode.Sequence, session.Result.Mode);
    }

    [Fact]
    public void ChordMode_AutoStopsWhenSecondKeyHeld()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Chord, DeviceA);

        session.OnRawKeyDown((int)Keys.LControlKey, DeviceA);
        Assert.True(session.IsCapturing);
        Assert.Null(session.Result);

        session.OnRawKeyDown((int)Keys.C, DeviceA);

        Assert.False(session.IsCapturing);
        Assert.Equal(
            new[] { KeyName(Keys.LControlKey), KeyName(Keys.C) },
            session.Result!.Keys);
        Assert.Equal(CaptureMode.Chord, session.Result.Mode);
    }

    [Fact]
    public void ChordMode_StopWithHeldKeys_UsesHeldSetInPressOrder()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Chord, DeviceA);

        session.OnRawKeyDown((int)Keys.LShiftKey, DeviceA);
        session.OnRawKeyDown((int)Keys.LControlKey, DeviceA);
        session.Stop();

        Assert.False(session.IsCapturing);
        Assert.Equal(
            new[] { KeyName(Keys.LShiftKey), KeyName(Keys.LControlKey) },
            session.Result!.Keys);
    }

    [Fact]
    public void ChordMode_KeyUp_RemovesFromHeldBeforeAutoStop()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Chord, DeviceA);

        session.OnRawKeyDown((int)Keys.LControlKey, DeviceA);
        session.OnRawKeyUp((int)Keys.LControlKey, DeviceA);
        session.OnRawKeyDown((int)Keys.C, DeviceA);

        Assert.True(session.IsCapturing);
        Assert.Null(session.Result);
    }

    [Fact]
    public void ChordMode_IgnoresDuplicateKeyDown()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Chord, DeviceA);

        session.OnRawKeyDown((int)Keys.LControlKey, DeviceA);
        session.OnRawKeyDown((int)Keys.LControlKey, DeviceA);

        Assert.True(session.IsCapturing);
        Assert.Null(session.Result);
    }

    [Fact]
    public void Stop_WithNoKeys_LeavesResultNull()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Sequence, DeviceA);
        session.Stop();

        Assert.False(session.IsCapturing);
        Assert.Null(session.Result);
    }

    [Fact]
    public void Start_ResetsPreviousCapture()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Key, DeviceA);
        session.OnRawKeyDown((int)Keys.A, DeviceA);

        session.Start(CaptureMode.Sequence, DeviceA);
        Assert.True(session.IsCapturing);
        Assert.Null(session.Result);

        session.OnRawKeyDown((int)Keys.B, DeviceA);
        session.Stop();

        Assert.Equal(new[] { KeyName(Keys.B) }, session.Result!.Keys);
        Assert.Equal(CaptureMode.Sequence, session.Result.Mode);
    }

    [Fact]
    public void KeyMode_IgnoresKeyUp()
    {
        var session = new CaptureSession();
        session.Start(CaptureMode.Key, DeviceA);
        session.OnRawKeyUp((int)Keys.Z, DeviceA);

        Assert.True(session.IsCapturing);
        Assert.Null(session.Result);
    }
}
