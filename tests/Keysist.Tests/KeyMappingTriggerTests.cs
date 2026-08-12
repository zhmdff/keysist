using Keysist.Config;
using Xunit;

public class KeyMappingTriggerTests
{
    [Fact]
    public void GetEffectiveTrigger_UsesLegacyKey_WhenTriggerMissing()
    {
        var m = new KeyMapping { Key = "NumPad1" };
        var t = m.GetEffectiveTrigger();
        Assert.Equal("key", t.Mode);
        Assert.Equal(new[] { "NumPad1" }, t.Keys);
    }

    [Fact]
    public void GetEffectiveTrigger_PrefersTrigger_OverLegacyKey()
    {
        var m = new KeyMapping
        {
            Key = "NumPad1",
            Trigger = new TriggerConfig { Mode = "chord", Keys = new List<string> { "LControlKey", "NumPad1" } }
        };
        var t = m.GetEffectiveTrigger();
        Assert.Equal("chord", t.Mode);
        Assert.Equal(new[] { "LControlKey", "NumPad1" }, t.Keys);
    }
}
