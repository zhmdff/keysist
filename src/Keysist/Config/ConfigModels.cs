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

    [JsonPropertyName("action")]
    public ActionConfig Action { get; set; } = new();

    [JsonPropertyName("swallow")]
    public bool? Swallow { get; set; }
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
