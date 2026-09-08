using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using QuotaArc.Design;
using QuotaArc.Model;
using QuotaArc.Sessions;
using QuotaArc.Settings;

namespace QuotaArc.Notch;

internal sealed class NotchViewModel : INotifyPropertyChanged
{
    private List<ProviderSnapshot> _snapshots = [];
    private Dictionary<string, List<AgentSession>> _sessions = [];
    private int? _hoveredIndex;
    private DateTime _now = DateTime.Now;
    private bool _isExpanded;
    private bool _isPinned;
    private bool _isAlwaysOn;
    private HashSet<string> _refreshing = [];
    private bool _isHoveringSettings;
    private NotchEdge _edge = NotchEdge.Right;
    private Size _screenSize;
    private Size _screenUsableSize;
    private double _alongOffset;
    private AccentColorChoice _accentColor = AccentColorChoice.System;
    private ResetTimeFormat _resetTimeFormat = ResetTimeFormat.Automatic;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<ProviderSnapshot> Snapshots
    {
        get => _snapshots;
        set { _snapshots = value; Raise(); }
    }

    public Dictionary<string, List<AgentSession>> Sessions
    {
        get => _sessions;
        set { _sessions = value; Raise(); }
    }

    public int? HoveredIndex
    {
        get => _hoveredIndex;
        set { if (_hoveredIndex != value) { _hoveredIndex = value; Raise(); } }
    }

    public DateTime Now
    {
        get => _now;
        set { _now = value; Raise(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; Raise(); } }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned != value) { _isPinned = value; Raise(); } }
    }

    public bool IsAlwaysOn
    {
        get => _isAlwaysOn;
        set { if (_isAlwaysOn != value) { _isAlwaysOn = value; Raise(); } }
    }

    public bool StaysOpen => IsPinned || IsAlwaysOn;

    public HashSet<string> Refreshing
    {
        get => _refreshing;
        set { _refreshing = value; Raise(); }
    }

    public bool IsHoveringSettings
    {
        get => _isHoveringSettings;
        set { if (_isHoveringSettings != value) { _isHoveringSettings = value; Raise(); } }
    }

    public NotchEdge Edge
    {
        get => _edge;
        set { if (_edge != value) { _edge = value; Raise(); } }
    }

    public Size ScreenSize
    {
        get => _screenSize;
        set { if (_screenSize != value) { _screenSize = value; Raise(); } }
    }

    public Size ScreenUsableSize
    {
        get => _screenUsableSize;
        set { if (_screenUsableSize != value) { _screenUsableSize = value; Raise(); } }
    }

    public double AlongOffset
    {
        get => _alongOffset;
        set { if (Math.Abs(_alongOffset - value) > 0.01) { _alongOffset = value; Raise(); } }
    }

    public AccentColorChoice AccentColor
    {
        get => _accentColor;
        set { if (_accentColor != value) { _accentColor = value; Raise(); } }
    }

    public ResetTimeFormat ResetTimeFormat
    {
        get => _resetTimeFormat;
        set { if (_resetTimeFormat != value) { _resetTimeFormat = value; Raise(); } }
    }

    public void Adopt(ScreenInfo screen)
    {
        ScreenSize = screen.Frame.Size;
        ScreenUsableSize = screen.WorkArea.Size;
    }

    public double ContentInset => 0;
    public double Flare => NotchLayout.CurlRadius;
    public bool IsFlushWithHardware => false;
    public double DrawnCornerRadius => NotchLayout.CornerRadius;
    public double OrbMergeScale => NotchLayout.OrbMergeScale;
    public double OrbArcRadius => NotchLayout.OrbArcRadius;
    public bool OrbHugsCorner => false;
    public double EndSpread => 0;
    public double EndSpreadFor(int cellCount) => 0;

    public double OrbAlong => ShapeLength;
    public double OrbInset => ContentInset + NotchLayout.OrbInsetFromEdge;
    public Size OrbArcOffset => new(0, 0);

    public Point[] OrbHandlePoints =>
        [new Point(OrbAlong, OrbInset)];

    public bool IsOnOrbHandle(double along, double across)
    {
        var radius = NotchLayout.OrbHotZone / 2;
        return OrbHandlePoints.Any(p =>
            Math.Sqrt(Math.Pow(along - p.X, 2) + Math.Pow(across - p.Y, 2)) <= radius);
    }

    public double TooltipInset =>
        ContentInset + NotchLayout.BodyDepth(Edge) + NotchLayout.TailGap;

    public double BodyLength =>
        NotchLayout.BodyLength(Snapshots.Count, Edge);

    public double RingCenter(int index) =>
        NotchLayout.RingCenter(index, Edge, Flare);

    public ActivitySummary? Activity(string providerId)
    {
        _sessions.TryGetValue(providerId, out var live);
        return ActivitySummary.From(live ?? []);
    }

    public ProviderSnapshot? HoveredSnapshot =>
        HoveredIndex is { } i && i >= 0 && i < Snapshots.Count ? Snapshots[i] : null;

    public double ShapeLength => ShapeLengthFor(Snapshots.Count);
    public Size PanelSize => PanelSizeFor(Snapshots.Count);
    public NotchPlacement Placement => new(Edge, PanelSize);
    public double Slack => SlackFor(Snapshots.Count);

    public double SlackFor(int cellCount) =>
        NotchLayout.Slack(Edge, MaxCardHeightFor(cellCount));

    public int SessionCap => SessionCapFor(Snapshots.Count);

    public int SessionCapFor(int cellCount)
    {
        if (ScreenSize == default) return NotchLayout.DefaultSessionCap;
        return NotchLayout.SessionsFitting(CardBudget(cellCount), NotchLayout.MaxWindowCount);
    }

    public double MaxCardHeightFor(int cellCount) =>
        NotchLayout.MaxCardHeight(SessionCapFor(cellCount));

    private double CardBudget(int cellCount)
    {
        if (Edge.IsVertical())
        {
            return ScreenSize.Height - ShapeLengthFor(cellCount) - 2 * NotchLayout.CardCorner;
        }
        return ScreenUsableSize.Height
            - ContentInset
            - NotchLayout.BodyDepth(Edge)
            - NotchLayout.TailLength
            - NotchLayout.TailGap;
    }

    public double NotchLength => IsExpanded ? ShapeLength : NotchLayout.PillHeight;
    public double NotchDepth => IsExpanded ? ContentInset + NotchLayout.BodyDepth(Edge) : NotchLayout.PillWidth;
    public double RestingLength => NotchLayout.PillHeight;
    public double RestingDepth => NotchLayout.PillWidth;

    public Size NotchSize => NotchPlacement.PanelSizeFor(Edge, NotchLength, NotchDepth);

    public double NotchLeadingInset => Slack + (ShapeLength - NotchLength) / 2;

    public double ShapeLengthFor(int cellCount) =>
        NotchLayout.ShapeLength(cellCount, Edge, Flare);

    public Size PanelSizeFor(int cellCount)
    {
        var card = MaxCardHeightFor(cellCount);
        return NotchPlacement.PanelSizeFor(
            Edge,
            ShapeLengthFor(cellCount) + 2 * NotchLayout.Slack(Edge, card),
            ContentInset + NotchLayout.TooltipDepth(Edge, card) + NotchLayout.BodyDepth(Edge));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
