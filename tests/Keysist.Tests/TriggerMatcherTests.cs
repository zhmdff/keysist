using Keysist.Config;
using Xunit;

public class TriggerMatcherTests
{
    private const string DeviceA = @"\\?\HID#VID_046D&PID_C52B";
    private const string DeviceB = @"\\?\HID#VID_045E&PID_07B2";

    private static DeviceConfig DeviceWith(params KeyMapping[] mappings) =>
        new() { Mappings = mappings.ToList() };

    [Fact]
    public void KeyMode_MatchesSingleKey()
    {
        var matcher = new TriggerMatcher();
        var device = DeviceWith(new KeyMapping
        {
            Trigger = new TriggerConfig { Mode = "key", Keys = new List<string> { "NumPad1" } },
            Action = new ActionConfig { Type = "launch", Path = "a.exe" }
        });

        matcher.OnKeyDown(DeviceA, "NumPad1");
        var match = matcher.Match(device, DeviceA, "NumPad1");

        Assert.Same(device.Mappings![0], match);
    }

    [Fact]
    public void KeyMode_IsCaseInsensitive()
    {
        var matcher = new TriggerMatcher();
        var device = DeviceWith(new KeyMapping
        {
            Trigger = new TriggerConfig { Mode = "key", Keys = new List<string> { "NumPad1" } },
            Action = new ActionConfig { Type = "launch" }
        });

        matcher.OnKeyDown(DeviceA, "numpad1");
        var match = matcher.Match(device, DeviceA, "numpad1");

        Assert.Same(device.Mappings![0], match);
    }

    [Fact]
    public void LegacyKeyField_MatchesViaGetEffectiveTrigger()
    {
        var matcher = new TriggerMatcher();
        var device = DeviceWith(new KeyMapping
        {
            Key = "NumPad1",
            Action = new ActionConfig { Type = "launch" }
        });

        matcher.OnKeyDown(DeviceA, "NumPad1");
        var match = matcher.Match(device, DeviceA, "NumPad1");

        Assert.Same(device.Mappings![0], match);
    }

    [Fact]
    public void ChordMode_FiresWhenCompletingKeyPressed()
    {
        var matcher = new TriggerMatcher();
        var mapping = new KeyMapping
        {
            Trigger = new TriggerConfig
            {
                Mode = "chord",
                Keys = new List<string> { "LControlKey", "NumPad1" }
            },
            Action = new ActionConfig { Type = "launch" }
        };
        var device = DeviceWith(mapping);

        matcher.OnKeyDown(DeviceA, "LControlKey");
        Assert.Null(matcher.Match(device, DeviceA, "LControlKey"));

        matcher.OnKeyDown(DeviceA, "NumPad1");
        var match = matcher.Match(device, DeviceA, "NumPad1");

        Assert.Same(mapping, match);
    }

    [Fact]
    public void ChordMode_DoesNotMatchWhenKeyReleased()
    {
        var matcher = new TriggerMatcher();
        var mapping = new KeyMapping
        {
            Trigger = new TriggerConfig
            {
                Mode = "chord",
                Keys = new List<string> { "LControlKey", "NumPad1" }
            },
            Action = new ActionConfig { Type = "launch" }
        };
        var device = DeviceWith(mapping);

        matcher.OnKeyDown(DeviceA, "LControlKey");
        matcher.OnKeyUp(DeviceA, "LControlKey");
        matcher.OnKeyDown(DeviceA, "NumPad1");

        Assert.Null(matcher.Match(device, DeviceA, "NumPad1"));
    }

    [Fact]
    public void ChordMode_IsolatedPerDevice()
    {
        var matcher = new TriggerMatcher();
        var mapping = new KeyMapping
        {
            Trigger = new TriggerConfig
            {
                Mode = "chord",
                Keys = new List<string> { "LControlKey", "NumPad1" }
            },
            Action = new ActionConfig { Type = "launch" }
        };
        var device = DeviceWith(mapping);

        matcher.OnKeyDown(DeviceA, "LControlKey");
        matcher.OnKeyDown(DeviceB, "NumPad1");

        Assert.Null(matcher.Match(device, DeviceB, "NumPad1"));
    }

    [Fact]
    public void SequenceMode_MatchesOrderedSuffix()
    {
        var matcher = new TriggerMatcher();
        var mapping = new KeyMapping
        {
            Trigger = new TriggerConfig
            {
                Mode = "sequence",
                Keys = new List<string> { "NumPad7", "NumPad8" }
            },
            Action = new ActionConfig { Type = "launch" }
        };
        var device = DeviceWith(mapping);

        matcher.OnKeyDown(DeviceA, "NumPad7");
        Assert.Null(matcher.Match(device, DeviceA, "NumPad7"));

        matcher.OnKeyDown(DeviceA, "NumPad8");
        var match = matcher.Match(device, DeviceA, "NumPad8");

        Assert.Same(mapping, match);
    }

    [Fact]
    public void SequenceMode_ClearsBufferAfterMatch()
    {
        var matcher = new TriggerMatcher();
        var mapping = new KeyMapping
        {
            Trigger = new TriggerConfig
            {
                Mode = "sequence",
                Keys = new List<string> { "A", "B" }
            },
            Action = new ActionConfig { Type = "launch" }
        };
        var device = DeviceWith(mapping);

        matcher.OnKeyDown(DeviceA, "A");
        matcher.OnKeyDown(DeviceA, "B");
        Assert.Same(mapping, matcher.Match(device, DeviceA, "B"));

        Assert.Null(matcher.Match(device, DeviceA, "B"));
    }

    [Fact]
    public void SequenceMode_DropsEntriesOlderThanGap()
    {
        var matcher = new TriggerMatcher(sequenceGapMs: 50);
        var mapping = new KeyMapping
        {
            Trigger = new TriggerConfig
            {
                Mode = "sequence",
                Keys = new List<string> { "NumPad7", "NumPad8" }
            },
            Action = new ActionConfig { Type = "launch" }
        };
        var device = DeviceWith(mapping);

        matcher.OnKeyDown(DeviceA, "NumPad7");
        Thread.Sleep(80);
        matcher.OnKeyDown(DeviceA, "NumPad8");

        Assert.Null(matcher.Match(device, DeviceA, "NumPad8"));
    }

    [Fact]
    public void Match_ReturnsFirstMappingInOrder()
    {
        var matcher = new TriggerMatcher();
        var first = new KeyMapping
        {
            Trigger = new TriggerConfig { Mode = "key", Keys = new List<string> { "A" } },
            Action = new ActionConfig { Type = "launch", Path = "first" }
        };
        var second = new KeyMapping
        {
            Trigger = new TriggerConfig { Mode = "key", Keys = new List<string> { "A" } },
            Action = new ActionConfig { Type = "launch", Path = "second" }
        };
        var device = DeviceWith(first, second);

        matcher.OnKeyDown(DeviceA, "A");
        var match = matcher.Match(device, DeviceA, "A");

        Assert.Same(first, match);
    }

    [Fact]
    public void Match_IgnoresAnyKeyAction()
    {
        var matcher = new TriggerMatcher();
        var device = new DeviceConfig
        {
            AnyKeyAction = new ActionConfig { Type = "launch", Path = "any.exe" },
            Mappings = new List<KeyMapping>()
        };

        matcher.OnKeyDown(DeviceA, "X");
        Assert.Null(matcher.Match(device, DeviceA, "X"));
    }
}
