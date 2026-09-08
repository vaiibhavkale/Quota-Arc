using System.Globalization;
using System.Text;
using System.Text.Json;

namespace QuotaArc.Providers;

internal sealed record AntigravityCredentials(string AccessToken, DateTime ExpiresAt, string AuthMethod)
{
    public bool IsExpired => ExpiresAt <= DateTime.Now;
    public const string Service = "gemini";
    public const string AccountName = "antigravity";
    private const string GoKeyringPrefix = "go-keyring-base64:";

    private static readonly CredentialCache<AntigravityCredentials> Cache =
        new(c => c.IsExpired);

    public static void ForgetCached() => Cache.Forget();

    public static AntigravityCredentials Load() =>
        Cache.Value(() => null, Read);

    private static AntigravityCredentials Read()
    {
        var blob = WindowsCredentialStore.Read(
            $"{Service}:{AccountName}",
            Service,
            $"go-keyring:{Service}:{AccountName}",
            AccountName);
        if (blob is not null && Decode(Encoding.UTF8.GetBytes(blob)) is { } fromCred)
            return fromCred;

        var home = ClaudeProfile.Home;
        foreach (var path in new[]
        {
            Path.Combine(home, ".gemini", "antigravity", "oauth.json"),
            Path.Combine(home, ".antigravity", "credentials.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                if (Decode(File.ReadAllBytes(path)) is { } fromFile) return fromFile;
            }
            catch { /* next */ }
        }

        throw UsageProviderException.NeedsAuth();
    }

    public static AntigravityCredentials? Decode(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data).Trim();
        if (text.StartsWith(GoKeyringPrefix, StringComparison.Ordinal))
            text = text[GoKeyringPrefix.Length..];
        try
        {
            var payload = Convert.FromBase64String(text);
            text = Encoding.UTF8.GetString(payload);
        }
        catch
        {
            // Already JSON.
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var method = root.TryGetProperty("auth_method", out var m) ? m.GetString() ?? "consumer" : "consumer";
            if (!root.TryGetProperty("token", out var tokenEl)) return null;
            var access = tokenEl.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            var expiry = tokenEl.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            if (string.IsNullOrEmpty(access) || expiry is null || Parse(expiry) is not { } at)
                return null;
            return new AntigravityCredentials(access, at, method);
        }
        catch { return null; }
    }

    public static DateTime? Parse(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dto))
            return dto.LocalDateTime;
        return null;
    }
}
