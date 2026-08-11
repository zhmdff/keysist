using Keysist.Correlation;
using Xunit;

public class InputCorrelatorTests
{
    [Fact]
    public void TryCorrelate_ReturnsTrueAndDevicePath_WhenRawInputPrecededHookWithinTtl()
    {
        var clock = new FakeClock { Now = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 15);

        correlator.RecordRawInput(vkey: 0x35 /* '5' */, devicePath: @"\\?\HID#VID_1234");
        clock.Now = 1005;

        var matched = correlator.TryCorrelate(0x35, out var devicePath);

        Assert.True(matched);
        Assert.Equal(@"\\?\HID#VID_1234", devicePath);
    }

    [Fact]
    public void TryCorrelate_ReturnsFalse_WhenNoRawInputRecorded()
    {
        var clock = new FakeClock { Now = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 15);

        var matched = correlator.TryCorrelate(0x35, out var devicePath);

        Assert.False(matched);
        Assert.Null(devicePath);
    }

    [Fact]
    public void TryCorrelate_ReturnsFalse_WhenEntryExpired()
    {
        var clock = new FakeClock { Now = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 15);

        correlator.RecordRawInput(0x35, @"\\?\HID#VID_1234");
        clock.Now = 1020; // past 15ms TTL

        var matched = correlator.TryCorrelate(0x35, out var devicePath);

        Assert.False(matched);
        Assert.Null(devicePath);
    }

    [Fact]
    public void TryCorrelate_ConsumesEntry_SoASecondCallForSameVkeyFails()
    {
        var clock = new FakeClock { Now = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 15);

        correlator.RecordRawInput(0x35, @"\\?\HID#VID_1234");
        correlator.TryCorrelate(0x35, out _);
        var second = correlator.TryCorrelate(0x35, out var devicePath);

        Assert.False(second);
        Assert.Null(devicePath);
    }

    [Fact]
    public void TryCorrelate_DoesNotConfuseDifferentVkeys()
    {
        var clock = new FakeClock { Now = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 15);

        correlator.RecordRawInput(0x35, @"\\?\HID#VID_1234"); // '5'
        var matched = correlator.TryCorrelate(0x37, out var devicePath); // '7'

        Assert.False(matched);
        Assert.Null(devicePath);
    }
}
