using System.Globalization;
using System.Windows;
using System.Windows.Media;
using QuotaArc.Design;

namespace QuotaArc.Notch;

/// <summary>
/// Every measurement is quoted in design-frame pixels so it can be checked
/// against docs/design/frame-124-hover-tooltip.png directly.
/// </summary>
internal static class NotchLayout
{
    public static double SideBodyDepth => Scale.Px(186);

    public static double BodyDepth(NotchEdge edge) =>
        edge.IsVertical() ? SideBodyDepth : 2 * SideRingMargin + CellExtent;

    private static double SideRingMargin => (SideBodyDepth - RingDiameter) / 2;

    public static double RingMargin(NotchEdge edge) => SideRingMargin;

    public static double CurlRadius => Scale.Px(103);
    public static double BezelFillet => Scale.Px(28);
    public static double CornerRadius => Scale.Px(78.8);
    public static double PadTop => Scale.Px(69.5);
    public static double PadBottom => Scale.Px(50.1);
    public static double CellSpacing => Scale.Px(83.5);

    public static double PillWidth => Scale.Px(26);
    public static double PillHeight => Scale.Px(210);
    public static double PillHotZone => Scale.Px(90);

    public static double RingDiameter => Scale.Px(117);
    public static double TrackStroke => Scale.Px(15.5);
    public static double ProgressStroke => Scale.Px(8);
    public static double GlyphSize => Scale.Px(46);
    public static double RingLabelGap => Scale.Px(26.9);

    public static double ActivityDiameter => Scale.Px(72);
    public static double ActivityStroke => Scale.Px(5.5);

    public static double OrbDiameter => Scale.Px(124);
    public static double OrbStroke => Scale.Px(18);
    public static double OrbGap => Scale.Px(27);
    public static double OrbArcRadius => CurlRadius - OrbGap;
    public static double OrbConvexArcRadius(double corner) => corner + OrbGap;
    public static double OrbCornerOffset(double corner) =>
        (corner + OrbGap + OrbDiameter / 2) / Math.Sqrt(2);
    public static double OrbGlyph => Scale.Px(56);
    public static double OrbMergeScale => (CurlRadius + OrbStroke) / OrbArcRadius;
    public static double OrbHotZone => Scale.Px(152);
    public static double OrbInsetFromEdge => CurlRadius;

    public static double CardWidth => Scale.Px(600);
    public static double CardCorner => Scale.Px(49.5);
    public static double CardPadding => Scale.Px(32);
    public static double TailLength => Scale.Px(75);
    public static double TailHeight => Scale.Px(87);
    public static double TailGap => Scale.Px(28);
    public static double BarHeight => Scale.Px(10.5);
    public static double HeaderGap => Scale.Px(17);
    public static double HeaderToBlock => Scale.Px(21);
    public static double LabelToBar => Scale.Px(16.8);
    public static double BarToUsed => Scale.Px(17.8);
    public static double BlockSpacing => Scale.Px(20);
    public static double SessionRowGap => Scale.Px(10);
    public static double StatusDot => Scale.Px(17);
    public static double StatusDotStroke => Scale.Px(3.4);
    public static double StatusDotGap => Scale.Px(11);
    public static double Hairline => Scale.Px(2.5);

    public static double PercentLineHeight { get; } = MeasureLine(27, FontWeights.SemiBold);
    public static double CardTitleLineHeight { get; } = MeasureLine(26, FontWeights.SemiBold);
    public static double CardBodyLineHeight { get; } = MeasureLine(18, FontWeights.Normal);

    public static double CardTextWidth => CardWidth - 2 * CardPadding;

