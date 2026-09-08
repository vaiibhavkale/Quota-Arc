using System.Text.Json;
using QuotaArc.Model;
using QuotaArc.Providers;

namespace QuotaArc.Tests;

public class ClaudeUsageResponseTests
{
    private const string Live = """
    {
      "five_hour": { "utilization": 52.0, "resets_at": "2026-08-28T09:50:00.316290+00:00",
                     "limit_dollars": null, "used_dollars": null },
      "seven_day": { "utilization": 17.0, "resets_at": "2026-09-02T17:00:00.316321+00:00",
                     "limit_dollars": null },
      "seven_day_opus": null,
      "nimbus_quill": { "utilization": 0.0, "resets_at": null },
      "limits": [
        { "kind": "session", "group": "session", "percent": 52, "severity": "normal",
          "resets_at": "2026-08-28T09:50:00.316290+00:00", "scope": null, "is_active": true },
        { "kind": "weekly_all", "group": "weekly", "percent": 17, "severity": "normal",
          "resets_at": "2026-09-02T17:00:00.316321+00:00", "scope": null, "is_active": false }
      ]
    }
    """;

    [Fact]
    public void DecodesTheLiveShape()
    {
        var windows = Windows(Live);
        Assert.Equal(2, windows.Count);
        Assert.Equal("session", windows[0].Id);
        Assert.Equal("Current session", windows[0].Label);
        Assert.Equal(0.52, windows[0].UsedFraction ?? -1, 4);
        Assert.Equal("All models", windows[1].Label);
        Assert.Equal(0.17, windows[1].UsedFraction ?? -1, 4);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-28T09:50:00.316290+00:00").LocalDateTime,
            windows[0].ResetsAt);
    }

    [Fact]
    public void SessionSortsFirst()
    {
        const string reversed = """
        { "limits": [
            { "kind": "weekly_all", "percent": 17, "resets_at": "2026-09-02T17:00:00.316321+00:00" },
            { "kind": "session", "percent": 52, "resets_at": "2026-08-28T09:50:00.316290+00:00" } ] }
        """;
        Assert.Equal(new[] { "session", "weekly_all" }, Windows(reversed).Select(w => w.Id));
    }

    [Fact]
    public void DropsWindowsWithoutAResetTime()
    {
        const string json = """
        { "limits": [ { "kind": "session", "percent": 5, "resets_at": null } ],
          "five_hour": { "utilization": 5.0, "resets_at": null } }
        """;
        Assert.Empty(Windows(json));
    }

    [Fact]
    public void FallsBackToTheNamedWindows()
    {
        const string json = """
        { "five_hour": { "utilization": 48.0, "resets_at": "2026-08-28T09:50:00.316290+00:00" },
          "seven_day": { "utilization": 16.0, "resets_at": "2026-09-02T17:00:00.316321+00:00" } }
        """;
        Assert.Equal(new[] { "Current session", "All models" }, Windows(json).Select(w => w.Label));
        Assert.Equal(0.48, Windows(json)[0].UsedFraction ?? -1, 4);
    }

    [Fact]
    public void UnknownKindsGetAReadableLabel()
    {
        Assert.Equal("Opus", ClaudeUsage.Label("weekly_opus"));
        Assert.Equal("Cowork", ClaudeUsage.Label("weekly_cowork"));
    }

    private static IReadOnlyList<LimitWindow> Windows(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ClaudeUsage.LimitWindows(doc.RootElement);
    }
}

public class ClaudeDesktopHistoryTests
{
    [Fact]
    public void ReadsTheSamePercentsTheSettingsPageShows()
    {
        var now = new DateTime(2026, 9, 8, 14, 48, 0, DateTimeKind.Local);
        var start = now.AddMinutes(-8);
        var json = $$"""
        {
          "samples": [
            { "t": {{Ms(now.AddMinutes(-20))}}, "u": { "fh": 0, "sd": 10 } },
            { "t": {{Ms(start)}}, "u": { "fh": 9, "sd": 11 } }
          ]
        }
        """;

        var windows = ClaudeDesktop.WindowsFromHistory(json, now);
        Assert.NotNull(windows);
        Assert.Equal(2, windows.Count);
        Assert.Equal("Current session", windows[0].Label);
        Assert.Equal(0.09, windows[0].UsedFraction ?? -1, 4);
        Assert.Equal("All models", windows[1].Label);
        Assert.Equal(0.11, windows[1].UsedFraction ?? -1, 4);
        Assert.Null(windows[1].ResetsAt);
        Assert.Equal(start.AddHours(5), windows[0].ResetsAt);
    }

    [Fact]
    public void UsesTheLatestSampleNotAnOlderSpike()
    {
        var now = new DateTime(2026, 9, 8, 14, 48, 0, DateTimeKind.Local);
        var json = $$"""
        {
          "samples": [
            { "t": {{Ms(now.AddHours(-3))}}, "u": { "fh": 73, "sd": 7 } },
            { "t": {{Ms(now.AddMinutes(-1))}}, "u": { "fh": 9, "sd": 11 } }
          ]
        }
        """;

        var windows = ClaudeDesktop.WindowsFromHistory(json, now);
        Assert.Equal(0.09, windows![0].UsedFraction ?? -1, 4);
        Assert.Equal(0.11, windows[1].UsedFraction ?? -1, 4);
    }

    [Fact]
    public void ParsesTheOnDiskHistoryFile()
    {
        if (!File.Exists(ClaudeDesktop.HistoryPath)) return;
        var windows = ClaudeDesktop.WindowsFromHistory();
        Assert.NotNull(windows);
        Assert.Equal(2, windows.Count);
        Assert.InRange(windows[0].UsedFraction ?? -1, 0, 1.5);
        Assert.InRange(windows[1].UsedFraction ?? -1, 0, 1.5);
    }

    [Fact]
    public void DesktopTokenDecryptDoesNotThrow()
    {
        var creds = ClaudeDesktop.LoadOAuth();
        if (creds is null) return;
        Assert.False(string.IsNullOrWhiteSpace(creds.AccessToken));
    }

    private static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();
}
