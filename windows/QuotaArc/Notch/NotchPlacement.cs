using System.Windows;

namespace QuotaArc.Notch;

/// <summary>
/// The one place that knows which way round the axes are.
/// Everything else works in stack space: along runs the provider stack,
/// across measures inward from the bezel.
/// Panel origin is top-left, matching WPF.
/// </summary>
internal readonly struct NotchPlacement
{
    public NotchPlacement(NotchEdge edge, Size panelSize)
    {
        Edge = edge;
        PanelSize = panelSize;
    }

    public NotchEdge Edge { get; }
    public Size PanelSize { get; }

    public Point Point(double along, double across) => Edge switch
    {
        NotchEdge.Right => new Point(PanelSize.Width - across, along),
        NotchEdge.Left => new Point(across, along),
        NotchEdge.Top => new Point(along, across),
        _ => new Point(along, PanelSize.Height - across)
    };

    public Rect Rect(double along, double across, double length, double depth) => Edge switch
    {
        NotchEdge.Right => new Rect(PanelSize.Width - across - depth, along, depth, length),
        NotchEdge.Left => new Rect(across, along, depth, length),
        NotchEdge.Top => new Rect(along, across, length, depth),
        _ => new Rect(along, PanelSize.Height - across - depth, length, depth)
    };

    public static Size PanelSizeFor(NotchEdge edge, double length, double depth) =>
        edge.IsVertical() ? new Size(depth, length) : new Size(length, depth);

    public double Along(Point point) => Edge.IsVertical() ? point.Y : point.X;

    public double Across(Point point) => Edge switch
    {
        NotchEdge.Right => PanelSize.Width - point.X,
        NotchEdge.Left => point.X,
        NotchEdge.Top => point.Y,
        _ => PanelSize.Height - point.Y
    };

    public double PanelLength => Edge.IsVertical() ? PanelSize.Height : PanelSize.Width;
    public double PanelDepth => Edge.IsVertical() ? PanelSize.Width : PanelSize.Height;
}
