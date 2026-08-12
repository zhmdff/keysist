using Keysist.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Keysist.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly IKeysistWorkspace _workspace;
    private readonly App _app;
    private readonly NotifyIcon _icon;
    private readonly Icon? _ownedIcon;

    public TrayIconController(IKeysistWorkspace workspace, App app)
    {
        _workspace = workspace;
        _app = app;

        // Autostart is opt-in via Settings — do not force-register on launch.

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Keysist", null, (_, _) => EnqueueShow(null));
        menu.Items.Add("New Mapping", null, (_, _) => EnqueueShow("NewMapping"));
        menu.Items.Add("Register Device", null, (_, _) => EnqueueShow("RegisterDevice"));
        menu.Items.Add("Reload Config", null, (_, _) =>
        {
            Logger.Log("Tray: Reload Config");
            _workspace.ReloadConfig();
        });
        menu.Items.Add("Exit", null, (_, _) =>
        {
            Logger.Log("Tray: Exit");
            _app.EnqueueOnUiThread(_app.ExitApplication);
        });

        _ownedIcon = LoadAppIcon();
        _icon = new NotifyIcon
        {
            Icon = _ownedIcon ?? SystemIcons.Application,
            Text = "Keysist",
            ContextMenuStrip = menu
        };

        _workspace.Config.LoadFailed += message =>
            _icon.ShowBalloonTip(5000, "Keysist config error", message, ToolTipIcon.Warning);
        _workspace.Config.SaveFailed += message =>
            _icon.ShowBalloonTip(5000, "Keysist config save failed", message, ToolTipIcon.Warning);
    }

    public void Show() => _icon.Visible = true;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }

    private void EnqueueShow(string? navigateTo)
    {
        Logger.Log($"Tray: Open UI navigateTo='{navigateTo ?? "(hub)"}' callerThread={Environment.CurrentManagedThreadId}");
        _app.EnqueueOnUiThread(() => _app.ShowMainWindow(navigateTo));
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "keysist.ico");
            if (File.Exists(path))
                return new Icon(path, 16, 16);

            var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            return associated;
        }
        catch (Exception ex)
        {
            Logger.Log($"Tray: LoadAppIcon failed: {ex.Message}");
            return null;
        }
    }
}
