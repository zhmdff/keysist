using System.Text.Json.Serialization;

namespace Keysist.Config;

public class KeysistConfig
{
    [JsonPropertyName("devices")]
    public List<DeviceConfig> Devices { get; set; } = new();
}

public class DeviceConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("devicePathMatch")]
    public string DevicePathMatch { get; set; } = "";

    [JsonPropertyName("defaultSwallow")]
    public bool DefaultSwallow { get; set; } = true;

    [JsonPropertyName("mappings")]
    public List<KeyMapping>? Mappings { get; set; }

    [JsonPropertyName("anyKeyAction")]
    public ActionConfig? AnyKeyAction { get; set; }
}

public class KeyMapping
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("trigger")]
    public TriggerConfig? Trigger { get; set; }

    [JsonPropertyName("action")]
    public ActionConfig Action { get; set; } = new();

    [JsonPropertyName("swallow")]
    public bool? Swallow { get; set; }

    public TriggerConfig GetEffectiveTrigger()
    {
        if (Trigger is not null && Trigger.Keys.Count > 0)
            return Trigger;
        return new TriggerConfig { Mode = "key", Keys = string.IsNullOrEmpty(Key) ? new() : new() { Key } };
    }
}

public class TriggerConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "key"; // key | chord | sequence

    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = new();
}

public class ActionConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "launch" | "sendKeys"

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("args")]
    public string? Args { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("steps")]
    public List<SendStep>? Steps { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; } // type | clipboard for pasteText
}

public class SendStep
{
    [JsonPropertyName("send")]
    public string Send { get; set; } = ""; // "vkey" | "text"

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("delayMs")]
    public int DelayMs { get; set; }
}
