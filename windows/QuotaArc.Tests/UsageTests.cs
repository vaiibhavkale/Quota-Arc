using QuotaArc.Model;
using QuotaArc.Providers;

namespace QuotaArc.Tests;

public class UsageBandTests
{
    [Fact]
    public void BandsMatchTheDesignFrame()
    {
        Assert.Equal(UsageBand.Ample, UsageBands.Band(0.21));
        Assert.Equal(UsageBand.Watch, UsageBands.Band(0.52));
        Assert.Equal(UsageBand.Critical, UsageBands.Band(0.73));
    }

    [Fact]
    public void Boundaries()
    {
        Assert.Equal(UsageBand.Ample, UsageBands.Band(0.0));
        Assert.Equal(UsageBand.Ample, UsageBands.Band(0.4999));
        Assert.Equal(UsageBand.Watch, UsageBands.Band(0.50));
        Assert.Equal(UsageBand.Watch, UsageBands.Band(0.6999));
        Assert.Equal(UsageBand.Critical, UsageBands.Band(0.70));
        Assert.Equal(UsageBand.Critical, UsageBands.Band(0.9999));
        Assert.Equal(UsageBand.Exhausted, UsageBands.Band(1.0));
        Assert.Equal(UsageBand.Exhausted, UsageBands.Band(1.4));
    }
}

public class ResetCopyTests
{
    private static readonly DateTime Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).LocalDateTime;

    [Fact]
    public void RelativeUnderAnHour() =>
        Assert.Equal("Resets in 51 min", ResetCopy.Text(Now.AddMinutes(51), Now));

    [Fact]
    public void RoundsToTheNearestMinute()
    {
        Assert.Equal("Resets in 50 min", ResetCopy.Text(Now.AddMinutes(50).AddSeconds(20), Now));
        Assert.Equal("Resets in 51 min", ResetCopy.Text(Now.AddMinutes(50).AddSeconds(40), Now));
    }

    [Fact]
    public void SwitchesToAbsoluteAtSixtyMinutes()
    {
        var atTheEdge = ResetCopy.Text(Now.AddHours(1), Now);
        Assert.DoesNotContain("min", atTheEdge);
        Assert.StartsWith("Resets ", atTheEdge);
        Assert.Equal("Resets in 59 min", ResetCopy.Text(Now.AddMinutes(59).AddSeconds(20), Now));
        var rounding = ResetCopy.Text(Now.AddMinutes(59).AddSeconds(40), Now);
        Assert.DoesNotContain("min", rounding);
    }

    [Fact]
    public void RemainingFormatUsesHoursAndMinutes()
    {
        var text = ResetCopy.Text(Now.AddHours(3).AddMinutes(20), Now, format: ResetTimeFormat.Remaining);
        Assert.Equal("Resets in 3h 20m", text);
    }
}

public class WindowSummaryTests
{
    private static LimitWindow Window(double fraction) =>
        new("w", "Monthly limit", fraction);

    [Fact]
    public void ShowsBothEndsOfTheSameFigure() =>
        Assert.Equal("12% Used · 88% left", Window(0.12).Summary);

    [Fact]
    public void AnOverspentLimitNeverGoesNegative() =>
        Assert.Equal("104% Used · 0% left", Window(1.04).Summary);

    [Fact]
    public void CountsAreUntouched()
    {
        Assert.Equal("8 used", new LimitWindow("w", "Requests", Used: 8).Summary);
        Assert.Equal("3 left", new LimitWindow("w", "Requests", Remaining: 3).Summary);
    }
}

public class ClaudeBackoffTests
{
    [Fact]
    public void FloorIsOneMinuteEvenWhenRetryAfterIsZero() =>
        Assert.Equal(60, ClaudeOAuthProvider.Backoff(0, 0));

    [Fact]
    public void DoublesThenCaps()
    {
        Assert.Equal(120, ClaudeOAuthProvider.Backoff(1, 0));
        Assert.Equal(15 * 60, ClaudeOAuthProvider.Backoff(4, 0));
        Assert.Equal(15 * 60, ClaudeOAuthProvider.Backoff(8, 0));
    }
}

public class UsageStoreScheduleTests
{
    [Fact]
    public void PollsHardWhileBusy() =>
        Assert.True(UsageStore.ShouldRefresh(true, 1, 300));

    [Fact]
    public void WaitsOutIdleIntervalWhenQuiet()
    {
        Assert.False(UsageStore.ShouldRefresh(false, 10, 300));
        Assert.True(UsageStore.ShouldRefresh(false, 300, 300));
    }
}

public class UsageStoreUnfoldTests
{
    // The notch unfolding is the one moment someone is looking, so a ring
    // stuck on "Waiting for the first reading..." matters most right here —
    // see UsageStore.RefreshIfStaleAsync, and the same fix on Mac.
    [Fact]
    public void RefreshesImmediatelyWhenARingHasNoReading() =>
        Assert.True(UsageStore.ShouldRefreshOnUnfold(sinceLastAttempt: 1, minSeconds: 15, awaitingFirstReading: true));

    [Fact]
    public void OtherwiseWaitsOutTheCooldown()
    {
        Assert.False(UsageStore.ShouldRefreshOnUnfold(sinceLastAttempt: 5, minSeconds: 15, awaitingFirstReading: false));
        Assert.True(UsageStore.ShouldRefreshOnUnfold(sinceLastAttempt: 15, minSeconds: 15, awaitingFirstReading: false));
    }
}
