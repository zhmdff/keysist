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

    public DeviceConfig? SelectedDevice { get; set; }

    public event Action? SelectedDeviceChanged;

    public void SetSelectedDevice(DeviceConfig? device)
    {
        if (ReferenceEquals(SelectedDevice, device))
            return;

        SelectedDevice = device;
        SelectedDeviceChanged?.Invoke();
    }

    public void ReloadConfig() => Config.Load();
}
