using System.Globalization;
using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal static class ClaudeUsage
{
    public static List<LimitWindow> LimitWindows(JsonElement root)
    {
        var windows = new List<LimitWindow>();
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var limit in limits.EnumerateArray())
            {
                if (!limit.TryGetProperty("kind", out var kindEl)) continue;
                if (!TryDate(limit, out var resetsAt)) continue;
                var kind = kindEl.GetString() ?? "";
                var percent = Number(limit, "percent") ?? Number(limit, "utilization") ?? 0;
                windows.Add(new LimitWindow(kind, Label(kind), percent / 100, ResetsAt: resetsAt));
            }
        }

        Merge(root, "five_hour", "fiveHour", "session", "Current session");
        Merge(root, "seven_day", "sevenDay", "weekly_all", "All models");
        windows.Sort(DisplayOrder);
        return windows;

        void Merge(JsonElement r, string snake, string camel, string id, string label)
        {
            if (windows.Any(w => w.Id == id)) return;
            if (!r.TryGetProperty(snake, out var win) && !r.TryGetProperty(camel, out win)) return;
            if (win.ValueKind == JsonValueKind.Null) return;
            if (!TryDate(win, out var resetsAt)) return;
            var util = Number(win, "utilization") ?? Number(win, "percent") ?? 0;
            windows.Add(new LimitWindow(id, label, util / 100, ResetsAt: resetsAt));
        }
    }

    public static string Label(string kind) => kind switch
    {
        "session" => "Current session",
        "weekly_all" => "All models",
        "weekly_opus" => "Opus",
        "weekly_sonnet" => "Sonnet",
        _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(kind.Replace("weekly_", "").Replace('_', ' '))
    };

    private static int DisplayOrder(LimitWindow a, LimitWindow b)
    {
        int Rank(string id) => id switch { "session" => 0, "weekly_all" => 1, _ => 2 };
        var cmp = Rank(a.Id).CompareTo(Rank(b.Id));
        return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
    }

    private static double? Number(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var n)) return null;
        if (n.ValueKind == JsonValueKind.Number) return n.GetDouble();
        if (n.ValueKind == JsonValueKind.String &&
            double.TryParse(n.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    internal static bool TryDate(JsonElement el, out DateTime date) =>
        TryDate(el, "resets_at", out date) || TryDate(el, "resetsAt", out date);

    private static bool TryDate(JsonElement el, string name, out DateTime date)
    {
        date = default;
        if (!el.TryGetProperty(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (v.ValueKind == JsonValueKind.Number)
        {
            var n = v.GetDouble();
            if (n <= 0) return false;
            date = n > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)n).LocalDateTime
                : DateTimeOffset.FromUnixTimeSeconds((long)n).LocalDateTime;
            return true;
        }
        if (v.ValueKind != JsonValueKind.String) return false;
        var text = v.GetString();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dto) ||
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
        {
            date = dto.LocalDateTime;
            return true;
        }
        return false;
    }
}
