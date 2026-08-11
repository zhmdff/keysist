// src/Keysist/Tray/TrayIconController.cs
using Keysist.Config;
using Keysist.Pipeline;
using Keysist.Startup;
using Keysist.Ui;
using System.Windows.Forms;

namespace Keysist.Tray;

public class TrayIconController : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly InputPipeline _pipeline;
    private readonly NotifyIcon _icon;

    public TrayIconController(ConfigStore configStore, InputPipeline pipeline)
    {
        _configStore = configStore;
        _pipeline = pipeline;

        if (!AutostartRegistrar.IsRegistered())
            AutostartRegistrar.Register();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Edit Mappings", null, (_, _) => new EditMappingsWindow(_configStore).ShowDialog());
        menu.Items.Add("Discover Device", null, (_, _) => new DiscoverDeviceWindow(_configStore).ShowDialog());
        menu.Items.Add("Reload Config", null, (_, _) => _configStore.Load());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Keysist",
            ContextMenuStrip = menu
        };

        _configStore.LoadFailed += message =>
            _icon.ShowBalloonTip(5000, "Keysist config error", message, ToolTipIcon.Warning);
    }

    public void Show() => _icon.Visible = true;

    public void Dispose() => _icon.Dispose();
}
