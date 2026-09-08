using System.Windows;
using System.Windows.Media;

namespace QuotaArc.Notch;

/// <summary>
/// Inverse-rounded pill welded to a screen edge. Path is written once for the
/// right edge, then transformed onto the others — same as SideNotchShape.swift.
/// </summary>
internal static class NotchShape
{
    public static StreamGeometry Create(
        Rect rect,
        NotchEdge edge,
        double curlRadius,
        double cornerRadius)
    {
        var depth = edge.IsVertical() ? rect.Width : rect.Height;
        var length = edge.IsVertical() ? rect.Height : rect.Width;
        var canonical = Canonical(new Rect(0, 0, depth, length), curlRadius, cornerRadius, double.PositiveInfinity);
        var transformed = canonical.Clone();
        transformed.Transform = Combine(TransformFor(edge, depth), new TranslateTransform(rect.X, rect.Y));
        transformed.Freeze();
        return transformed;
    }

    public static Matrix TransformFor(NotchEdge edge, double depth) => edge switch
    {
        NotchEdge.Right => Matrix.Identity,
        NotchEdge.Left => new Matrix(-1, 0, 0, 1, depth, 0),
        NotchEdge.Top => new Matrix(0, -1, 1, 0, 0, depth),
        _ => new Matrix(0, 1, 1, 0, 0, 0)
    };

    private static Transform Combine(Matrix a, TranslateTransform b)
    {
        var m = a;
        m.Translate(b.X, b.Y);
        return new MatrixTransform(m);
    }

    private static StreamGeometry Canonical(Rect rect, double flare, double cornerRadius, double cornerCap)
    {
        var wanted = Math.Max(0, Math.Min(Math.Min(cornerRadius, cornerCap), rect.Width / 2));
        var curl = Math.Max(0, Math.Min(flare, Math.Min(rect.Height / 2, rect.Width - wanted)));
        var corner = Math.Max(0, Math.Min(wanted, (rect.Height - 2 * curl) / 2));
        var bodyTop = rect.Top + curl;
        var bodyBottom = rect.Bottom - curl;

        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(rect.Right, rect.Top), true, true);
            if (curl > 0)
            {
                ctx.ArcTo(
                    new Point(rect.Right - curl, bodyTop),
                    new Size(curl, curl),
                    0, false, SweepDirection.Clockwise, true, false);
            }
            ctx.LineTo(new Point(rect.Left + corner, bodyTop), true, false);
            ctx.ArcTo(
                new Point(rect.Left, bodyTop + corner),
                new Size(corner, corner),
                0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(rect.Left, bodyBottom - corner), true, false);
            ctx.ArcTo(
                new Point(rect.Left + corner, bodyBottom),
                new Size(corner, corner),
                0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(rect.Right - curl, bodyBottom), true, false);
            if (curl > 0)
            {
                ctx.ArcTo(
                    new Point(rect.Right, rect.Bottom),
                    new Size(curl, curl),
                    0, false, SweepDirection.Clockwise, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }
}
