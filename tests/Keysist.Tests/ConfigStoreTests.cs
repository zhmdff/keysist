using Keysist.Config;
using Xunit;

public class ConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"keysist-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Load_ParsesValidConfig()
    {
        File.WriteAllText(_path, """{ "devices": [ { "name": "Numpad", "devicePathMatch": "VID_1234" } ] }""");
        var store = new ConfigStore(_path);

        store.Load();

        Assert.Single(store.Current.Devices);
        Assert.Equal("Numpad", store.Current.Devices[0].Name);
    }

    [Fact]
    public void Load_MissingFile_YieldsEmptyConfig_NoThrow()
    {
        var store = new ConfigStore(_path); // file never written

        var ex = Record.Exception(() => store.Load());

        Assert.Null(ex);
        Assert.Empty(store.Current.Devices);
    }

    [Fact]
    public void Load_InvalidJson_KeepsLastGoodConfig_AndRaisesLoadFailed()
    {
        File.WriteAllText(_path, """{ "devices": [ { "name": "Numpad", "devicePathMatch": "VID_1234" } ] }""");
        var store = new ConfigStore(_path);
        store.Load();

        string? error = null;
        store.LoadFailed += msg => error = msg;
        File.WriteAllText(_path, "{ not valid json ");
        store.Load();

        Assert.NotNull(error);
        Assert.Single(store.Current.Devices);
        Assert.Equal("Numpad", store.Current.Devices[0].Name);
    }
}
