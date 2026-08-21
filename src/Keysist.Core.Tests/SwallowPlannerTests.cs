using Keysist.Config;

namespace Keysist.Core.Tests;

public class SwallowPlannerTests
{
    private static DeviceConfig MappedDevice(string keyName, bool swallow = true) => new()
    {
        Name = "Numpad",
        DevicePathMatch = "HID#VID_AAAA",
        DefaultSwallow = true,
        Mappings =
        [
            new KeyMapping
            {
                Trigger = new TriggerConfig { Mode = "key", Keys = [keyName] },
                Action = new ActionConfig { Type = "launch", Path = "calc.exe" },
                Swallow = swallow,
            },
        ],
    };

    [Fact]
    public void IsTentativeSwallowCandidate_true_for_key_name_mapped_on_some_device()
    {
        var devices = new[] { MappedDevice("NumPad1") };
        Assert.True(SwallowPlanner.IsTentativeSwallowCandidate(devices, "NumPad1"));
    }

    [Fact]
    public void IsTentativeSwallowCandidate_false_for_key_name_mapped_nowhere()
    {
        var devices = new[] { MappedDevice("NumPad1") };
        Assert.False(SwallowPlanner.IsTentativeSwallowCandidate(devices, "A"));
    }

    [Fact]
    public void IsTentativeSwallowCandidate_false_for_modifier_keys_even_if_mapped()
    {
        var devices = new[] { MappedDevice("ControlKey") };
        Assert.False(SwallowPlanner.IsTentativeSwallowCandidate(devices, "ControlKey"));
    }

    [Fact]
    public void IsTentativeSwallowCandidate_false_when_mapping_has_swallow_disabled()
    {
        var devices = new[] { MappedDevice("NumPad1", swallow: false) };
        Assert.False(SwallowPlanner.IsTentativeSwallowCandidate(devices, "NumPad1"));
    }
}
