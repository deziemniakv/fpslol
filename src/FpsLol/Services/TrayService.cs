using System.Windows;
using WinForms = System.Windows.Forms;

namespace FpsLol.Services;

public interface ITrayService : IDisposable
{
    void Initialize(Action show, Action exit);
    void SetVisible(bool visible);
    void Notify(string title, string message, bool isError);
}

/// <summary>System tray icon (minimize to tray + balloon notifications while hidden).</summary>
public sealed class TrayService : ITrayService
{
    private WinForms.NotifyIcon? _icon;

    public void Initialize(Action show, Action exit)
    {
        if (_icon is not null) return;

        System.Drawing.Icon? appIcon = null;
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/fpslol.ico"))?.Stream;
            if (stream is not null) appIcon = new System.Drawing.Icon(stream, new System.Drawing.Size(16, 16));
        }
        catch
        {
            // fall back to the default application icon
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open FPS.LOL", null, (_, _) => show());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "FPS.LOL",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) show();
        };
    }

    public void SetVisible(bool visible)
    {
        if (_icon is not null) _icon.Visible = visible;
    }

    public void Notify(string title, string message, bool isError)
    {
        if (_icon is not { Visible: true }) return;
        _icon.ShowBalloonTip(4000, title, message, isError ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
