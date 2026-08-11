// src/Keysist/Actions/VirtualKeyNames.cs
namespace Keysist.Actions;

public static class VirtualKeyNames
{
    private static readonly Dictionary<string, ushort> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VK_MEDIA_PLAY_PAUSE"] = 0xB3,
        ["VK_MEDIA_NEXT_TRACK"] = 0xB0,
        ["VK_MEDIA_PREV_TRACK"] = 0xB1,
        ["VK_VOLUME_UP"] = 0xAF,
        ["VK_VOLUME_DOWN"] = 0xAE,
        ["VK_VOLUME_MUTE"] = 0xAD,
    };

    public static IReadOnlyCollection<string> KnownNames => ByName.Keys;

    public static ushort Resolve(string name)
    {
        if (ByName.TryGetValue(name, out var vk)) return vk;
        if (name.StartsWith("0x") && ushort.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex))
            return hex;
        throw new ArgumentException($"Unknown virtual key name '{name}'");
    }
}
