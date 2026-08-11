using Keysist.Config;
using Xunit;

public class DeviceMatcherTests
{
    private static DeviceConfig Device(string match) => new() { DevicePathMatch = match };

    [Fact]
    public void FindMatch_ReturnsDevice_WhenSubstringPresent_CaseInsensitive()
    {
        var devices = new[] { Device("HID#VID_1234&PID_5678") };
        var actual = @"\\?\HID#VID_1234&PID_5678&MI_00#7&abc#0000#{...}";

        var result = DeviceMatcher.FindMatch(devices, actual);

        Assert.Same(devices[0], result);
    }

    [Fact]
    public void FindMatch_ReturnsNull_WhenNoDeviceMatches()
    {
        var devices = new[] { Device("HID#VID_1234&PID_5678") };
        var actual = @"\\?\HID#VID_9999&PID_0001&MI_00#7&abc#0000#{...}";

        Assert.Null(DeviceMatcher.FindMatch(devices, actual));
    }

    [Fact]
    public void FindMatch_ReturnsFirstMatch_WhenMultipleConfigured()
    {
        var devices = new[] { Device("VID_1234"), Device("VID_9999") };
        var actual = @"\\?\HID#VID_9999&PID_0001#...";

        var result = DeviceMatcher.FindMatch(devices, actual);

        Assert.Same(devices[1], result);
    }
}
