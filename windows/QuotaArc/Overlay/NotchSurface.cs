using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using QuotaArc.Design;
using QuotaArc.Model;
using QuotaArc.Notch;
using QuotaArc.Providers;
using QuotaArc.Sessions;
using QuotaArc.Settings;

namespace QuotaArc.Overlay;

internal sealed class NotchSurface : FrameworkElement
{
    public NotchViewModel Model { get; }

    private readonly SpringValue _length = new();
    private readonly SpringValue _depth = new();
    private readonly SpringValue _expand = new();
    private readonly SpringValue _orbHover = new() { Spec = SpringSpec.OrbHover };
    private readonly SpringValue _tooltipOpacity = new() { Spec = SpringSpec.Glide };
    private readonly SpringValue _tooltipAlong = new() { Spec = SpringSpec.Glide };
    private readonly SpringValue _tooltipHeight = new() { Spec = SpringSpec.Glide };
    private readonly Dictionary<string, SpringValue> _sweeps = [];
    private readonly Dictionary<string, SpringValue> _press = [];
    private readonly List<SpringValue> _cellFade = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _spin;
    private double _pulse;
    private double _lastRender = double.NaN;
    private bool _wasExpanded;
    private int _lastCellCount = -1;

    public NotchSurface(NotchViewModel model)
    {
        Model = model;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
        Model.PropertyChanged += (_, _) => UpdateTargets();
        CompositionTarget.Rendering += OnRenderFrame;
        Loaded += (_, _) =>
        {
            _wasExpanded = Model.IsExpanded;
            JumpToModel();
        };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRenderFrame;
    }

    public void JumpToModel()
    {
        _length.Jump(Model.NotchLength);
        _depth.Jump(Model.NotchDepth);
        _expand.Jump(Model.IsExpanded ? 1 : 0);
        UpdateTargets();
        InvalidateVisual();
    }

    private void UpdateTargets()
    {
        _length.Spec = SpringSpec.Unfold;
        _depth.Spec = SpringSpec.Unfold;
        _expand.Spec = SpringSpec.Unfold;
        _length.Target = Model.IsExpanded ? Model.ShapeLength : Model.RestingLength;
        _depth.Target = Model.IsExpanded ? Model.NotchDepth : Model.RestingDepth;
        _expand.Target = Model.IsExpanded ? 1 : 0;
        _orbHover.Target = Model.IsHoveringSettings ? 1 : 0;

        while (_cellFade.Count < Model.Snapshots.Count)
            _cellFade.Add(new SpringValue { Spec = SpringSpec.Contents });
        var expanding = Model.IsExpanded && !_wasExpanded;
        var countChanged = Model.Snapshots.Count != _lastCellCount;
        _wasExpanded = Model.IsExpanded;
        _lastCellCount = Model.Snapshots.Count;
        for (var i = 0; i < Model.Snapshots.Count; i++)
        {
            _cellFade[i].Spec = SpringSpec.Contents;
            if (expanding || countChanged)
                _cellFade[i].Delay = Model.IsExpanded ? SpringSpec.StaggerDelay(i) : 0;
            if (!Model.IsExpanded)
                _cellFade[i].Delay = 0;
            _cellFade[i].Target = Model.IsExpanded ? 1 : 0;
        }

        foreach (var snap in Model.Snapshots)
        {
            if (!_sweeps.TryGetValue(snap.Id, out var sweep))
            {
                sweep = new SpringValue(snap.RingFraction ?? 0) { Spec = SpringSpec.Reading };
                _sweeps[snap.Id] = sweep;
            }
            sweep.Target = snap.RingFraction ?? 0;
            if (!_press.TryGetValue(snap.Id, out var press))
            {
                press = new SpringValue(1) { Spec = SpringSpec.RefreshPress };
                _press[snap.Id] = press;
            }
            press.Target = Model.Refreshing.Contains(snap.Id) ? 0.93 : 1;
        }

        if (Model.HoveredSnapshot is { } hovered && Model.HoveredIndex is { } idx && Model.IsExpanded)
        {
            _tooltipOpacity.Target = 1;
            _tooltipAlong.Target = Model.Slack + Model.RingCenter(idx);
            _tooltipHeight.Target = NotchLayout.CardHeight(
                hovered.Windows.Count,
                Model.Activity(hovered.Id)?.Sessions.Count ?? 0,
                Model.SessionCap,
                hovered.StatusMessage,
                hovered.Block?.Summary(Model.Now));
        }
        else
        {
            _tooltipOpacity.Target = 0;
        }

        InvalidateVisual();
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = double.IsNaN(_lastRender) ? 1.0 / 60 : Math.Clamp(now - _lastRender, 1.0 / 240, 0.05);
        _lastRender = now;
        Typography.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var reduce = ReduceMotion;

        var dirty = false;
        dirty |= _length.Step(dt, reduce);
        dirty |= _depth.Step(dt, reduce);
        dirty |= _expand.Step(dt, reduce);
        dirty |= _orbHover.Step(dt, reduce);
        dirty |= _tooltipOpacity.Step(dt, reduce);
        dirty |= _tooltipAlong.Step(dt, reduce);
        dirty |= _tooltipHeight.Step(dt, reduce);
        foreach (var s in _sweeps.Values) dirty |= s.Step(dt, reduce);
        foreach (var s in _press.Values) dirty |= s.Step(dt, reduce);
        foreach (var s in _cellFade) dirty |= s.Step(dt, reduce);

        var spinning = Model.Snapshots.Any(snap =>
            Model.Activity(snap.Id) is { State: ActivitySummary.Kind.Working or ActivitySummary.Kind.Waiting });
        if (spinning)
        {
            _spin = (_spin + dt * 360 / 1.1) % 360;
            _pulse = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(now * Math.PI / 0.9));
            dirty = true;
        }

