using System.Net.Http.Headers;
using System.Text;
using QuotaArc.Model;

namespace QuotaArc.Providers;

internal sealed class AntigravityProvider : IUsageProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Uri Assist = new("https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist");
    private static readonly Uri Quota = new("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary");
    private bool _everBridged;

    public string Id => "gemini";
    public string DisplayName => "Antigravity";
    public ProviderGlyph Glyph => ProviderGlyph.Antigravity;
    public SignInRoute SignInRoute => new SignInRoute.OpenApp("Antigravity");
    public void ForgetCachedCredential()
    {
        AntigravityCredentials.ForgetCached();
        AntigravityBridge.ForgetCached();
    }

    public ProviderAccount? Account()
    {
        try
        {
            var creds = AntigravityCredentials.Load();
            return new ProviderAccount(
                null,
                creds.AuthMethod == "consumer" ? "Personal" : creds.AuthMethod,
                "Antigravity",
                new Uri("https://antigravity.google"));
        }
        catch
        {
            if (AntigravityBridge.Discover() is not null)
                return new ProviderAccount(null, null, "Antigravity", new Uri("https://antigravity.google"));
            return null;
        }
    }

    public async Task<ProviderSnapshot> FetchSnapshotAsync()
    {
        if (OperatingSystem.IsWindows() &&
            AntigravityBridge.Discover() is { } bridge &&
            await AntigravityBridge.QuotaAsync(bridge) is { Count: > 0 } local)
        {
            _everBridged = true;
            return Snapshot(local, Fidelity.Official);
        }

        if (_everBridged)
            throw UsageProviderException.CredentialExpired();

        AntigravityCredentials? credentials = null;
        try
        {
            credentials = AntigravityCredentials.Load();
        }
        catch (UsageProviderException ex) when (ex.Kind is UsageErrorKind.NeedsAuth or UsageErrorKind.AccessDenied)
        {
            throw;
        }

        if (credentials is not null)
        {
            if (credentials.IsExpired)
                throw UsageProviderException.CredentialExpired();

            await EnsureAssistAsync(credentials.AccessToken);
            if (await QuotaWindowsAsync(credentials.AccessToken) is { Count: > 0 } windows)
                return Snapshot(windows, Fidelity.Official);
        }

        var activity = AntigravityActivity.Read();
        return new ProviderSnapshot(
            Id, DisplayName, Glyph, Fidelity.Derived, new ProviderStatus.Ok(),
            [new LimitWindow("requests", "Requests today · no limit published", Used: activity.RequestsToday)]);
    }

    private ProviderSnapshot Snapshot(
        IReadOnlyList<LimitWindow> windows,
        Fidelity fidelity) =>
        new(Id, DisplayName, Glyph, fidelity, new ProviderStatus.Ok(), windows,
            AntigravityBridge.HeadlineId(windows));

    private async Task EnsureAssistAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Assist);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(
            """{"metadata":{"pluginType":"GEMINI"}}""", Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var status = (int)res.StatusCode;
        if (status == 401)
        {
            AntigravityCredentials.ForgetCached();
            throw UsageProviderException.NeedsAuth();
        }
        if (status == 403) throw UsageProviderException.NeedsAuth();
        if (status == 429) throw UsageProviderException.RateLimited(0);
        if (status != 200) throw UsageProviderException.BadResponse(status);
    }

    private static async Task<List<LimitWindow>?> QuotaWindowsAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Quota);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        if ((int)res.StatusCode != 200) return null;
        return AntigravityUsage.Windows(await res.Content.ReadAsByteArrayAsync());
    }
}
