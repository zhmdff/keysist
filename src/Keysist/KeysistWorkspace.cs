using Keysist.Config;
using Keysist.Pipeline;

namespace Keysist;

public sealed class KeysistWorkspace : IKeysistWorkspace
{
    public KeysistWorkspace(ConfigStore config, InputPipeline pipeline)
    {
        Config = config;
        Pipeline = pipeline;
    }

    public ConfigStore Config { get; }

    public InputPipeline Pipeline { get; }

    public void ReloadConfig() => Config.Load();
}
