using Keysist.RawInputInterop;

namespace Keysist.Core.Tests;

public class RawInputBufferReaderTests
{
    [Fact]
    public void TryResolveDevicePath_returns_null_when_no_events_queued()
    {
        var reader = new RawInputBufferReader();
        Assert.Null(reader.TryResolveDevicePath(vkey: 65, isKeyUp: false));
    }
}
