using QuotaArc.Model;
using QuotaArc.Notch;
using QuotaArc.Sessions;
using QuotaArc.Settings;

namespace QuotaArc.Overlay;

internal sealed class NotchFleet
{
    private readonly List<NotchWindowController> _controllers = [];
    private NotchScreenScope _scope;
    private NotchEdge _edge;
    private NotchVisibility _visibility = NotchVisibility.OnHover;
    private DisplayPreference _displayPreference = DisplayPreference.FollowActiveWindow;
    private ResetTimeFormat _resetTimeFormat = ResetTimeFormat.Automatic;
    private AccentColorChoice _accentColor = AccentColorChoice.System;
    private double _alongOffset;
    private List<ProviderSnapshot> _snapshots = [];
    private HashSet<string> _refreshing = [];
    private Dictionary<string, List<AgentSession>> _sessions = [];
    private bool _hasShown;

    public Action? OnRefresh { get; set; }
    public Action<string>? OnRefreshProvider { get; set; }
    public Action? OnUnfold { get; set; }
    public Action? OnOpenSettings { get; set; }
    public Action? OnQuit { get; set; }
    public Action<double>? OnReposition { get; set; }

    public IReadOnlyDictionary<string, List<AgentSession>> Sessions => _sessions;
    public NotchWindowController Primary => _controllers.FirstOrDefault() ?? MakeController(null);

    public NotchFleet(NotchScreenScope scope, NotchEdge edge)
    {
        _scope = scope;
        _edge = edge;
    }

    public void Show()
    {
        _hasShown = true;
        Reconcile();
        SystemEventsScreen.DisplaySettingsChanged += OnDisplaysChanged;
    }

    public void Stop()
    {
        SystemEventsScreen.DisplaySettingsChanged -= OnDisplaysChanged;
        foreach (var controller in _controllers) controller.Stop();
        _controllers.Clear();
    }

    public void Apply(NotchScreenScope scope)
    {
        _scope = scope;
        if (_hasShown) Reconcile();
    }

    public void Apply(NotchEdge edge)
    {
        _edge = edge;
        foreach (var controller in _controllers)
            controller.ApplyEdge(edge);
    }

    public void Apply(NotchVisibility visibility)
    {
        _visibility = visibility;
        foreach (var controller in _controllers)
            controller.Apply(visibility);
    }

    public void Apply(DisplayPreference preference)
    {
        _displayPreference = preference;
        if (_hasShown) Reconcile();
    }

    public void Apply(ResetTimeFormat format)
    {
        _resetTimeFormat = format;
        foreach (var controller in _controllers)
            controller.Model.ResetTimeFormat = format;
    }

    public void Apply(AccentColorChoice accent)
    {
        _accentColor = accent;
        foreach (var controller in _controllers)
            controller.Model.AccentColor = accent;
    }

    public void ApplyAlongOffset(double offset)
    {
        _alongOffset = offset;
        foreach (var controller in _controllers)
            controller.ApplyAlongOffset(offset);
    }

    public void SetSnapshots(IReadOnlyList<ProviderSnapshot> snapshots)
    {
        _snapshots = [.. snapshots];
        var now = DateTime.Now;
        foreach (var controller in _controllers)
        {
            controller.Model.Snapshots = _snapshots;
            controller.Model.Now = now;
        }
    }

    public void SetRefreshing(IEnumerable<string> ids)
    {
        _refreshing = [.. ids];
        foreach (var controller in _controllers)
            controller.Model.Refreshing = _refreshing;
    }

    public void SetSessions(Dictionary<string, List<AgentSession>> sessions)
    {
        _sessions = sessions;
        var now = DateTime.Now;
        foreach (var controller in _controllers)
        {
            controller.Model.Sessions = sessions;
            controller.Model.Now = now;
        }
    }

    public void Peek(double seconds)
    {
        foreach (var controller in _controllers)
            controller.Peek(seconds);
    }

    public void RevealNotch()
    {
        foreach (var controller in _controllers)
            controller.RevealNotch();
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => Reconcile();

    private void Reconcile()
    {
        var desired = DesiredScreens();
        foreach (var controller in _controllers) controller.Stop();
        _controllers.Clear();
        foreach (var screen in desired)
            _controllers.Add(MakeController(screen));
        if (_controllers.Count == 0)
            _controllers.Add(MakeController(null));
    }

    private List<ScreenInfo> DesiredScreens()
    {
        if (_scope == NotchScreenScope.AllDisplays)
            return [.. ScreenQuery.All()];
        return [ScreenQuery.Preferred(_displayPreference)];
    }

    private NotchWindowController MakeController(ScreenInfo? screen)
    {
        var controller = new NotchWindowController
        {
            AssignedScreen = screen,
            DisplayPreference = _displayPreference,
            OnRefresh = () => OnRefresh?.Invoke(),
            OnRefreshProvider = id => OnRefreshProvider?.Invoke(id),
            OnUnfold = () => OnUnfold?.Invoke(),
            OnOpenSettings = () => OnOpenSettings?.Invoke(),
            OnQuit = () => OnQuit?.Invoke(),
            OnReposition = offset => OnReposition?.Invoke(offset)
        };
        controller.Model.Edge = _edge;
        controller.Model.AlongOffset = _alongOffset;
        controller.Model.ResetTimeFormat = _resetTimeFormat;
        controller.Model.AccentColor = _accentColor;
        controller.Model.Snapshots = _snapshots;
        controller.Model.Refreshing = _refreshing;
        controller.Model.Sessions = _sessions;
        controller.Apply(_visibility);
        controller.Show();
        return controller;
    }
}
