using System.Globalization;
using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal static class AntigravityUsage
{
    public static List<LimitWindow> Windows(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var buckets = new List<JsonElement>();
            if (root.TryGetProperty("quotaGroups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groups.EnumerateArray())
                {
                    if (g.TryGetProperty("buckets", out var bs) && bs.ValueKind == JsonValueKind.Array)
                        buckets.AddRange(bs.EnumerateArray());
                }
            }
            if (root.TryGetProperty("buckets", out var top) && top.ValueKind == JsonValueKind.Array)
                buckets.AddRange(top.EnumerateArray());

            var windows = new List<LimitWindow>();
            foreach (var bucket in buckets)
            {
                if (!TryDouble(bucket, "limit", out var limit) || limit <= 0) continue;
                if (!TryDouble(bucket, "used", out var used) || used < 0 || used > limit * 1.5) continue;
                var label = Str(bucket, "displayName") ?? Str(bucket, "name") ?? "Usage";
                DateTime? resets = null;
                if (Str(bucket, "resetTime") is { } rt)
                    resets = AntigravityCredentials.Parse(rt);
                windows.Add(new LimitWindow(Str(bucket, "name") ?? label, label, used / limit, ResetsAt: resets));
            }
            return windows;
        }
        catch { return []; }
    }

    public static string Tier(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("currentTier", out var cur) && Str(cur, "name") is { } n)
                return n;
            if (root.TryGetProperty("allowedTiers", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in allowed.EnumerateArray())
                {
                    if (t.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True)
                        return Str(t, "name") ?? "Gemini";
                }
                if (allowed.GetArrayLength() > 0)
                    return Str(allowed[0], "name") ?? "Gemini";
            }
        }
        catch { /* ignore */ }
        return "Gemini";
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number) return false;
        value = n.GetDouble();
        return true;
    }
}

internal static class AntigravityActivity
{
    public static string TranscriptRoot =>
        Path.Combine(ClaudeProfile.Home, ".gemini", "antigravity", "brain");

    public static (int RequestsToday, DateTime? Last) Read(string? root = null, DateTime? now = null)
    {
        root ??= TranscriptRoot;
        now ??= DateTime.Now;
        if (!Directory.Exists(root)) return (0, null);
        var today = 0;
        DateTime? latest = null;
        try
        {
            foreach (var trajectory in Directory.GetDirectories(root))
            {
                var transcript = Path.Combine(trajectory, ".system_generated", "logs", "transcript.jsonl");
                if (!File.Exists(transcript)) continue;
                foreach (var line in File.ReadLines(transcript))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var src = doc.RootElement.TryGetProperty("source", out var s) ? s.GetString() : null;
                        if (src != "MODEL") continue;
                        var created = doc.RootElement.TryGetProperty("created_at", out var c) ? c.GetString() : null;
                        if (created is null || !DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var at)) continue;
                        var local = at.LocalDateTime;
                        if (latest is null || local > latest) latest = local;
                        if (local.Date == now.Value.Date) today++;
                    }
                    catch { /* skip line */ }
                }
            }
        }
        catch { /* ignore */ }
        return (today, latest);
    }
}