    public static double BodyTextHeight(string text)
    {
        if (string.IsNullOrEmpty(text)) return CardBodyLineHeight;
        try
        {
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typography.Ui,
                Scale.FontSize(18),
                Brushes.White,
                96);
            ft.MaxTextWidth = CardTextWidth;
            var lines = Math.Max(1, (int)Math.Ceiling(ft.Height / CardBodyLineHeight));
            return lines * CardBodyLineHeight;
        }
        catch
        {
            var chars = Math.Max(1, (int)(CardTextWidth / (Scale.FontSize(18) * 0.52)));
            var lines = Math.Max(1, (int)Math.Ceiling(text.Length / (double)chars));
            return lines * CardBodyLineHeight;
        }
    }

    private static double MeasureLine(double capPixels, FontWeight weight)
    {
        var size = Scale.FontSize(capPixels);
        try
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);
            var ft = new FormattedText(
                "Ag",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                Brushes.White,
                96);
            return Math.Ceiling(ft.Height);
        }
        catch
        {
            return Math.Ceiling(size * 1.235);
        }
    }

    public static double CellExtent => RingDiameter + RingLabelGap + PercentLineHeight;

    public static double CellAlong(NotchEdge edge) =>
        edge.IsVertical() ? CellExtent : RingDiameter;

    public static double CellPitch(NotchEdge edge) => CellAlong(edge) + CellSpacing;

    public static double PadStart(NotchEdge edge) =>
        edge.IsVertical() ? PadTop : (PadTop + PadBottom) / 2;

    public static double PadEnd(NotchEdge edge) =>
        edge.IsVertical() ? PadBottom : (PadTop + PadBottom) / 2;

    public static double RingCenter(int index, NotchEdge edge = NotchEdge.Right, double flare = double.NaN)
    {
        if (double.IsNaN(flare)) flare = CurlRadius;
        return flare + PadStart(edge) + RingDiameter / 2 + index * CellPitch(edge);
    }

    public static double BodyLength(int cellCount, NotchEdge edge = NotchEdge.Right)
    {
        var start = PadStart(edge);
        var end = PadEnd(edge);
        if (cellCount <= 0) return start + end;
        return start + cellCount * CellAlong(edge) + (cellCount - 1) * CellSpacing + end;
    }

    public static double OrbCenterAlong(int cellCount, NotchEdge edge = NotchEdge.Right) =>
        ShapeLength(cellCount, edge);

    public static double ShapeLength(int cellCount, NotchEdge edge = NotchEdge.Right, double flare = double.NaN)
    {
        if (double.IsNaN(flare)) flare = CurlRadius;
        return BodyLength(cellCount, edge) + 2 * flare;
    }

    public static double CardHeight(
        int windowCount,
        int sessionCount = 0,
        int sessionCap = 4,
        string? statusMessage = null,
        string? blockMessage = null)
    {
        var header = Math.Max(GlyphSize, CardTitleLineHeight);
        var height = 2 * CardPadding + header;

        if (blockMessage != null)
            height += HeaderToBlock + BodyTextHeight(blockMessage);

        if (windowCount > 0)
        {
            var block = 2 * CardBodyLineHeight + LabelToBar + BarHeight + BarToUsed;
            height += HeaderToBlock
                + windowCount * block
                + (windowCount - 1) * BlockSpacing;
        }
        else
        {
            height += HeaderToBlock + BodyTextHeight(statusMessage ?? "");
        }

        if (sessionCount > 0)
        {
            var shown = Math.Min(sessionCount, Math.Max(0, sessionCap));
            var row = 2 * CardBodyLineHeight + SessionRowGap;
            height += BlockSpacing + Hairline + BlockSpacing
                + shown * row
                + Math.Max(0, shown - 1) * BlockSpacing;
            if (sessionCount > shown)
                height += BlockSpacing + CardBodyLineHeight;
        }
        return height;
    }

    public static double Slack(NotchEdge edge, double maxCardHeight = -1)
    {
        if (maxCardHeight < 0) maxCardHeight = DefaultMaxCardHeight;
        return edge.IsVertical()
            ? Math.Max(EndSlack, maxCardHeight / 2 + CardCorner)
            : Math.Max(EndSlack, CardWidth / 2 + CardCorner);
    }

    private static double EndSlack => Scale.Px(190);

    public const int MaxWindowCount = 4;
    public const int SessionCeiling = 12;
    public const int DefaultSessionCap = 4;

    public static int SessionsFitting(double cardBudget, int windowCount)
    {
        var fits = 0;
        for (var n = 1; n <= SessionCeiling; n++)
        {
            var height = CardHeight(windowCount, n + 1, n);
            if (height > cardBudget) break;
            fits = n;
        }
        return fits;
    }

    public static double MaxCardHeight(int sessionCap) =>
        CardHeight(MaxWindowCount, sessionCap + 1, sessionCap);

    public static double DefaultMaxCardHeight { get; } = MaxCardHeight(DefaultSessionCap);

    public static double TooltipDepth(NotchEdge edge, double maxCardHeight = -1)
    {
        if (maxCardHeight < 0) maxCardHeight = DefaultMaxCardHeight;
        return (edge.IsVertical() ? CardWidth : maxCardHeight) + TailLength + TailGap;
    }

    public static (double From, double To) RestingTrim(NotchEdge edge, bool convex = false)
    {
        var (from, to) = edge switch
        {
            NotchEdge.Right => (0.75, 1.0),
            NotchEdge.Left => (0.5, 0.75),
            NotchEdge.Top => (0.5, 0.75),
            _ => (0.25, 0.5)
        };
        if (!convex) return (from, to);
        var turned = (from + 0.5) % 1.0;
        return (turned, turned + 0.25);
    }
}
