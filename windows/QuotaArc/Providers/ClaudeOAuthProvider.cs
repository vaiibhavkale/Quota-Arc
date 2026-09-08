using System.Net.Http.Headers;
using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal sealed class ClaudeOAuthProvider : IUsageProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Uri _endpoint = new("https://api.anthropic.com/api/oauth/usage");
    private readonly ClaudeKeychain _keychain;
    private readonly UsageArchive _archive;
    private DateTime? _retryNoEarlierThan;
    private int _consecutiveRateLimits;
    private ClaudeCredentials? _credentials;

    public ClaudeOAuthProvider(ClaudeProfile? profile = null, UsageArchive? archive = null)
    {
        Profile = profile ?? ClaudeProfile.Default();
        Id = Profile.Id;
        DisplayName = Profile.DisplayName;
        _keychain = new ClaudeKeychain(Profile);
        _archive = archive ?? new UsageArchive();
        _retryNoEarlierThan = _archive.LoadBackoffUntil(Id);
    }

    public ClaudeProfile Profile { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public ProviderGlyph Glyph => ProviderGlyph.Claude;

    public ProviderAccount? Account()
    {
        var creds = _keychain.TryLoad();
        if (creds is not null)
            return new ProviderAccount(null, creds.SubscriptionType, Profile.SourceName,
                new Uri("https://claude.ai/settings/usage"));
        if (Profile.Slug is null && ClaudeDesktop.IsPresent())
            return new ProviderAccount(null, null, "Claude Desktop",
                new Uri("https://claude.ai/settings/usage"));
        return null;
    }

    public SignInRoute SignInRoute =>
        Profile.Slug is null && ClaudeDesktop.IsPresent()
            ? new SignInRoute.OpenApp("Claude")
            : new SignInRoute.Guidance(
                $"Run `{Profile.SignInCommand}` once - it signs in and refreshes the token this reads.");

    public void ForgetCachedCredential() => _keychain.ForgetCached();

    public async Task<ProviderSnapshot> FetchSnapshotAsync()
    {
        var local = Profile.Slug is null ? ClaudeDesktop.SnapshotFromHistory(DisplayName) : null;
        if (_retryNoEarlierThan is { } until && until > DateTime.Now)
        {
            if (local is not null) return local;
            throw UsageProviderException.RateLimited((until - DateTime.Now).TotalSeconds);
        }

        var creds = _keychain.TryLoad();
        if (creds is null)
        {
            if (local is not null) return local;
            throw UsageProviderException.NeedsAuth();
        }
        _credentials = creds;

        try
        {
            using var timeout = new CancellationTokenSource(
                local is not null ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(15));
            var snapshot = await FetchAsync(retryOnUnauthorized: true, timeout.Token);
            _retryNoEarlierThan = null;
            _consecutiveRateLimits = 0;
            _archive.SaveBackoffUntil(null, Id);
            return snapshot;
        }
        catch (UsageProviderException ex) when (ex.Kind is UsageErrorKind.NeedsAuth or UsageErrorKind.CredentialExpired)
        {
            _credentials = null;
            if (local is not null) return local;
            throw;
        }
        catch (UsageProviderException ex) when (ex.Kind == UsageErrorKind.RateLimited)
        {
            _consecutiveRateLimits++;
            _retryNoEarlierThan = DateTime.Now.AddSeconds(ex.RetryAfter);
            _archive.SaveBackoffUntil(_retryNoEarlierThan, Id);
            if (local is not null) return local;
            throw;
        }
        catch (Exception)
        {
            if (local is not null) return local;
            throw;
        }
    }

    private async Task<ProviderSnapshot> FetchAsync(bool retryOnUnauthorized, CancellationToken cancel)
    {
        var token = CurrentToken();
        using var req = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var res = await Http.SendAsync(req, cancel);
        var status = (int)res.StatusCode;
        if (status is 401 or 403)
        {
            _keychain.ForgetCached();
            _credentials = null;
            if (retryOnUnauthorized) return await FetchAsync(false, cancel);
            throw UsageProviderException.NeedsAuth();
        }
        if (status == 429)
        {
            var retry = RetryAfter(res) ?? 0;
            throw UsageProviderException.RateLimited(Backoff(_consecutiveRateLimits, retry));
        }
        if (status is < 200 or >= 300) throw UsageProviderException.BadResponse(status);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancel));
        return new ProviderSnapshot(
            Id, DisplayName, Glyph, Fidelity.Official, new ProviderStatus.Ok(),
            ClaudeUsage.LimitWindows(doc.RootElement), "session");
    }

    private string CurrentToken()
    {
        if (_credentials is { IsExpired: false } held) return held.AccessToken;
        var fresh = _keychain.Load();
        if (fresh.IsExpired) throw UsageProviderException.CredentialExpired();
        _credentials = fresh;
        return fresh.AccessToken;
    }

    public static double Backoff(int attempt, double? retryAfter)
    {
        const double floor = 60;
        const double ceiling = 15 * 60;
        var doubled = floor * Math.Pow(2, Math.Min(attempt, 4));
        return Math.Min(ceiling, Math.Max(doubled, retryAfter ?? 0));
    }

    private static double? RetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter?.Delta is { } d) return Math.Max(0, d.TotalSeconds);
        if (res.Headers.RetryAfter?.Date is { } date)
            return Math.Max(0, (date - DateTimeOffset.Now).TotalSeconds);
        return null;
    }
}
