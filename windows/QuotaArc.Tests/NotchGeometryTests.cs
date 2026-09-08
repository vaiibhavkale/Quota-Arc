using QuotaArc.Design;
using QuotaArc.Model;
using QuotaArc.Notch;
using QuotaArc.Providers;
using System.Windows;

namespace QuotaArc.Tests;

public class NotchEdgeTests
{
    [Fact]
    public void StackRunsDownTheSidesAndAcrossTheOthers()
    {
        Assert.True(NotchEdge.Right.IsVertical());
        Assert.True(NotchEdge.Left.IsVertical());
        Assert.False(NotchEdge.Top.IsVertical());
        Assert.False(NotchEdge.Bottom.IsVertical());
    }

    [Fact]
    public void TooltipLeavesByTheInwardFace()
    {
        Assert.Equal(TooltipDirection.Leading, NotchEdge.Right.TooltipDirection());
        Assert.Equal(TooltipDirection.Trailing, NotchEdge.Left.TooltipDirection());
        Assert.Equal(TooltipDirection.Down, NotchEdge.Top.TooltipDirection());
        Assert.Equal(TooltipDirection.Up, NotchEdge.Bottom.TooltipDirection());
    }
}

public class NotchPlacementTests
{
    private static NotchPlacement Place(NotchEdge edge) => new(edge, new Size(400, 900));

    [Fact]
    public void AcrossZeroIsTheEdgeItself()
    {
        Assert.Equal(400, Place(NotchEdge.Right).Point(0, 0).X, 3);
        Assert.Equal(0, Place(NotchEdge.Left).Point(0, 0).X, 3);
        Assert.Equal(0, Place(NotchEdge.Top).Point(0, 0).Y, 3);
        Assert.Equal(900, Place(NotchEdge.Bottom).Point(0, 0).Y, 3);
    }

    [Fact]
    public void AcrossGrowsInwardFromEveryEdge()
    {
        Assert.Equal(370, Place(NotchEdge.Right).Point(0, 30).X, 3);
        Assert.Equal(30, Place(NotchEdge.Left).Point(0, 30).X, 3);
        Assert.Equal(30, Place(NotchEdge.Top).Point(0, 30).Y, 3);
        Assert.Equal(870, Place(NotchEdge.Bottom).Point(0, 30).Y, 3);
    }

    [Fact]
    public void ARectSpansInwardFromTheEdge()
    {
        Assert.Equal(new Rect(330, 10, 70, 200), Place(NotchEdge.Right).Rect(10, 0, 200, 70));
        Assert.Equal(new Rect(0, 10, 70, 200), Place(NotchEdge.Left).Rect(10, 0, 200, 70));
        Assert.Equal(new Rect(10, 0, 200, 70), Place(NotchEdge.Top).Rect(10, 0, 200, 70));
        Assert.Equal(new Rect(10, 830, 200, 70), Place(NotchEdge.Bottom).Rect(10, 0, 200, 70));
    }

    [Fact]
    public void PanelIsTallForTheSidesAndWideForTheRest()
    {
        Assert.Equal(new Size(300, 500), NotchPlacement.PanelSizeFor(NotchEdge.Right, 500, 300));
        Assert.Equal(new Size(500, 300), NotchPlacement.PanelSizeFor(NotchEdge.Top, 500, 300));
    }
}

public class NotchGeometryTests
{
    private static readonly ScreenInfo Screen = new(
        new Rect(0, 0, 1800, 1169),
        new Rect(0, 0, 1800, 1132));

    private static void AssertCentredOnFullHeight(ScreenInfo screen, Rect frame)
    {
        var expected = screen.Frame.Top + screen.Frame.Height / 2;
        var actual = frame.Top + frame.Height / 2;
        Assert.InRange(actual, expected - 0.51, expected + 0.51);
    }

    [Fact]
    public void PanelHugsTheRightEdgeAndIsVerticallyCentred()
    {
        var frame = NotchGeometry.PanelFrame(Screen, new Size(334, 484));
        Assert.Equal(1800, frame.Right, 3);
        AssertCentredOnFullHeight(Screen, frame);
        Assert.Equal(334, frame.Width);
        Assert.Equal(484, frame.Height);
    }

    [Fact]
    public void FractionalSizeStillLandsFlushOnTheEdge()
    {
        var frame = NotchGeometry.PanelFrame(Screen, new Size(334.3247863247863, 205.182905982906));
        Assert.Equal(1800, frame.Right, 4);
        Assert.Equal(frame.X, Math.Round(frame.X), 8);
        Assert.Equal(frame.Y, Math.Round(frame.Y), 8);
        Assert.True(frame.Width >= 334.3247863247863);
        Assert.True(frame.Height >= 205.182905982906);
    }

    [Fact]
    public void BottomEdgeRestsOnTheTaskbar()
    {
        var docked = new ScreenInfo(
            new Rect(0, 0, 1800, 1169),
            new Rect(0, 0, 1800, 1099));
        var frame = NotchGeometry.PanelFrame(docked, new Size(600, 200), NotchEdge.Bottom);
        Assert.Equal(1099, frame.Bottom, 3);
    }

    [Fact]
    public void TopEdgeHangsBelowATopTaskbar()
    {
        var topBar = new ScreenInfo(
            new Rect(0, 0, 1800, 1169),
            new Rect(0, 37, 1800, 1132));
        var frame = NotchGeometry.PanelFrame(topBar, new Size(600, 200), NotchEdge.Top);
        Assert.Equal(37, frame.Top, 3);
    }

