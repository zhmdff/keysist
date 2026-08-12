using Keysist.Config;
using Xunit;

public class SwallowPlannerTests
{
    [Fact]
    public void IsTentativeSwallowCandidate_True_ForMappedNonModifierKey()
    {
        var devices = new[]
        {
            new DeviceConfig
            {
                Name = "Pad",
                DefaultSwallow = true,
                Mappings =
                [
                    new KeyMapping
                    {
                        Trigger = new TriggerConfig { Mode = "key", Keys = ["NumPad1"] },
                        Action = new ActionConfig { Type = "sendKeys" },
                        Swallow = true,
                    }
                ]
            }
        };

        Assert.True(SwallowPlanner.IsTentativeSwallowCandidate(devices, "NumPad1"));
    }

    [Fact]
    public void IsTentativeSwallowCandidate_False_ForModifierEvenIfInChord()
    {
        var devices = new[]
        {
            new DeviceConfig
            {
                Name = "Pad",
                DefaultSwallow = true,
                Mappings =
                [
                    new KeyMapping
                    {
                        Trigger = new TriggerConfig { Mode = "chord", Keys = ["LControlKey", "NumPad1"] },
                        Action = new ActionConfig { Type = "sendKeys" },
                        Swallow = true,
                    }
                ]
            }
        };

        Assert.False(SwallowPlanner.IsTentativeSwallowCandidate(devices, "LControlKey"));
        Assert.True(SwallowPlanner.IsTentativeSwallowCandidate(devices, "NumPad1"));
    }

    [Fact]
    public void IsTentativeSwallowCandidate_False_WhenSwallowDisabled()
    {
        var devices = new[]
        {
            new DeviceConfig
            {
                Name = "Pad",
                DefaultSwallow = false,
                Mappings =
                [
                    new KeyMapping
                    {
                        Trigger = new TriggerConfig { Mode = "key", Keys = ["NumPad1"] },
                        Action = new ActionConfig { Type = "sendKeys" },
                        Swallow = false,
                    }
                ]
            }
        };

        Assert.False(SwallowPlanner.IsTentativeSwallowCandidate(devices, "NumPad1"));
    }

    [Fact]
    public void IsTentativeSwallowCandidate_False_ForUnmappedKey()
    {
        var devices = new[]
        {
            new DeviceConfig
            {
                Name = "Pad",
                DefaultSwallow = true,
                Mappings =
                [
                    new KeyMapping
                    {
                        Trigger = new TriggerConfig { Mode = "key", Keys = ["NumPad1"] },
                        Action = new ActionConfig { Type = "sendKeys" },
                    }
                ]
            }
        };

        Assert.False(SwallowPlanner.IsTentativeSwallowCandidate(devices, "NumPad2"));
    }
}
