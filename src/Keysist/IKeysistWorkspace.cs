using Keysist.Config;
using Keysist.Pipeline;

namespace Keysist;

public interface IKeysistWorkspace
{
    ConfigStore Config { get; }

    InputPipeline Pipeline { get; }

    DeviceConfig? SelectedDevice { get; set; }

    event Action? SelectedDeviceChanged;

    void SetSelectedDevice(DeviceConfig? device);

    void ReloadConfig();
}
