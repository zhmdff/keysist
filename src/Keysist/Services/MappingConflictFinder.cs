using Keysist.Capture;
using Keysist.Config;
using Keysist.Services;

namespace Keysist.Services;

public sealed class MappingConflict
{
    public required DeviceConfig Device { get; init; }
    public required KeyMapping Mapping { get; init; }

    public string Summary =>
        $"{Device.Name}: {MappingSummaryHelper.FormatTriggerSummary(Mapping)} → {MappingSummaryHelper.FormatActionSummary(Mapping.Action)}";
}

public static class MappingConflictFinder
{
    public static MappingConflict? Find(
        IReadOnlyList<DeviceConfig> devices,
        CaptureResult result,
        KeyMapping? except = null)
    {
        var device = DeviceMatcher.FindMatch(devices, result.DevicePath);
        if (device is null)
            return null;

        foreach (var mapping in device.Mappings ?? [])
        {
            if (except is not null && ReferenceEquals(mapping, except))
                continue;

            if (TriggersOverlap(mapping.GetEffectiveTrigger(), result))
            {
                return new MappingConflict
                {
                    Device = device,
                    Mapping = mapping,
                };
            }
        }

        return null;
    }

    private static bool TriggersOverlap(TriggerConfig existing, CaptureResult incoming)
    {
        var mode = ActionConfigBuilder.TriggerModeFromCapture(incoming.Mode);
        if (!string.Equals(existing.Mode, mode, StringComparison.OrdinalIgnoreCase))
            return false;

        if (existing.Keys.Count != incoming.Keys.Count)
            return false;

        for (var i = 0; i < existing.Keys.Count; i++)
        {
            if (!string.Equals(existing.Keys[i], incoming.Keys[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
