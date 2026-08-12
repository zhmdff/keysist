namespace Keysist.Config;

public static class DeviceMatcher
{
    public static DeviceConfig? FindMatch(IEnumerable<DeviceConfig> devices, string actualDevicePath)
    {
        foreach (var device in devices)
        {
            if (!string.IsNullOrEmpty(device.DevicePathMatch) &&
                actualDevicePath.Contains(device.DevicePathMatch, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }
        return null;
    }
}
