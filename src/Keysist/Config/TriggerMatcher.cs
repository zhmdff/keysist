namespace Keysist.Config;

public sealed class TriggerMatcher
{
    private readonly long _sequenceGapMs;
    private readonly Dictionary<string, HashSet<string>> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SequenceEntry>> _sequenceBuffers = new(StringComparer.OrdinalIgnoreCase);

    public TriggerMatcher(long sequenceGapMs = 1000) => _sequenceGapMs = sequenceGapMs;

    public void OnKeyDown(string devicePath, string keyName)
    {
        GetPressed(devicePath).Add(keyName);

        var now = Environment.TickCount64;
        var buffer = GetSequenceBuffer(devicePath);
        buffer.RemoveAll(e => now - e.TimeMs > _sequenceGapMs);
        buffer.Add(new SequenceEntry(keyName, now));
    }

    public void OnKeyUp(string devicePath, string keyName)
    {
        if (_pressedKeys.TryGetValue(devicePath, out var pressed))
            pressed.Remove(keyName);
    }

    public KeyMapping? Match(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.Mappings is null)
            return null;

        foreach (var mapping in device.Mappings)
        {
            var trigger = mapping.GetEffectiveTrigger();
            if (trigger.Keys.Count == 0)
                continue;

            switch (trigger.Mode.ToLowerInvariant())
            {
                case "key":
                    if (string.Equals(trigger.Keys[0], keyNameJustPressed, StringComparison.OrdinalIgnoreCase))
                        return mapping;
                    break;

                case "chord":
                    if (AllPressed(devicePath, trigger.Keys))
                        return mapping;
                    break;

                case "sequence":
                    if (BufferEndsWith(devicePath, trigger.Keys))
                    {
                        GetSequenceBuffer(devicePath).Clear();
                        return mapping;
                    }
                    break;
            }
        }

        return null;
    }

    private bool AllPressed(string devicePath, IReadOnlyList<string> keys)
    {
        if (!_pressedKeys.TryGetValue(devicePath, out var pressed))
            return false;

        foreach (var key in keys)
        {
            if (!pressed.Contains(key))
                return false;
        }

        return true;
    }

    private bool BufferEndsWith(string devicePath, IReadOnlyList<string> keys)
    {
        if (!_sequenceBuffers.TryGetValue(devicePath, out var buffer) || buffer.Count < keys.Count)
            return false;

        for (var i = 0; i < keys.Count; i++)
        {
            if (!string.Equals(buffer[buffer.Count - keys.Count + i].Key, keys[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private HashSet<string> GetPressed(string devicePath)
    {
        if (!_pressedKeys.TryGetValue(devicePath, out var pressed))
        {
            pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _pressedKeys[devicePath] = pressed;
        }

        return pressed;
    }

    private List<SequenceEntry> GetSequenceBuffer(string devicePath)
    {
        if (!_sequenceBuffers.TryGetValue(devicePath, out var buffer))
        {
            buffer = new List<SequenceEntry>();
            _sequenceBuffers[devicePath] = buffer;
        }

        return buffer;
    }

    private sealed record SequenceEntry(string Key, long TimeMs);
}
