using System.Net.Http.Headers;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal sealed class CodexLocalProvider : IUsageProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly UsageArchive _archive;
    private DateTime? _retryNoEarlierThan;

    public CodexLocalProvider(UsageArchive? archive = null)
    {
        _archive = archive ?? new UsageArchive();
        _retryNoEarlierThan = _archive.LoadBackoffUntil(Id);
    }

    public string Id => "codex";
    public string DisplayName => "Codex";
    public ProviderGlyph Glyph => ProviderGlyph.Openai;
    public SignInRoute SignInRoute => new SignInRoute.OpenApp("Codex");
    public ProviderAccount? Account() => CodexCredentials.Account();

    public async Task<ProviderSnapshot> FetchSnapshotAsync()
    {
        var now = DateTime.Now;
        if (_retryNoEarlierThan is { } until && until > now)
            throw UsageProviderException.RateLimited((until - now).TotalSeconds);

        var credential = CodexCredentials.Load();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        req.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var res = await Http.SendAsync(req);
        var status = (int)res.StatusCode;
        if (status is 401 or 403) throw UsageProviderException.NeedsAuth();
        if (status == 429)
        {
            var delay = Math.Max(60, RetryAfter(res) ?? 0);
            _retryNoEarlierThan = DateTime.Now.AddSeconds(delay);
            _archive.SaveBackoffUntil(_retryNoEarlierThan, Id);
            throw UsageProviderException.RateLimited(delay);
        }
        if (status is < 200 or >= 300) throw UsageProviderException.BadResponse(status);

        var data = await res.Content.ReadAsByteArrayAsync();
        var windows = CodexUsage.WindowsFrom(data);
        _retryNoEarlierThan = null;
        _archive.SaveBackoffUntil(null, Id);
        return new ProviderSnapshot(
            Id, DisplayName, Glyph, Fidelity.Official, new ProviderStatus.Ok(),
            windows, windows.FirstOrDefault()?.Id);
    }

    private static double? RetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter?.Delta is { } d) return Math.Max(0, d.TotalSeconds);
        return null;
    }
}