        if (dirty) InvalidateVisual();
    }

    private static bool ReduceMotion =>
        string.Equals(Environment.GetEnvironmentVariable("QUOTAARC_REDUCE_MOTION"), "1",
            StringComparison.Ordinal);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var place = new NotchPlacement(Model.Edge, size);
        DrawNotch(dc, place);
        if (Model.Snapshots.Count > 0) DrawOrb(dc, place);
        if (_tooltipOpacity.Current > 0.01 && Model.HoveredSnapshot is { } snap)
            DrawTooltip(dc, place, snap);
    }

    private void DrawNotch(DrawingContext dc, NotchPlacement place)
    {
        var length = Math.Max(1, _length.Current);
        var depth = Math.Max(1, _depth.Current);
        var notchSize = NotchPlacement.PanelSizeFor(Model.Edge, length, depth);
        var centre = place.Point(
            Model.Slack + (Model.ShapeLength - length) / 2 + length / 2,
            depth / 2);
        var rect = new Rect(
            centre.X - notchSize.Width / 2,
            centre.Y - notchSize.Height / 2,
            notchSize.Width,
            notchSize.Height);
        var geo = NotchShape.Create(rect, Model.Edge, NotchLayout.CurlRadius, NotchLayout.CornerRadius);
        dc.DrawGeometry(Palette.NotchBrush, null, geo);

        dc.PushClip(geo);
        var expand = Math.Clamp(_expand.Current, 0, 1);
        for (var i = 0; i < Model.Snapshots.Count; i++)
        {
            var fade = i < _cellFade.Count ? Math.Clamp(_cellFade[i].Current, 0, 1) : expand;
            if (fade <= 0.01) continue;
            var outward = Model.Edge.Outward();
            var slide = (1 - fade) * Scale.Px(28);
            DrawCell(dc, place, i, fade, new Vector(outward.X * slide, outward.Y * slide));
        }
        dc.Pop();
    }

    private void DrawCell(DrawingContext dc, NotchPlacement place, int index, double fade, Vector slide)
    {
        var snap = Model.Snapshots[index];
        var along = Model.Slack + Model.RingCenter(index);
        double ringAcross;
        double labelAlong = along;
        double labelAcross;
        if (Model.Edge.IsVertical())
        {
            ringAcross = Model.ContentInset + NotchLayout.BodyDepth(Model.Edge) / 2;
            labelAlong = along + NotchLayout.RingDiameter / 2 + NotchLayout.RingLabelGap + NotchLayout.PercentLineHeight / 2;
            labelAcross = ringAcross;
        }
        else if (Model.Edge == NotchEdge.Top)
        {
            ringAcross = Model.ContentInset + NotchLayout.RingMargin(Model.Edge) + NotchLayout.RingDiameter / 2;
            labelAcross = ringAcross + NotchLayout.RingDiameter / 2 + NotchLayout.RingLabelGap + NotchLayout.PercentLineHeight / 2;
        }
        else
        {
            labelAcross = Model.ContentInset + NotchLayout.RingMargin(Model.Edge) + NotchLayout.PercentLineHeight / 2;
            ringAcross = labelAcross + NotchLayout.PercentLineHeight / 2 + NotchLayout.RingLabelGap + NotchLayout.RingDiameter / 2;
        }

        var ringCenter = place.Point(along, ringAcross) + slide;
        var labelCenter = place.Point(labelAlong, labelAcross) + slide;
        dc.PushOpacity(fade);
        DrawRing(dc, ringCenter, snap);
        var text = snap.HasReading ? snap.HeadlineText : "—";
        var ft = Typography.Make(text, Typography.UiSemibold, Typography.PercentSize, Palette.TextPrimaryBrush);
        dc.DrawText(ft, new Point(labelCenter.X - ft.Width / 2, labelCenter.Y - ft.Height / 2));
        dc.Pop();
    }

    private void DrawRing(DrawingContext dc, Point center, ProviderSnapshot snap)
    {
        var scale = _press.TryGetValue(snap.Id, out var p) ? p.Current : 1;
        var r = NotchLayout.RingDiameter / 2 * scale;
        var track = new Pen(Palette.RingTrackBrush, NotchLayout.TrackStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        track.Freeze();
        dc.DrawEllipse(null, track, center, r - NotchLayout.TrackStroke / 2, r - NotchLayout.TrackStroke / 2);

        var sweep = _sweeps.TryGetValue(snap.Id, out var s) ? Math.Clamp(s.Current, 0, 1) : 0;
        var blocked = snap.Block is not null;
        var band = blocked ? UsageBand.Exhausted : UsageBands.Band(snap.UsedFraction ?? 0);
        if (snap.HasReading && snap.RingFraction is not null)
        {
            var progress = new Pen(band.Brush(Model.AccentColor), NotchLayout.ProgressStroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            progress.Freeze();
            DrawArc(dc, center, r - NotchLayout.TrackStroke / 2, -90, sweep * 360, progress);
        }

        var glyphSize = NotchLayout.GlyphSize * snap.Glyph.OpticalScale() * scale;
        var glyphRect = new Rect(center.X - glyphSize / 2, center.Y - glyphSize / 2, glyphSize, glyphSize);
        var glyphBrush = band == UsageBand.Exhausted
            ? new SolidColorBrush(Color.FromArgb(89, 255, 255, 255))
            : Palette.TextPrimaryBrush;
        dc.DrawGeometry(glyphBrush, null, snap.Glyph.Geometry(glyphRect));

        if (snap.Status.IsStale || !snap.HasReading)
        {
            dc.PushOpacity(0.45);
            dc.DrawRectangle(Brushes.Transparent, null, glyphRect);
            dc.Pop();
        }

        var activity = Model.Activity(snap.Id);
        if (activity is { State: not ActivitySummary.Kind.Idle })
        {
            var inset = (NotchLayout.RingDiameter - NotchLayout.ActivityDiameter) / 2;
            var ar = r - inset * scale;
            var pen = new Pen(activity.Brush, NotchLayout.ActivityStroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            if (activity.State == ActivitySummary.Kind.Working)
                DrawArc(dc, center, ar, _spin - 90, 90, pen);
            else
            {
                dc.PushOpacity(_pulse);
                dc.DrawEllipse(null, pen, center, ar, ar);
                dc.Pop();
            }
        }
    }

    private void DrawOrb(DrawingContext dc, NotchPlacement place)
    {
        var expand = Math.Clamp(_expand.Current, 0, 1);
        if (expand < 0.02) return;
        var hover = Math.Clamp(_orbHover.Current, 0, 1);
        var merge = Model.IsExpanded ? 1 : Model.OrbMergeScale;
        var scale = 1 + (merge - 1) * (1 - expand);
        var centre = place.Point(Model.Slack + Model.OrbAlong, Model.OrbInset);
        dc.PushOpacity(expand);
        dc.PushTransform(new ScaleTransform(scale, scale, centre.X, centre.Y));

        var (from, to) = NotchLayout.RestingTrim(Model.Edge, Model.OrbHugsCorner);
        var arcPen = new Pen(Palette.NotchBrush, NotchLayout.OrbStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        arcPen.Freeze();
        dc.PushOpacity(1 - hover);
        DrawArc(dc, centre, Model.OrbArcRadius, from * 360, (to - from) * 360, arcPen);
        dc.Pop();

        dc.PushOpacity(hover);
        dc.DrawEllipse(Palette.NotchBrush, null, centre, NotchLayout.OrbDiameter / 2, NotchLayout.OrbDiameter / 2);
        var gear = Typography.Make("\uE713", new Typeface("Segoe MDL2 Assets"), NotchLayout.OrbGlyph, Palette.TextPrimaryBrush);
        dc.DrawText(gear, new Point(centre.X - gear.Width / 2, centre.Y - gear.Height / 2));
        dc.Pop();

        dc.Pop();
        dc.Pop();
    }

    private void DrawTooltip(DrawingContext dc, NotchPlacement place, ProviderSnapshot snap)
    {
        var height = Math.Max(40, _tooltipHeight.Current);
        var along = _tooltipAlong.Current;
        var cardAcross = Model.Edge.IsVertical() ? NotchLayout.CardWidth : height;
        var centre = place.Point(along, Model.TooltipInset + (NotchLayout.TailLength + cardAcross) / 2);
        dc.PushOpacity(Math.Clamp(_tooltipOpacity.Current, 0, 1));

        Size tailSize = Model.Edge.TooltipDirection() is TooltipDirection.Leading or TooltipDirection.Trailing
            ? new Size(NotchLayout.TailLength, NotchLayout.TailHeight)
            : new Size(NotchLayout.TailHeight, NotchLayout.TailLength);

        Rect card;
        Point tip, a, b;
        switch (Model.Edge.TooltipDirection())
        {
            case TooltipDirection.Leading:
                card = new Rect(centre.X - (NotchLayout.CardWidth + tailSize.Width) / 2, centre.Y - height / 2,
                    NotchLayout.CardWidth, height);
                tip = new Point(card.Right + tailSize.Width, card.Top + height / 2);
                a = new Point(card.Right, card.Top + height / 2 - tailSize.Height / 2);
                b = new Point(card.Right, card.Top + height / 2 + tailSize.Height / 2);
                break;
            case TooltipDirection.Trailing:
                tip = new Point(centre.X - (NotchLayout.CardWidth + tailSize.Width) / 2, centre.Y);
                card = new Rect(tip.X + tailSize.Width, centre.Y - height / 2, NotchLayout.CardWidth, height);
                a = new Point(card.Left, card.Top + height / 2 - tailSize.Height / 2);
                b = new Point(card.Left, card.Top + height / 2 + tailSize.Height / 2);
                break;
            case TooltipDirection.Down:
                tip = new Point(centre.X, centre.Y - (height + tailSize.Height) / 2);
                card = new Rect(centre.X - NotchLayout.CardWidth / 2, tip.Y + tailSize.Height, NotchLayout.CardWidth, height);
                a = new Point(card.Left + NotchLayout.CardWidth / 2 - tailSize.Width / 2, card.Top);
                b = new Point(card.Left + NotchLayout.CardWidth / 2 + tailSize.Width / 2, card.Top);
                break;
            default:
                card = new Rect(centre.X - NotchLayout.CardWidth / 2, centre.Y - (height + tailSize.Height) / 2,
                    NotchLayout.CardWidth, height);
                tip = new Point(centre.X, card.Bottom + tailSize.Height);
                a = new Point(card.Left + NotchLayout.CardWidth / 2 - tailSize.Width / 2, card.Bottom);
                b = new Point(card.Left + NotchLayout.CardWidth / 2 + tailSize.Width / 2, card.Bottom);
                break;
        }

        var tail = new StreamGeometry();
        using (var ctx = tail.Open())
        {
            ctx.BeginFigure(a, true, true);
            ctx.LineTo(tip, true, false);
            ctx.LineTo(b, true, false);
        }
        tail.Freeze();
        dc.DrawGeometry(Palette.CardBrush, null, tail);

        var clip = new RectangleGeometry(card, NotchLayout.CardCorner, NotchLayout.CardCorner);
        dc.DrawGeometry(Palette.CardBrush, null, clip);
        dc.PushClip(clip);
        DrawTooltipContents(dc, card, snap);
        dc.Pop();
        dc.Pop();
    }

    private void DrawTooltipContents(DrawingContext dc, Rect card, ProviderSnapshot snap)
    {
        var x = card.X + NotchLayout.CardPadding;
        var y = card.Y + NotchLayout.CardPadding;
        var glyphSize = NotchLayout.GlyphSize;
        dc.DrawGeometry(Palette.TextPrimaryBrush, null,
            snap.Glyph.Geometry(new Rect(x, y, glyphSize, glyphSize)));
        var title = Typography.Make($"{snap.DisplayName} Usage", Typography.UiSemibold, Typography.CardTitleSize, Palette.TextPrimaryBrush);
        dc.DrawText(title, new Point(x + glyphSize + NotchLayout.HeaderGap, y + (glyphSize - title.Height) / 2));
        y += Math.Max(glyphSize, NotchLayout.CardTitleLineHeight);

        if (snap.Block is { } block)
        {
            y += NotchLayout.HeaderToBlock;
            var msg = Typography.Make(block.Summary(Model.Now), Typography.Ui, Typography.CardBodySize, Palette.CriticalBrush, wrapWidth: NotchLayout.CardTextWidth);
            dc.DrawText(msg, new Point(x, y));
            y += msg.Height;
        }

        if (snap.StatusMessage is { } status)
        {
            y += NotchLayout.HeaderToBlock;
            var msg = Typography.Make(status, Typography.Ui, Typography.CardBodySize, Palette.TextSecondaryBrush, wrapWidth: NotchLayout.CardTextWidth);
            dc.DrawText(msg, new Point(x, y));
        }
        else
        {
            foreach (var window in snap.Windows)
            {
                y += y <= card.Y + NotchLayout.CardPadding + Math.Max(glyphSize, NotchLayout.CardTitleLineHeight) + 0.1
                    ? NotchLayout.HeaderToBlock
                    : NotchLayout.BlockSpacing;
                y = DrawWindow(dc, x, y, window, snap.Fidelity);
            }
        }

        var activity = Model.Activity(snap.Id);
        if (activity is { Sessions.Count: > 0 })
        {
            y += NotchLayout.BlockSpacing;
            dc.DrawRectangle(Palette.RingTrackBrush, null, new Rect(x, y, NotchLayout.CardTextWidth, NotchLayout.Hairline));
            y += NotchLayout.Hairline;
            var ordered = activity.Sessions.OrderBy(s => s.State switch
            {
                AgentState.Waiting => 0,
                AgentState.Busy => 1,
                _ => 2
            }).ThenByDescending(s => s.Since).ToList();
            var shown = ordered.Take(Model.SessionCap).ToList();
            foreach (var session in shown)
            {
                y += NotchLayout.BlockSpacing;
                y = DrawSession(dc, x, y, session);
            }
            var hidden = ordered.Count - shown.Count;
            if (hidden > 0)
            {
                y += NotchLayout.BlockSpacing;
                var more = Typography.Make($"and {hidden} more", Typography.Ui, Typography.CardBodySize, Palette.TextSecondaryBrush);
                dc.DrawText(more, new Point(x, y));
            }
        }
    }

    private double DrawWindow(DrawingContext dc, double x, double y, LimitWindow window, Fidelity fidelity)
    {
        var reset = window.ResetsAt is { } at ? ResetCopy.Text(at, Model.Now, format: Model.ResetTimeFormat) : "";
        var left = Typography.Make(window.Label, Typography.Ui, Typography.CardBodySize, Palette.TextPrimaryBrush);
        var right = Typography.Make(reset, Typography.Ui, Typography.CardBodySize, Palette.TextSecondaryBrush);
        dc.DrawText(left, new Point(x, y));
        dc.DrawText(right, new Point(x + NotchLayout.CardTextWidth - right.Width, y));
        y += NotchLayout.CardBodyLineHeight;
        if (window.UsedFraction is { } frac)
        {
            y += NotchLayout.LabelToBar;
            var track = new Rect(x, y, NotchLayout.CardTextWidth, NotchLayout.BarHeight);
            dc.DrawRoundedRectangle(Palette.BarTrackBrush, null, track, NotchLayout.BarHeight / 2, NotchLayout.BarHeight / 2);
            var fillW = Math.Max(NotchLayout.BarHeight, NotchLayout.CardTextWidth * Math.Clamp(frac, 0, 1));
            dc.DrawRoundedRectangle(UsageBands.Band(frac).Brush(Model.AccentColor), null,
                new Rect(x, y, fillW, NotchLayout.BarHeight), NotchLayout.BarHeight / 2, NotchLayout.BarHeight / 2);
            y += NotchLayout.BarHeight;
        }
        y += NotchLayout.BarToUsed;
        var summary = Typography.Make($"{(window.UsedFraction is null ? "" : fidelity.Qualifier())}{window.Summary}",
            Typography.Ui, Typography.CardBodySize, Palette.TextPrimaryBrush);
        dc.DrawText(summary, new Point(x, y));
        return y + NotchLayout.CardBodyLineHeight;
    }

    private double DrawSession(DrawingContext dc, double x, double y, AgentSession session)
    {
        var color = session.State switch
        {
            AgentState.Busy => Model.AccentColor.Brush(),
            AgentState.Waiting => Palette.WatchBrush,
            _ => Palette.TextSecondaryBrush
        };
        var word = session.State switch
        {
            AgentState.Busy => "working",
            AgentState.Waiting => "waiting",
            _ => "idle"
        };
        var name = Typography.Make(session.Name, Typography.Ui, Typography.CardBodySize, Palette.TextPrimaryBrush);
        var state = Typography.Make(word, Typography.Ui, Typography.CardBodySize, color);
        dc.DrawText(name, new Point(x, y));
        var ringR = NotchLayout.StatusDot / 2;
        var ringC = new Point(x + NotchLayout.CardTextWidth - state.Width - NotchLayout.StatusDotGap - ringR, y + state.Height / 2);
        var pen = new Pen(color, NotchLayout.StatusDotStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var trim = session.State switch { AgentState.Busy => 0.75, AgentState.Waiting => 0.5, _ => 1.0 };
        DrawArc(dc, ringC, ringR, session.State == AgentState.Busy ? _spin - 90 : -90, trim * 360, pen);
        dc.DrawText(state, new Point(x + NotchLayout.CardTextWidth - state.Width, y));
        y += NotchLayout.CardBodyLineHeight + NotchLayout.SessionRowGap;
        var detail = session.State == AgentState.Waiting && !string.IsNullOrEmpty(session.WaitingFor)
            ? session.WaitingFor!
            : session.Detail;
        var d = Typography.Make(detail, Typography.Ui, Typography.CardBodySize, Palette.TextSecondaryBrush);
        var elapsed = Typography.Make(ElapsedCopy.Text(session.Since, Model.Now), Typography.Ui, Typography.CardBodySize, Palette.TextSecondaryBrush);
        dc.DrawText(d, new Point(x, y));
        dc.DrawText(elapsed, new Point(x + NotchLayout.CardTextWidth - elapsed.Width, y));
        return y + NotchLayout.CardBodyLineHeight;
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double startDeg, double sweepDeg, Pen pen)
    {
        if (radius <= 0 || Math.Abs(sweepDeg) < 0.2) return;
        if (Math.Abs(sweepDeg) >= 359.5)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var start = Polar(center, radius, startDeg);
        var end = Polar(center, radius, startDeg + sweepDeg);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, Math.Abs(sweepDeg) > 180,
                sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static Point Polar(Point c, double r, double deg)
    {
        var a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }
}
