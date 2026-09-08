using System.Globalization;
using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal static class CursorUsage
{
    public static List<LimitWindow> WindowsFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        DateTime? resetsAt = null;
        if (root.TryGetProperty("billingCycleEnd", out var end) &&
            DateTimeOffset.TryParse(end.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dto))
            resetsAt = dto.LocalDateTime;

        var usage = root.TryGetProperty("individualUsage", out var u) ? u : default;
        var plan = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("plan", out var p) ? p : default;

        var windows = new List<LimitWindow>();
        if (Percent(plan, "totalPercentUsed") is { } total)
            windows.Add(new LimitWindow("included", "Included usage", total, ResetsAt: resetsAt));
        if (Percent(plan, "apiPercentUsed") is { } api && api > 0)
            windows.Add(new LimitWindow("api", "API usage", api, ResetsAt: resetsAt));
        if (SpendWindow(usage, "onDemand", "on_demand", "On demand", resetsAt) is { } od)
            windows.Add(od);

        if (windows.Count > 0) return windows;

        var membership = root.TryGetProperty("membershipType", out var m) ? m.GetString() ?? "this" : "this";
        if (root.TryGetProperty("isUnlimited", out var unl) && unl.ValueKind == JsonValueKind.True)
            throw UsageProviderException.NothingMetered($"Unlimited on the {membership} plan — nothing to meter");
        throw UsageProviderException.NothingMetered($"The {membership} plan has nothing for Cursor to meter yet");
    }

    private static LimitWindow? SpendWindow(JsonElement usage, string key, string id, string label, DateTime? resetsAt)
    {
        if (usage.ValueKind != JsonValueKind.Object || !usage.TryGetProperty(key, out var bucket))
            return null;
        if (!bucket.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.True)
            return null;
        if (!TryDouble(bucket, "limit", out var limit) || limit <= 0) return null;
        if (!TryDouble(bucket, "used", out var used)) return null;
        return new LimitWindow(id, label, used / limit, ResetsAt: resetsAt);
    }

    private static double? Percent(JsonElement plan, string name)
    {
        if (plan.ValueKind != JsonValueKind.Object) return null;
        return TryDouble(plan, name, out var n) ? n / 100 : null;
    }

    private static bool TryDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number) return false;
        value = n.GetDouble();
        return true;
    }
}
