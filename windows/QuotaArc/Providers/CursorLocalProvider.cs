using QuotaArc.Model;

namespace QuotaArc.Providers;

internal sealed class CursorLocalProvider : IUsageProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Uri Endpoint = new("https://cursor.com/api/usage-summary");

    public string Id => "cursor";
    public string DisplayName => "Cursor";
    public ProviderGlyph Glyph => ProviderGlyph.Cursor;
    public SignInRoute SignInRoute => new SignInRoute.OpenApp("Cursor");
    public ProviderAccount? Account() => CursorCredentials.Account();

    public async Task<ProviderSnapshot> FetchSnapshotAsync()
    {
        var credentials = CursorCredentials.Load();
        using var req = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        req.Headers.TryAddWithoutValidation("Cookie", credentials.SessionCookie);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var res = await Http.SendAsync(req);
        var status = (int)res.StatusCode;
        if (status is 401 or 403) throw UsageProviderException.NeedsAuth();
        if (status is < 200 or >= 300) throw UsageProviderException.BadResponse(status);
        var body = await res.Content.ReadAsStringAsync();
        return new ProviderSnapshot(
            Id, DisplayName, Glyph, Fidelity.Official, new ProviderStatus.Ok(),
            CursorUsage.WindowsFromJson(body), "included");
    }
}
