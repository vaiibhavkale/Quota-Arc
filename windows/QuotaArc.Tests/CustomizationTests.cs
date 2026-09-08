using QuotaArc.Model;
using QuotaArc.Providers;
using QuotaArc.Sessions;
using QuotaArc.Settings;

namespace QuotaArc.Tests;

public class ProviderOrderTests
{
    private static List<string> Arrange(IReadOnlyList<string> ids, IReadOnlyList<string> order) =>
        ProviderOrder.Arrange(ids, order, id => id);

    [Fact]
    public void AnEmptyOrderLeavesTheBuiltInOrderAlone() =>
        Assert.Equal(["claude", "cursor", "codex"], Arrange(["claude", "cursor", "codex"], []));

    [Fact]
    public void ProvidersFollowTheStoredOrder() =>
        Assert.Equal(["codex", "claude", "cursor"],
            Arrange(["claude", "cursor", "codex"], ["codex", "claude", "cursor"]));

    [Fact]
    public void AProviderTheOrderHasNeverSeenStillAppears() =>
        Assert.Equal(["cursor", "claude", "opencode"],
            Arrange(["claude", "cursor", "opencode"], ["cursor", "claude"]));

    [Fact]
    public void AStoredIdWithNoProviderIsIgnored() =>
        Assert.Equal(["cursor", "claude"],
            Arrange(["claude", "cursor"], ["cursor", "claude-work", "claude"]));

    [Fact]
    public void AReconnectedProviderJoinsTheEndOfTheConnectedOnes() =>
        Assert.Equal(["claude", "codex", "glm", "gemini"],
            ProviderOrder.JoiningConnected("glm", ["glm", "claude", "codex", "gemini"],
                id => id is "claude" or "codex"));
}

public class ThresholdNotifierTests
{
    private readonly List<ThresholdAlert> _alerts = [];
    private HashSet<string> _muted = [];
    private readonly ThresholdNotifier _notifier;

    public ThresholdNotifierTests()
    {
        _notifier = new ThresholdNotifier(
            id => _muted.Contains(id),
            alert => _alerts.Add(alert));
    }

    private static ProviderSnapshot Snap(string id, string name, double fraction) =>
        new(id, name, ProviderGlyph.Claude, Fidelity.Official, new ProviderStatus.Ok(),
            [new LimitWindow("session", "Current session", fraction)], "session");

    [Fact]
    public void CrossingEightyAlertsOnce()
    {
        _notifier.Observe([Snap("claude", "Claude", 0.5)]);
        Assert.Empty(_alerts);
        _notifier.Observe([Snap("claude", "Claude", 0.82)]);
        _notifier.Observe([Snap("claude", "Claude", 0.91)]);
        Assert.Single(_alerts);
        Assert.Equal(80, _alerts[0].Threshold);
        Assert.Equal(82, _alerts[0].UsedPercent);
    }

    [Fact]
    public void CrossingHundredAfterEightyAlertsAgain()
    {
        _notifier.Observe([Snap("claude", "Claude", 0.85)]);
        _notifier.Observe([Snap("claude", "Claude", 1.02)]);
        Assert.Equal([80, 100], _alerts.Select(a => a.Threshold));
    }

    [Fact]
    public void ARolledOverWindowAlertsAgain()
    {
        _notifier.Observe([Snap("claude", "Claude", 0.9)]);
        _notifier.Observe([Snap("claude", "Claude", 0.1)]);
        Assert.Single(_alerts);
        _notifier.Observe([Snap("claude", "Claude", 0.84)]);
        Assert.Equal(2, _alerts.Count);
        Assert.Equal(80, _alerts[1].Threshold);
    }

    [Fact]
    public void MutedProvidersAreSilentButRemembered()
    {
        _muted = ["claude"];
        _notifier.Observe([Snap("claude", "Claude", 0.85)]);
        Assert.Empty(_alerts);
        _muted = [];
        _notifier.Observe([Snap("claude", "Claude", 0.86)]);
        Assert.Empty(_alerts);
        _notifier.Observe([Snap("claude", "Claude", 1.0)]);
        Assert.Equal([100], _alerts.Select(a => a.Threshold));
    }
}

