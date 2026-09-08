using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

/// <summary>
/// Claude Desktop on Windows writes the same plan percentages the settings
/// page shows into %APPDATA%\Claude\plan-usage-history.json, and keeps the
/// account token in an OSCrypt cache. Either is enough to stop inventing numbers.
/// </summary>
internal static class ClaudeDesktop
{
    public static string AppData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");

    public static string HistoryPath => Path.Combine(AppData, "plan-usage-history.json");
    public static string ConfigPath => Path.Combine(AppData, "config.json");
    public static string LocalStatePath => Path.Combine(AppData, "Local State");

    public static bool IsPresent() => File.Exists(HistoryPath) || File.Exists(ConfigPath);

    public static DateTime? HistoryStamp() =>
        File.Exists(HistoryPath) ? File.GetLastWriteTimeUtc(HistoryPath) : null;

    public static ProviderSnapshot? SnapshotFromHistory(string displayName = "Claude")
    {
        var windows = WindowsFromHistory();
        if (windows is not { Count: > 0 }) return null;
        return new ProviderSnapshot(
            "claude", displayName, ProviderGlyph.Claude, Fidelity.Official,
            new ProviderStatus.Ok(), windows, "session");
    }

    public static List<LimitWindow>? WindowsFromHistory(string? json = null, DateTime? now = null)
    {
        json ??= File.Exists(HistoryPath) ? File.ReadAllText(HistoryPath) : null;
        if (string.IsNullOrWhiteSpace(json)) return null;
        now ??= DateTime.Now;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("samples", out var samples) ||
                samples.ValueKind != JsonValueKind.Array ||
                samples.GetArrayLength() == 0)
                return null;

            var list = new List<(DateTime At, int Fh, int Sd)>();
            foreach (var sample in samples.EnumerateArray())
            {
                if (!sample.TryGetProperty("t", out var tEl) || tEl.ValueKind != JsonValueKind.Number)
                    continue;
                if (!sample.TryGetProperty("u", out var u) || u.ValueKind != JsonValueKind.Object)
                    continue;
                if (!TryInt(u, "fh", out var fh) || !TryInt(u, "sd", out var sd))
                    continue;
                var stamp = tEl.TryGetInt64(out var ms) ? ms : (long)tEl.GetDouble();
                list.Add((FromUnix(stamp), fh, sd));
            }
            if (list.Count == 0) return null;

            var latest = list[^1];
            var sessionReset = InferFiveHourReset(list, now.Value);

            return
            [
                new LimitWindow("session", "Current session", latest.Fh / 100.0, ResetsAt: sessionReset),
                new LimitWindow("weekly_all", "All models", latest.Sd / 100.0)
            ];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The five-hour window starts at the last sample where usage left 0.
    /// Claude Desktop only records a point every few minutes, so this can be
    /// a little late, but it is the same clock the settings page is on.
    /// </summary>
    public static DateTime? InferFiveHourReset(IReadOnlyList<(DateTime At, int Fh, int Sd)> samples, DateTime now)
    {
        DateTime? start = null;
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i - 1].Fh == 0 && samples[i].Fh > 0)
                start = samples[i].At;
            else if (samples[i].Fh == 0)
                start = null;
        }
        if (start is null)
        {
            var last = samples[^1];
            if (last.Fh == 0) return last.At;
            start = last.At;
        }
        var reset = start.Value.AddHours(5);
        return reset > now ? reset : now;
    }

    public static DateTime? InferWeeklyReset(IReadOnlyList<(DateTime At, int Fh, int Sd)> samples, DateTime now)
    {
        DateTime? drop = null;
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i].Sd + 15 < samples[i - 1].Sd)
                drop = samples[i].At;
        }
        if (drop is null) return null;
        var reset = drop.Value.AddDays(7);
        return reset > now ? reset : null;
    }

    public static ClaudeCredentials? LoadOAuth()
    {
        var key = ChromiumCrypt.LoadOsCryptKey(LocalStatePath);
        if (key is null || !File.Exists(ConfigPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            foreach (var name in new[] { "oauth:tokenCacheV2", "oauth:tokenCache" })
            {
                if (!doc.RootElement.TryGetProperty(name, out var el)) continue;
                if (el.ValueKind != JsonValueKind.String) continue;
                var plain = ChromiumCrypt.Decrypt(key, el.GetString() ?? "");
                if (plain is null) continue;
                if (ClaudeKeychain.TryParse(plain, out var creds) &&
                    !string.IsNullOrWhiteSpace(creds.AccessToken) &&
                    !creds.IsExpired)
                    return creds;
                if (TryParseLoose(plain, out creds) && !creds.IsExpired)
                    return creds;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    internal static bool TryParseLoose(string json, out ClaudeCredentials credentials)
    {
        credentials = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!FindToken(doc.RootElement, out var token, out var expires))
                return false;
            credentials = new ClaudeCredentials(token, expires ?? DateTime.Now.AddHours(1), null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool FindToken(JsonElement el, out string token, out DateTime? expires)
    {
        token = "";
        expires = null;
        if (el.ValueKind == JsonValueKind.Object)
        {
            var t = Str(el, "accessToken") ?? Str(el, "access_token") ?? Str(el, "token");
            if (!string.IsNullOrWhiteSpace(t) && t.Length > 12)
            {
                token = t;
                expires = DateOf(el, "expiresAt") ?? DateOf(el, "expires_at") ?? DateOf(el, "expiry");
                return true;
            }
            foreach (var prop in el.EnumerateObject())
            {
                if (FindToken(prop.Value, out token, out expires)) return true;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in el.EnumerateArray())
            {
                if (FindToken(child, out token, out expires)) return true;
            }
        }
        return false;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? DateOf(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number)
        {
            var n = v.GetDouble();
            if (n <= 0) return null;
            return n > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)n).LocalDateTime
                : DateTimeOffset.FromUnixTimeSeconds((long)n).LocalDateTime;
        }
        if (v.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(v.GetString(), out var dto))
            return dto.LocalDateTime;
        return null;
    }

    private static bool TryInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number) return false;
        if (n.TryGetInt32(out value)) return true;
        value = (int)Math.Round(n.GetDouble());
        return true;
    }

    private static DateTime FromUnix(long stamp) =>
        DateTimeOffset.FromUnixTimeMilliseconds(stamp > 10_000_000_000 ? stamp : stamp * 1000)
            .LocalDateTime;
}
