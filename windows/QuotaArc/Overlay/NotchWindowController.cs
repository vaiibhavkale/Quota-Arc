using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuotaArc;
using QuotaArc.Design;
using QuotaArc.Notch;
using QuotaArc.Settings;

namespace QuotaArc.Overlay;

internal sealed class NotchWindowController
{
    public NotchViewModel Model { get; } = new();
    public Action? OnRefresh { get; set; }
    public Action<string>? OnRefreshProvider { get; set; }
    public Action? OnUnfold { get; set; }
    public Action? OnOpenSettings { get; set; }
    public Action? OnQuit { get; set; }
    public Action<double>? OnReposition { get; set; }
    public ScreenInfo? AssignedScreen { get; set; }
    public DisplayPreference DisplayPreference { get; set; } = DisplayPreference.FollowActiveWindow;
    public NotchVisibility Visibility { get; private set; } = NotchVisibility.OnHover;

    private NotchWindow? _window;
    private readonly DispatcherTimer _clockTimer;
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _foldTimer;
    private DispatcherTimer? _peekTimer;
    private DateTime? _peekUntil;
    private Rect? _lastWorkArea;
    private int _edgeChange;
    private const double HoverGrace = 0.25;
    private const double FoldGrace = 0.45;

    public NotchWindowController()
    {
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => Model.Now = DateTime.Now;
        SystemEventsScreen.DisplaySettingsChanged += (_, _) => Relocate();
    }

