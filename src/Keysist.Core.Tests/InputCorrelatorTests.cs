using Keysist.Correlation;

namespace Keysist.Core.Tests;

file class FakeClock : IClock
{
    public long NowMsValue;
    public long NowMs() => NowMsValue;
}

public class InputCorrelatorTests
{
    [Fact]
    public void TryCorrelate_returns_false_when_nothing_recorded()
    {
        var correlator = new InputCorrelator(new FakeClock(), ttlMs: 300);
        Assert.False(correlator.TryCorrelate(vkey: 65, out _));
    }

    [Fact]
    public void TryCorrelate_returns_the_device_that_was_recorded_for_that_vkey()
    {
        var clock = new FakeClock { NowMsValue = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 300);

        correlator.RecordRawInput(vkey: 65, devicePath: "HID#DeviceA");

        Assert.True(correlator.TryCorrelate(65, out var devicePath));
        Assert.Equal("HID#DeviceA", devicePath);
    }

    [Fact]
    public void TryCorrelate_does_not_leak_a_device_recorded_for_a_different_vkey()
    {
        var clock = new FakeClock { NowMsValue = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 300);

        correlator.RecordRawInput(vkey: 65, devicePath: "HID#DeviceA");

        Assert.False(correlator.TryCorrelate(vkey: 66, out var devicePath));
        Assert.Null(devicePath);
    }

    [Fact]
    public void TryCorrelate_ignores_entries_older_than_ttl()
    {
        var clock = new FakeClock { NowMsValue = 1000 };
        var correlator = new InputCorrelator(clock, ttlMs: 300);

        correlator.RecordRawInput(vkey: 65, devicePath: "HID#DeviceA");
        clock.NowMsValue = 1301; // 301ms later, past the 300ms TTL

        Assert.False(correlator.TryCorrelate(65, out _));
    }
}
