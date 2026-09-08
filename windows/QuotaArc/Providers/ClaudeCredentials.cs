using System.Text.Json;

namespace QuotaArc.Providers;

internal sealed record ClaudeCredentials(string AccessToken, DateTime ExpiresAt, string? SubscriptionType)
{
    public bool IsExpired => ExpiresAt <= DateTime.Now;
}

internal sealed class ClaudeKeychain
{
    public string Service { get; }
    private readonly string _configDirectory;
    private readonly CredentialCache<ClaudeCredentials> _cache;

    public ClaudeKeychain(string service, string? configDirectory = null)
    {
        Service = service;
        _configDirectory = configDirectory
            ?? Path.Combine(ClaudeProfile.Home, ClaudeProfile.DirectoryPrefix);
        _cache = new CredentialCache<ClaudeCredentials>(c => c.IsExpired);
    }

    public ClaudeKeychain(ClaudeProfile profile) : this(profile.CredentialService, profile.ConfigDirectory) { }

    public static ClaudeKeychain Default { get; } = new(ClaudeProfile.DefaultKeychainService);

    public ClaudeCredentials Load() =>
        _cache.Value(() => FileStamp(), Read);

    public ClaudeCredentials? TryLoad()
    {
        try { return Load(); }
        catch (UsageProviderException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public void ForgetCached() => _cache.Forget();

    private DateTime? FileStamp()
    {
        DateTime? latest = null;
        foreach (var path in FileCandidates()
                     .Append(ClaudeDesktop.HistoryPath)
                     .Append(ClaudeDesktop.ConfigPath))
        {
            if (!File.Exists(path)) continue;
            var t = File.GetLastWriteTimeUtc(path);
            if (latest is null || t > latest) latest = t;
        }
        return latest;
    }

    private ClaudeCredentials Read()
    {
        var blob = WindowsCredentialStore.Read(Service, Service + ":", "Claude Code", "claude");
        if (blob is not null && TryParse(blob, out var fromCred) && HasToken(fromCred))
            return fromCred;

        foreach (var path in FileCandidates())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path);
                if (TryParse(text, out var fromFile) && HasToken(fromFile)) return fromFile;
            }
            catch { /* next */ }
        }

        if (ClaudeDesktop.LoadOAuth() is { } desktop && HasToken(desktop))
            return desktop;

        throw UsageProviderException.NeedsAuth();
    }

    private static bool HasToken(ClaudeCredentials creds) =>
        !string.IsNullOrWhiteSpace(creds.AccessToken) && !creds.IsExpired;

    private IEnumerable<string> FileCandidates()
    {
        yield return Path.Combine(_configDirectory, ".credentials.json");
        yield return Path.Combine(_configDirectory, "credentials.json");
        yield return Path.Combine(ClaudeProfile.Home, ".claude.json");
    }

    internal static bool TryParse(string json, out ClaudeCredentials credentials)
    {
        credentials = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("claudeAiOauth", out var oauth) &&
                !root.TryGetProperty("claude_ai_oauth", out oauth))
            {
                if (root.TryGetProperty("accessToken", out _) || root.TryGetProperty("access_token", out _))
                    oauth = root;
                else
                    return false;
            }

            var token = oauth.TryGetProperty("accessToken", out var t) ? t.GetString()
                : oauth.TryGetProperty("access_token", out t) ? t.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(token)) return false;

            DateTime expires;
            if (oauth.TryGetProperty("expiresAt", out var exp) || oauth.TryGetProperty("expires_at", out exp))
            {
                if (exp.ValueKind == JsonValueKind.Number)
                {
                    var n = exp.GetDouble();
                    expires = n > 10_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)n).LocalDateTime
                        : DateTimeOffset.FromUnixTimeSeconds((long)n).LocalDateTime;
                }
                else if (exp.ValueKind == JsonValueKind.String &&
                         DateTimeOffset.TryParse(exp.GetString(), out var parsed))
                    expires = parsed.LocalDateTime;
                else
                    expires = DateTime.Now.AddHours(1);
            }
            else expires = DateTime.Now.AddHours(1);

            string? plan = null;
            if (oauth.TryGetProperty("subscriptionType", out var p) ||
                oauth.TryGetProperty("subscription_type", out p))
                plan = p.GetString();

            credentials = new ClaudeCredentials(token, expires, plan);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