    public void Show()
    {
        Relocate();
        CompositionTarget.Rendering += OnFrame;
        _clockTimer.Start();
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Model.Snapshots) or nameof(Model.HoveredIndex)
                or nameof(Model.IsExpanded) or nameof(Model.AlongOffset)
                or nameof(Model.AccentColor) or nameof(Model.ResetTimeFormat))
            {
                if (e.PropertyName is nameof(Model.Snapshots) or nameof(Model.AlongOffset))
                    Relocate(Model.Snapshots.Count);
                else
                    UpdateInteractiveRects();
            }
        };
    }

    public void Stop()
    {
        CompositionTarget.Rendering -= OnFrame;
        _clockTimer.Stop();
        _peekTimer?.Stop();
        _foldTimer?.Stop();
        _hoverTimer?.Stop();
        _window?.Close();
        _window = null;
    }

    public void Relocate(int? cellCount = null)
    {
        var screen = CurrentScreen();
        Model.Adopt(screen);
        var size = Model.PanelSizeFor(cellCount ?? Model.Snapshots.Count);
        var frame = NotchGeometry.PanelFrame(screen, size, Model.Edge, Model.AlongOffset, Model.Slack);
        _lastWorkArea = screen.WorkArea;

        if (_window is null)
        {
            _window = new NotchWindow(Model)
            {
                Clicked = HandleClick,
                RightClicked = ShowMenu,
                Nudged = HandleNudge
            };
            _window.Place(frame);
            _window.Show();
        }
        else
        {
            _window.Place(frame);
        }
        UpdateInteractiveRects();
    }

    public void Apply(NotchVisibility visibility)
    {
        Visibility = visibility;
        switch (visibility)
        {
            case NotchVisibility.AlwaysShow:
                _window?.Show();
                Model.IsAlwaysOn = true;
                Model.IsPinned = false;
                CancelFold();
                Model.IsExpanded = true;
                break;
            case NotchVisibility.OnHover:
                _window?.Show();
                Model.IsAlwaysOn = false;
                Model.IsPinned = false;
                Model.IsExpanded = false;
                Model.HoveredIndex = null;
                break;
            case NotchVisibility.Hidden:
                Model.IsAlwaysOn = false;
                Model.IsPinned = false;
                Model.IsExpanded = false;
                Model.HoveredIndex = null;
                _window?.Hide();
                break;
        }
        UpdateInteractiveRects();
    }

    public void Peek(double seconds)
    {
        if (Visibility == NotchVisibility.Hidden) return;
        _window?.Show();
        CancelFold();
        _peekTimer?.Stop();
        _peekUntil = DateTime.Now.AddSeconds(seconds);
        Model.IsExpanded = true;
        OnUnfold?.Invoke();
        UpdateInteractiveRects();
        _peekTimer = Once(seconds, () =>
        {
            _peekTimer = null;
            _peekUntil = null;
            if (Model.StaysOpen) return;
            if (_window is not null && LiveRect.Contains(_window.LocalCursor())) return;
            Model.IsExpanded = false;
            Model.HoveredIndex = null;
            UpdateInteractiveRects();
        });
    }

    public void RevealNotch()
    {
        _window?.Show();
        CancelFold();
        Model.IsExpanded = true;
        OnUnfold?.Invoke();
        UpdateInteractiveRects();
    }

    public void ApplyAlongOffset(double offset)
    {
        Model.AlongOffset = offset;
        Relocate();
    }

    private ScreenInfo CurrentScreen()
    {
        if (AssignedScreen is { } assigned) return assigned;
        return ScreenQuery.Preferred(DisplayPreference);
    }

    private void HandleNudge(Vector delta)
    {
        var amount = Model.Edge.IsVertical() ? delta.Y : delta.X;
        Model.AlongOffset += amount;
        Relocate();
        OnReposition?.Invoke(Model.AlongOffset);
    }

    public async void ApplyEdge(NotchEdge edge)
    {
        if (Model.Edge == edge) return;
        if (_window is null)
        {
            Model.Edge = edge;
            Relocate();
            return;
        }

        var wasOpen = Model.IsExpanded;
        Model.HoveredIndex = null;
        var change = ++_edgeChange;
        for (var i = 0; i < 8; i++)
        {
            _window.Opacity = 1 - i / 8.0;
            await Task.Delay(20);
            if (change != _edgeChange) return;
        }
        Model.Edge = edge;
        Model.IsExpanded = false;
        Relocate();
        _window.Surface.JumpToModel();
        _window.Opacity = 1;
        if (!wasOpen) return;
        await Task.Delay(50);
        if (change != _edgeChange) return;
        Model.IsExpanded = true;
        UpdateInteractiveRects();
    }

    public void TogglePinned()
    {
        if (Model.IsAlwaysOn) return;
        Model.IsPinned = !Model.IsPinned;
        if (Model.IsPinned)
        {
            CancelFold();
            Model.IsExpanded = true;
        }
        UpdateInteractiveRects();
    }

    private NotchPlacement Placement
    {
        get
        {
            if (_window is { Width: > 0, Height: > 0 } w)
                return new NotchPlacement(Model.Edge, new Size(w.Width, w.Height));
            return new NotchPlacement(Model.Edge, Model.PanelSize);
        }
    }

    private Rect NotchRect => Placement.Rect(Model.Slack, 0, Model.ShapeLength, Model.NotchDepth);

    private Rect PillRect
    {
        get
        {
            var length = Math.Max(Model.RestingLength, NotchLayout.PillHotZone);
            return Placement.Rect(
                Model.Slack + (Model.ShapeLength - length) / 2,
                0, length, Model.RestingDepth + NotchLayout.PillHotZone);
        }
    }

    private Rect HandleRect
    {
        get
        {
            var side = NotchLayout.OrbHotZone;
            Rect? box = null;
            foreach (var point in Model.OrbHandlePoints)
            {
                var centre = Placement.Point(Model.Slack + point.X, point.Y);
                var r = new Rect(centre.X - side / 2, centre.Y - side / 2, side, side);
                box = box is { } b ? Rect.Union(b, r) : r;
            }
            return box ?? default;
        }
    }

    private bool IsOverHandle(Point local)
    {
        var along = Placement.Along(local) - Model.Slack;
        var across = Placement.Across(local);
        return Model.IsOnOrbHandle(along, across);
    }

    private Rect LiveRect => Model.IsExpanded ? Rect.Union(NotchRect, HandleRect) : PillRect;

    private Rect? TooltipRect(int index)
    {
        if (index < 0 || index >= Model.Snapshots.Count) return null;
        var snapshot = Model.Snapshots[index];
        var cardHeight = NotchLayout.CardHeight(
            snapshot.Windows.Count,
            Model.Activity(snapshot.Id)?.Sessions.Count ?? 0,
            Model.SessionCap,
            snapshot.StatusMessage,
            snapshot.Block?.Summary(Model.Now));
        var cardAcross = Model.Edge.IsVertical() ? NotchLayout.CardWidth : cardHeight;
        var cardAlong = Model.Edge.IsVertical() ? cardHeight : NotchLayout.CardWidth;
        var centre = Model.Slack + Model.RingCenter(index);
        return Placement.Rect(
            centre - cardAlong / 2,
            Model.ContentInset + NotchLayout.BodyDepth(Model.Edge),
            cardAlong,
            NotchLayout.TailGap + NotchLayout.TailLength + cardAcross);
    }

    private void UpdateInteractiveRects()
    {
        if (_window is null) return;
        var local = _window.LocalCursor();
        var live = LiveRect;
        var over = live.Contains(local);
        if (Model.IsExpanded && Model.HoveredIndex is { } i && TooltipRect(i) is { } card)
            over = over || card.Contains(local);
        _window.SetClickThrough(!over);
        _window.Cursor = over && Model.IsExpanded ? Cursors.Hand : Cursors.Arrow;
    }

    private void OnFrame(object? sender, EventArgs e) => CursorMoved();

    private void CursorMoved()
    {
        FollowUsableAreaIfItMoved();
        if (_window is null || !_window.IsVisible) return;
        var local = _window.LocalCursor();
        var overTooltip = Model.HoveredIndex is { } hi
            && TooltipRect(hi) is { } card
            && Model.IsExpanded
            && card.Contains(local);
        SetExpanded(LiveRect.Contains(local) || overTooltip);

        int? target = null;
        if (Model.IsExpanded && NotchRect.Contains(local))
            target = CellIndex(Placement.Along(local));
        else if (Model.IsExpanded && Model.HoveredIndex is { } current &&
                 TooltipRect(current) is { } t && t.Contains(local))
            target = current;

        var overHandle = Model.IsExpanded && IsOverHandle(local);
        Model.IsHoveringSettings = overHandle;

        if (target is { } idx)
        {
            _hoverTimer?.Stop();
            _hoverTimer = null;
            Model.HoveredIndex = idx;
        }
        else if (Model.HoveredIndex is not null && _hoverTimer is null)
        {
            _hoverTimer = Once(HoverGrace, () =>
            {
                _hoverTimer = null;
                Model.HoveredIndex = null;
            });
        }

        UpdateInteractiveRects();
    }

    private void SetExpanded(bool wanted)
    {
        if (wanted)
        {
            CancelFold();
            if (!Model.IsExpanded)
            {
                Model.IsExpanded = true;
                OnUnfold?.Invoke();
            }
            return;
        }
        if (_peekUntil is { } until && until > DateTime.Now) return;
        if (!Model.IsExpanded || Model.StaysOpen || _foldTimer is not null) return;
        _foldTimer = Once(FoldGrace, () =>
        {
            _foldTimer = null;
            if (Model.StaysOpen) return;
            Model.IsExpanded = false;
            Model.HoveredIndex = null;
            UpdateInteractiveRects();
        });
    }

    private void CancelFold()
    {
        _foldTimer?.Stop();
        _foldTimer = null;
    }

    private void HandleClick()
    {
        if (_window is null || !Model.IsExpanded)
        {
            SetExpanded(true);
            return;
        }
        var local = _window.LocalCursor();
        if (IsOverHandle(local))
        {
            OnOpenSettings?.Invoke();
            return;
        }
        if (NotchRect.Contains(local) && CellIndex(Placement.Along(local)) is { } index
            && index >= 0 && index < Model.Snapshots.Count)
        {
            OnRefreshProvider?.Invoke(Model.Snapshots[index].Id);
            return;
        }
        TogglePinned();
    }

    private int? CellIndex(double along)
    {
        var pitch = NotchLayout.CellPitch(Model.Edge);
        for (var i = 0; i < Model.Snapshots.Count; i++)
        {
            var centre = Model.Slack + Model.RingCenter(i);
            if (Math.Abs(along - centre) <= pitch / 2) return i;
        }
        return null;
    }

    private void FollowUsableAreaIfItMoved()
    {
        var screen = CurrentScreen();
        if (_lastWorkArea is { } last && last == screen.WorkArea) return;
        Relocate();
    }

    private void ShowMenu()
    {
        if (_window is null) return;
        var menu = new ContextMenu { StaysOpen = false };
        var keep = new MenuItem
        {
            Header = "Keep open",
            IsCheckable = true,
            IsChecked = Model.StaysOpen,
            IsEnabled = !Model.IsAlwaysOn
        };
        keep.Click += (_, _) => TogglePinned();
        menu.Items.Add(keep);
        menu.Items.Add(new Separator());
        var refresh = new MenuItem { Header = "Refresh now" };
        refresh.Click += (_, _) => OnRefresh?.Invoke();
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = $"Quit {AppBranding.Name}" };
        quit.Click += (_, _) => OnQuit?.Invoke();
        menu.Items.Add(quit);
        menu.PlacementTarget = _window;
        menu.IsOpen = true;
    }

    private static DispatcherTimer Once(double seconds, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            action();
        };
        t.Start();
        return t;
    }
}

internal static class SystemEventsScreen
{
    public static event EventHandler? DisplaySettingsChanged
    {
        add => Microsoft.Win32.SystemEvents.DisplaySettingsChanged += value;
        remove => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= value;
    }
}
