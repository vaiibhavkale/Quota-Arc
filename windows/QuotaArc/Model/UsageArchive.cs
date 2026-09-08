using System.Text.Json;
using System.Text.Json.Serialization;
using QuotaArc.Providers;

namespace QuotaArc.Model;

internal sealed class UsageArchive
{
    private const string Key = "lastGoodReadings";
    private const string BackoffKey = "backoffUntil";
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DateTime? LoadBackoffUntil(string providerId = "claude")
    {
        var raw = AppSettings.Get(BackoffKeyFor(providerId));
        if (string.IsNullOrEmpty(raw) || !DateTime.TryParse(raw, out var date) || date <= DateTime.Now)
            return null;
        return date;
    }

    public void SaveBackoffUntil(DateTime? date, string providerId = "claude")
    {
        var key = BackoffKeyFor(providerId);
        if (date is { } d) AppSettings.Set(key, d.ToString("o"));
        else AppSettings.Remove(key);
    }

    private static string BackoffKeyFor(string providerId) =>
        providerId == "claude" ? BackoffKey : $"{BackoffKey}.{providerId}";

    public Dictionary<string, (ProviderSnapshot Snapshot, DateTime FetchedAt)> Load()
    {
        var raw = AppSettings.Get(Key);
        if (string.IsNullOrEmpty(raw)) return [];
        try
        {
            var entries = JsonSerializer.Deserialize<List<Entry>>(raw, _json) ?? [];
            var result = new Dictionary<string, (ProviderSnapshot, DateTime)>();
            foreach (var entry in entries)
            {
                if (entry.Id == "codex" &&
                    entry.Windows.Any(w => w.Id is not "primary" and not "secondary"))
                    continue;
                var snapshot = new ProviderSnapshot(
                    entry.Id, entry.DisplayName, entry.Glyph, entry.Fidelity,
                    new ProviderStatus.Stale(entry.FetchedAt),
                    entry.Windows, entry.HeadlineId);
                result[entry.Id] = (snapshot, entry.FetchedAt);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    public void Save(Dictionary<string, (ProviderSnapshot Snapshot, DateTime FetchedAt)> readings)
    {
        var entries = readings.Values.Select(r => new Entry(
            r.Snapshot.Id, r.Snapshot.DisplayName, r.Snapshot.Glyph, r.Snapshot.Fidelity,
            r.Snapshot.Windows.ToList(), r.FetchedAt, r.Snapshot.HeadlineId)).ToList();
        AppSettings.Set(Key, JsonSerializer.Serialize(entries, _json));
    }

    public void Forget(string providerId)
    {
        var readings = Load();
        readings.Remove(providerId);
        Save(readings);
    }

    private sealed record Entry(
        string Id,
        string DisplayName,
        ProviderGlyph Glyph,
        Fidelity Fidelity,
        List<LimitWindow> Windows,
        DateTime FetchedAt,
        string? HeadlineId);
}