    [Fact]
    public void SideTaskbarPushesTheNotchIn()
    {
        var left = new ScreenInfo(
            new Rect(0, 0, 1800, 1169),
            new Rect(90, 0, 1710, 1132));
        var frame = NotchGeometry.PanelFrame(left, new Size(334, 484), NotchEdge.Left);
        Assert.Equal(90, frame.Left, 3);
    }

    [Fact]
    public void CentringIgnoresChromeOnTheOtherAxis()
    {
        var docked = new ScreenInfo(
            new Rect(0, 0, 1800, 1169),
            new Rect(0, 0, 1800, 1099));
        var frame = NotchGeometry.PanelFrame(docked, new Size(334, 484), NotchEdge.Right);
        AssertCentredOnFullHeight(docked, frame);
    }

    [Fact]
    public void ZeroOffsetChangesNothing()
    {
        var size = new Size(334, 484);
        var centred = NotchGeometry.PanelFrame(Screen, size);
        var explicitOffset = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Right, alongOffset: 0);
        Assert.Equal(centred, explicitOffset);
    }

    [Fact]
    public void PositiveOffsetMovesASideEdgePanelDown()
    {
        var size = new Size(334, 484);
        var centred = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Right);
        var nudged = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Right, alongOffset: 100);
        Assert.InRange(nudged.Top + nudged.Height / 2,
            centred.Top + centred.Height / 2 + 100 - 0.5,
            centred.Top + centred.Height / 2 + 100 + 0.5);
        Assert.Equal(centred.Right, nudged.Right, 3);
    }

    [Fact]
    public void PositiveOffsetMovesATopEdgePanelRight()
    {
        var size = new Size(484, 120);
        var centred = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Top);
        var nudged = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Top, alongOffset: 100);
        Assert.InRange(nudged.Left + nudged.Width / 2,
            centred.Left + centred.Width / 2 + 100 - 0.5,
            centred.Left + centred.Width / 2 + 100 + 0.5);
        Assert.Equal(centred.Top, nudged.Top, 3);
    }

    [Fact]
    public void SlackWidensTheDraggableRangeBeyondClampingTheWholePanel()
    {
        var size = new Size(334, 900);
        var withoutSlack = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Right, alongOffset: 10_000, slack: 0);
        var withSlack = NotchGeometry.PanelFrame(Screen, size, NotchEdge.Right, alongOffset: 10_000, slack: 400);
        Assert.True(withSlack.Top > withoutSlack.Top);
    }
}

public class NotchLayoutTests
{
    [Fact]
    public void RingIsTheSpecAnchor() =>
        Assert.Equal(44, NotchLayout.RingDiameter, 3);

    [Fact]
    public void ProportionsMatchTheFrame()
    {
        Assert.Equal(186.0 / 117.0, NotchLayout.BodyDepth(NotchEdge.Right) / NotchLayout.RingDiameter, 3);
        Assert.Equal(600.0 / 117.0, NotchLayout.CardWidth / NotchLayout.RingDiameter, 3);
    }

    [Fact]
    public void ShapeGrowsOneCellAtATime()
    {
        var one = NotchLayout.ShapeLength(1);
        var two = NotchLayout.ShapeLength(2);
        Assert.Equal(NotchLayout.CellExtent + NotchLayout.CellSpacing, two - one, 3);
    }

    [Fact]
    public void CellIsRingAndLabelDownASideEdge()
    {
        Assert.Equal(NotchLayout.CellExtent, NotchLayout.CellAlong(NotchEdge.Right), 3);
        Assert.Equal(NotchLayout.RingDiameter, NotchLayout.CellAlong(NotchEdge.Top), 3);
    }

    [Fact]
    public void HorizontalNotchIsDeepEnoughForTheLabel()
    {
        var needed = NotchLayout.RingDiameter + NotchLayout.RingLabelGap + NotchLayout.PercentLineHeight;
        Assert.True(NotchLayout.BodyDepth(NotchEdge.Top) >= needed);
    }

    [Fact]
    public void SideEdgesKeepTheFramesUnevenPadding()
    {
        Assert.Equal(Scale.Px(69.5), NotchLayout.PadStart(NotchEdge.Right), 3);
        Assert.Equal(Scale.Px(50.1), NotchLayout.PadEnd(NotchEdge.Right), 3);
    }

    [Fact]
    public void OrbArcIsAQuadrantOnEveryEdge()
    {
        foreach (var edge in NotchEdgeInfo.All)
        {
            var (from, to) = NotchLayout.RestingTrim(edge);
            Assert.Equal(0.25, to - from, 4);
        }
        var right = NotchLayout.RestingTrim(NotchEdge.Right);
        Assert.Equal(0.75, right.From, 4);
        Assert.Equal(1.0, right.To, 4);
    }
}

public class FoldingTests
{
    [Fact]
    public void FoldingKeepsTheCentreLineOnEveryEdge()
    {
        foreach (var edge in NotchEdgeInfo.All)
        {
            var model = new NotchViewModel { Edge = edge };
            model.Snapshots =
            [
                new("p0", "P", ProviderGlyph.Claude, Fidelity.Official, new ProviderStatus.Ok(), []),
                new("p1", "P", ProviderGlyph.Claude, Fidelity.Official, new ProviderStatus.Ok(), []),
                new("p2", "P", ProviderGlyph.Claude, Fidelity.Official, new ProviderStatus.Ok(), [])
            ];
            model.IsExpanded = true;
            var open = model.NotchLeadingInset + model.NotchLength / 2;
            model.IsExpanded = false;
            var folded = model.NotchLeadingInset + model.NotchLength / 2;
            Assert.Equal(open, folded, 3);
            Assert.Equal(model.PanelSizeFor(3), model.PanelSize);
            Assert.True(model.NotchDepth < NotchLayout.BodyDepth(edge) / 2);
        }
    }
}
