using Keysist.Config;
using Keysist.Pipeline;

namespace Keysist;

public interface IKeysistWorkspace
{
    ConfigStore Config { get; }

    InputPipeline Pipeline { get; }

    void ReloadConfig();
}
