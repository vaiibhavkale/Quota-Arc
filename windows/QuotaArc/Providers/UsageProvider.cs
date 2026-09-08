using QuotaArc.Model;

namespace QuotaArc.Providers;

internal interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }
    ProviderGlyph Glyph { get; }
    Task<ProviderSnapshot> FetchSnapshotAsync();
    ProviderAccount? Account();
    SignInRoute SignInRoute { get; }
    Task SignOutAsync() => Task.CompletedTask;
    void ForgetCachedCredential() { }
}

internal enum UsageErrorKind
{
    NeedsAuth,
    AccessDenied,
    CredentialExpired,
    BadResponse,
    RateLimited,
    NothingMetered
}

internal sealed class UsageProviderException : Exception
{
    public UsageErrorKind Kind { get; }
    public int Status { get; }
    public double RetryAfter { get; }

    public UsageProviderException(UsageErrorKind kind, string? message = null, int status = 0, double retryAfter = 0)
        : base(message ?? kind.ToString())
    {
        Kind = kind;
        Status = status;
        RetryAfter = retryAfter;
    }

    public static UsageProviderException NeedsAuth() => new(UsageErrorKind.NeedsAuth);
    public static UsageProviderException AccessDenied() => new(UsageErrorKind.AccessDenied);
    public static UsageProviderException CredentialExpired() => new(UsageErrorKind.CredentialExpired);
    public static UsageProviderException BadResponse(int status) =>
        new(UsageErrorKind.BadResponse, $"HTTP {status}", status);
    public static UsageProviderException RateLimited(double retryAfter) =>
        new(UsageErrorKind.RateLimited, retryAfter: retryAfter);
    public static UsageProviderException NothingMetered(string why) =>
        new(UsageErrorKind.NothingMetered, why);
}

internal sealed record ProviderAccount(string? Label, string? Plan, string Source, Uri? ManageUrl)
{
    public string Summary =>
        string.Join(" · ", new[]
        {
            Label,
            Plan is { Length: > 0 } p ? char.ToUpper(p[0]) + p[1..] : null,
            $"via {Source}"
        }.Where(s => !string.IsNullOrEmpty(s)));
}

internal abstract record SignInRoute
{
    public sealed record OpenApp(string Name) : SignInRoute;
    public sealed record Guidance(string Text) : SignInRoute;

    public string? ActionTitle => this switch
    {
        OpenApp a => $"Open {a.Name}",
        _ => null
    };

    public string Explanation => this switch
    {
        OpenApp a => $"Sign in with {a.Name} to read this account.",
        Guidance g => g.Text,
        _ => "Sign in with the tool that owns this account."
    };

    public string SignOutCaveat => this switch
    {
        OpenApp a => $"You stay signed in to {a.Name} — end that session in {a.Name} itself.",
        _ => "You stay signed in to the tool that owns the account."
    };
}

internal sealed record ProviderSummary(
    string Id,
    string Name,
    ProviderGlyph Glyph,
    ProviderAccount? Account,
    SignInRoute SignIn,
    bool WasRefusedAccess)
{
    public bool UsesKeychain => ClaudeProfile.IsClaude(Id) || Id == "gemini";
}
