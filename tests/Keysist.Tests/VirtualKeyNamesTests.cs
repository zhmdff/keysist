using Keysist.Actions;
using Xunit;

public class VirtualKeyNamesTests
{
    [Theory]
    [InlineData("LControlKey", 0xA2)]
    [InlineData("C", 0x43)]
    [InlineData("NumPad1", 0x61)]
    [InlineData("VK_MEDIA_PLAY_PAUSE", 0xB3)]
    [InlineData("0x1B", 0x1B)]
    public void Resolve_KnownNames(string name, int expected) =>
        Assert.Equal((ushort)expected, VirtualKeyNames.Resolve(name));
}
