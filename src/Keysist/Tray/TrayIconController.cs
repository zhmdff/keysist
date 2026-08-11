// src/Keysist/Tray/TrayIconController.cs
using Keysist.Config;
using Keysist.Pipeline;

namespace Keysist.Tray;

public class TrayIconController : IDisposable
{
    public TrayIconController(ConfigStore configStore, InputPipeline pipeline) { }
    public void Show() { }
    public void Dispose() { }
}
