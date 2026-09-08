using System.Windows;

namespace QuotaArc.Notch;

internal readonly record struct ScreenInfo(
    Rect Frame,
    Rect WorkArea,
    string DeviceName = "",
    string DisplayName = "");

internal static class NotchGeometry
{
    /// <summary>
    /// The panel hugs the chosen *usable* edge (taskbar-aware) and is centred
    /// along the full screen on the other axis, so a bottom taskbar does not
    /// bounce a right-edge notch up and down as it auto-hides.
    /// Frames are rounded out to whole DIPs so the bezel join is flush.
    /// </summary>
    public static Rect PanelFrame(
        ScreenInfo screen,
        Size panelSize,
        NotchEdge edge = NotchEdge.Right,
        double alongOffset = 0,
        double slack = 0)
    {
        var full = screen.Frame;
        var usable = screen.WorkArea;
        var width = Math.Ceiling(panelSize.Width);
        var height = Math.Ceiling(panelSize.Height);

        double x, y;
        // Positive alongOffset on a side edge moves the pill down. WPF y grows
        // down, so this adds; Mac subtracts because AppKit y grows up. Horizontal
        // edges add it to x, same as Mac, so Alt-drag follows the pointer.
        switch (edge)
        {
            case NotchEdge.Right:
                x = usable.Right - width;
                y = Clamp(full.Top + (full.Height - height) / 2 + alongOffset,
                    full.Top - slack, full.Bottom - height + slack);
                break;
            case NotchEdge.Left:
                x = usable.Left;
                y = Clamp(full.Top + (full.Height - height) / 2 + alongOffset,
                    full.Top - slack, full.Bottom - height + slack);
                break;
            case NotchEdge.Top:
                x = Clamp(full.Left + (full.Width - width) / 2 + alongOffset,
                    full.Left - slack, full.Right - width + slack);
                y = usable.Top;
                break;
            default:
                x = Clamp(full.Left + (full.Width - width) / 2 + alongOffset,
                    full.Left - slack, full.Right - width + slack);
                y = usable.Bottom - height;
                break;
        }

        return new Rect(Math.Round(x), Math.Round(y), width, height);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));
}