public class SessionCompletionWatcherTests
{
    private static AgentSession Session(string id, AgentState state, DateTime? since = null) =>
        new(id, id, $"Terminal · {id}", state, null, since ?? DateTime.Now);

    [Fact]
    public void FirstReadingAnnouncesNothing()
    {
        var watcher = new SessionCompletionWatcher();
        Assert.Empty(watcher.Absorb(new Dictionary<string, List<AgentSession>>
        {
            ["claude"] = [Session("a", AgentState.Busy), Session("b", AgentState.Idle)]
        }));
    }

    [Fact]
    public void BusyToIdleIsFinished()
    {
        var watcher = new SessionCompletionWatcher();
        _ = watcher.Absorb(new Dictionary<string, List<AgentSession>> { ["claude"] = [Session("a", AgentState.Busy)] });
        var events = watcher.Absorb(new Dictionary<string, List<AgentSession>> { ["claude"] = [Session("a", AgentState.Idle)] });
        Assert.Single(events);
        Assert.Equal(SessionCompletionReason.Finished, events[0].Reason);
        Assert.Equal("claude", events[0].ProviderId);
    }

    [Fact]
    public void BusyToWaitingIsBlocked()
    {
        var watcher = new SessionCompletionWatcher();
        _ = watcher.Absorb(new Dictionary<string, List<AgentSession>> { ["claude"] = [Session("a", AgentState.Busy)] });
        var events = watcher.Absorb(new Dictionary<string, List<AgentSession>> { ["claude"] = [Session("a", AgentState.Waiting)] });
        Assert.Equal(SessionCompletionReason.Blocked, events[0].Reason);
    }

    [Fact]
    public void WaitingToIdleIsSilent()
    {
        var watcher = new SessionCompletionWatcher();
        _ = watcher.Absorb(new Dictionary<string, List<AgentSession>> { ["claude"] = [Session("a", AgentState.Waiting)] });
        Assert.Empty(watcher.Absorb(new Dictionary<string, List<AgentSession>> { ["claude"] = [Session("a", AgentState.Idle)] }));
    }
}

public class PercentTests
{
    [Fact]
    public void SubOnePercentDoesNotRoundToZero()
    {
        Assert.Equal("<0.1", Percent.Text(0.0004));
        Assert.Equal("0.3", Percent.Text(0.003));
        Assert.Equal("12", Percent.Text(0.12));
    }

    [Fact]
    public void HalvesKeepTheDashboardMaths()
    {
        var (used, left) = Percent.Halves(0.12);
        Assert.Equal("12", used);
        Assert.Equal("88", left);
    }
}

public class DisplayPreferenceTests
{
    [Fact]
    public void MissingRawFollowsTheActiveWindow() =>
        Assert.Equal(DisplayPreferenceKind.FollowActiveWindow, DisplayPreference.Parse(null).Kind);

    [Fact]
    public void ADeviceNamePinsTheNotch()
    {
        var pinned = DisplayPreference.Parse(@"\\.\DISPLAY2");
        Assert.Equal(DisplayPreferenceKind.Pinned, pinned.Kind);
        Assert.Equal(@"\\.\DISPLAY2", pinned.DeviceName);
    }
}

public class PeekDurationTests
{
    [Fact]
    public void SecondsMatchTheLabels()
    {
        Assert.Equal(3, PeekDuration.Brief.Seconds());
        Assert.Equal(5, PeekDuration.Standard.Seconds());
        Assert.Equal(10, PeekDuration.Long.Seconds());
    }
}

public class AppPresenceTests
{
    [Fact]
    public void DefaultIsTheTraySoClosingTheWindowDoesNotLoseTheApp()
    {
        Assert.Equal(AppPresence.Tray, AppPresenceInfo.Parse(null));
        Assert.True(AppPresence.Tray.WantsTray());
        Assert.True(AppPresence.Taskbar.WantsTray());
        Assert.False(AppPresence.Hidden.WantsTray());
    }
}