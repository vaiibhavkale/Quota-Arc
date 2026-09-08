using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal static class CodexUsage
{
    public static List<LimitWindow> WindowsFrom(byte[] data, DateTime? now = null)
    {
        now ??= DateTime.Now;
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (!root.TryGetProperty("rate_limit", out var rate))
            throw UsageProviderException.NothingMetered("Codex reported no usage windows");

        var windows = new List<LimitWindow>();
        foreach (var (id, key) in new[] { ("primary", "primary_window"), ("secondary", "secondary_window") })
        {
            if (!rate.TryGetProperty(key, out var win) || win.ValueKind != JsonValueKind.Object)
                continue;
            if (!win.TryGetProperty("used_percent", out var pct) || pct.ValueKind != JsonValueKind.Number)
                throw UsageProviderException.BadResponse(0);
            DateTime? resetsAt = null;
            if (win.TryGetProperty("reset_at", out var at) && at.ValueKind == JsonValueKind.Number)
                resetsAt = DateTimeOffset.FromUnixTimeSeconds((long)at.GetDouble()).LocalDateTime;
            else if (win.TryGetProperty("reset_after_seconds", out var after) && after.ValueKind == JsonValueKind.Number)
                resetsAt = now.Value.AddSeconds(after.GetDouble());
            var seconds = win.TryGetProperty("limit_window_seconds", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble() : 0;
            windows.Add(new LimitWindow(id, Label(seconds, id), pct.GetDouble() / 100, ResetsAt: resetsAt));
        }
        if (windows.Count == 0)
            throw UsageProviderException.NothingMetered("Codex reported no usage windows");
        return windows;
    }

    public static string Label(double windowSeconds, string fallback)
    {
        if (windowSeconds <= 0)
            return fallback == "primary" ? "Current session" : "Longer window";
        var minutes = windowSeconds / 60;
        if (minutes < 60) return $"{(int)minutes}m limit";
        if (minutes < 60 * 24) return $"{(int)(minutes / 60)}h limit";
        var days = (int)Math.Round(minutes / (60 * 24));
        return days switch
        {
            7 => "Weekly limit",
            30 => "Monthly limit",
            _ => $"{days}d limit"
        };
    }
}
