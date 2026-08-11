using System.Text.Json;
using Keysist.Config;
using Xunit;

public class ConfigModelsSerializationTests
{
    [Fact]
    public void RoundTrips_DeviceWithPerKeyMappings()
    {
        var json = """
        {
          "devices": [
            {
              "name": "Numpad",
              "devicePathMatch": "HID#VID_1234&PID_5678",
              "defaultSwallow": true,
              "mappings": [
                {
                  "key": "Numpad5",
                  "action": { "type": "launch", "path": "C:\\Tools\\foo.exe", "args": "" },
                  "swallow": true
                },
                {
                  "key": "Numpad7",
                  "action": { "type": "sendKeys", "steps": [ { "send": "vkey", "value": "VK_MEDIA_PLAY_PAUSE", "delayMs": 0 } ] }
                }
              ]
            }
          ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<KeysistConfig>(json, options)!;

        Assert.Single(config.Devices);
        var device = config.Devices[0];
        Assert.Equal("Numpad", device.Name);
        Assert.Equal("HID#VID_1234&PID_5678", device.DevicePathMatch);
        Assert.True(device.DefaultSwallow);
        Assert.Equal(2, device.Mappings!.Count);
        Assert.Equal("launch", device.Mappings[0].Action.Type);
        Assert.Equal("C:\\Tools\\foo.exe", device.Mappings[0].Action.Path);
        Assert.True(device.Mappings[0].Swallow);
        Assert.Null(device.Mappings[1].Swallow);
        Assert.Equal("sendKeys", device.Mappings[1].Action.Type);
        Assert.Equal("VK_MEDIA_PLAY_PAUSE", device.Mappings[1].Action.Steps![0].Value);
    }

    [Fact]
    public void RoundTrips_DeviceWithAnyKeyAction()
    {
        var json = """
        {
          "devices": [
            {
              "name": "Remote",
              "devicePathMatch": "HID#VID_9999&PID_0001",
              "defaultSwallow": true,
              "anyKeyAction": { "type": "launch", "path": "C:\\Tools\\bar.exe" }
            }
          ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<KeysistConfig>(json, options)!;

        Assert.Null(config.Devices[0].Mappings);
        Assert.NotNull(config.Devices[0].AnyKeyAction);
        Assert.Equal("launch", config.Devices[0].AnyKeyAction!.Type);
    }
}
