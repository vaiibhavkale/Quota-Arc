using QuotaArc;
using System.Drawing;
using System.Windows.Forms;

namespace QuotaArc.AppHost;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private Icon? _drawn;

    public TrayIcon(Action showHub, Action showSettings, Action quit)
    {
        _drawn = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _drawn,
            Text = AppBranding.Name,
            Visible = false
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add($"Open {AppBranding.Name}", null, (_, _) => showHub());
        menu.Items.Add("Settings", null, (_, _) => showSettings());
        menu.Items.Add($"Quit {AppBranding.Name}", null, (_, _) => quit());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                showHub();
        };
        _icon.DoubleClick += (_, _) => showHub();
    }

    public void Show() => _icon.Visible = true;
    public void Hide() => _icon.Visible = false;

    public void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_icon.Visible)
            _icon.Visible = true;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _drawn?.Dispose();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, AppBranding.IconFileName);
            if (File.Exists(ico))
                return LoadBestIcon(ico);
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted is not null) return extracted;
            }
        }
        catch { /* fall back to a drawn mark */ }
        return MakeIcon();
    }

    private static Icon LoadBestIcon(string path)
    {
        var tray = SystemInformation.SmallIconSize;
        var side = Math.Max(32, Math.Max(tray.Width, tray.Height));
        return new Icon(path, side, side);
    }

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.FillEllipse(Brushes.Black, 2, 1, 12, 14);
        g.FillRectangle(Brushes.Black, 10, 1, 6, 14);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
