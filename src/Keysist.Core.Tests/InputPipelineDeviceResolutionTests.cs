using Keysist.Config;
using Keysist.Pipeline;
using Keysist.RawInputInterop;

namespace Keysist.Core.Tests;

file class FakeResolver : IRawEventResolver
{
    public string? NextDevicePath;
    public int CallCount;

    public string? TryResolveDevicePath(int vkey, bool isKeyUp)
    {
        CallCount++;
        return NextDevicePath;
    }
}

public class InputPipelineDeviceResolutionTests
{
    private static ConfigStore BuildStoreWithMappedDeviceA()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), $"keysist-tests-{Guid.NewGuid():N}.json"));
        store.Current.Devices.Add(new DeviceConfig
        {
            Name = "NumPad",
            DevicePathMatch = "HID#VID_AAAA",
            DefaultSwallow = true,
            Mappings =
            [
                new KeyMapping
                {
                    Trigger = new TriggerConfig { Mode = "key", Keys = ["A"] },
                    Action = new ActionConfig { Type = "launch", Path = "calc.exe" },
                    Swallow = true,
                }
            ],
        });
        return store;
    }

    [Fact]
    public void ShouldEatVkey_does_not_swallow_when_resolved_device_has_no_mapping()
    {
        var store = BuildStoreWithMappedDeviceA();
        var resolver = new FakeResolver { NextDevicePath = "HID#VID_BBBB&PID_0002" }; // unmapped device B
        var pipeline = new InputPipeline(store, resolver);

        var shouldEat = pipeline.TestOnlyShouldEatVkey(vkeyForKeyA: 65);

        Assert.False(shouldEat);
        Assert.Equal(1, resolver.CallCount); // resolved exactly once, synchronously — no retry loop
    }

    [Fact]
    public void ShouldEatVkey_swallows_when_resolved_device_has_matching_mapping()
    {
        var store = BuildStoreWithMappedDeviceA();
        var resolver = new FakeResolver { NextDevicePath = "HID#VID_AAAA&PID_0001" };
        var pipeline = new InputPipeline(store, resolver);

        var shouldEat = pipeline.TestOnlyShouldEatVkey(vkeyForKeyA: 65);

        Assert.True(shouldEat);
    }
}
