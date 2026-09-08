using System.Text.Json;
using QuotaArc.Model;
using QuotaArc.Providers;

namespace QuotaArc.Tests;

public class AntigravityBridgeTests
{
    private const string Sample = """
    {
      "response": {
        "groups": [
          {
            "displayName": "Gemini Models",
            "buckets": [
              {
                "bucketId": "gemini-weekly",
                "displayName": "Weekly Limit Remaining",
                "remainingFraction": 1,
                "resetTime": "2026-09-15T10:25:53Z"
              },
              {
                "bucketId": "gemini-5h",
                "displayName": "Five Hour Limit Remaining",
                "remainingFraction": 0.75,
                "resetTime": "2026-09-08T15:25:53Z"
              }
            ]
          },
          {
            "displayName": "Claude and GPT models",
            "buckets": [
              {
                "bucketId": "3p-weekly",
                "displayName": "Weekly Limit Remaining",
                "remainingFraction": 0.706568,
                "resetTime": "2026-09-14T04:47:11Z"
              }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void InvertsRemainingFractionIntoUsed()
    {
        using var doc = JsonDocument.Parse(Sample);
        var windows = AntigravityBridge.WindowsFromBridge(doc.RootElement);
        var fiveHour = windows.First(w => w.Id == "gemini-5h");
        Assert.Equal(0.25, fiveHour.UsedFraction ?? -1, 3);
        var weekly = windows.First(w => w.Id == "3p-weekly");
        Assert.Equal(0.293432, weekly.UsedFraction ?? -1, 3);
        Assert.Equal("3p-weekly", AntigravityBridge.HeadlineId(windows));
    }

    [Fact]
    public void RingFollowsThePoolThatIsActuallyUsed()
    {
        LimitWindow W(string id, double used) => new(id, id, used);
        var windows = new[]
        {
            W("gemini-5h", 0),
            W("3p-5h", 0),
            W("gemini-weekly", 0),
            W("3p-weekly", 0.29),
        };
        Assert.Equal("3p-weekly", AntigravityBridge.HeadlineId(windows));
    }

    [Fact]
    public void RingStaysOnTheHigherPoolWhenGeminiIsAlsoUsed()
    {
        LimitWindow W(string id, double used) => new(id, id, used);
        var windows = new[]
        {
            W("gemini-5h", 0.40),
            W("3p-weekly", 0.29),
        };
        Assert.Equal("gemini-5h", AntigravityBridge.HeadlineId(windows));
    }

    [Fact]
    public void ParsesCsrfTokenFromCommandLine()
    {
        const string line =
            "\"c:\\server\\language_server_windows_x64.exe\" --csrf_token abc-123 --extension_server_port 55431";
        Assert.True(AntigravityBridge.TryParseCommandLine(line, out var token));
        Assert.Equal("abc-123", token);
    }
}
