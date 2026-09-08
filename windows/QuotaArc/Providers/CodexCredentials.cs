using System.Text;
using System.Text.Json;

namespace QuotaArc.Providers;

internal static class CodexCredentials
{
    public sealed record Credential(string AccessToken, string AccountId);

    public static string AuthPath => Path.Combine(ClaudeProfile.Home, ".codex", "auth.json");

    public static Credential Load(string? path = null, DateTime? now = null)
    {
        path ??= AuthPath;
        now ??= DateTime.Now;
        if (!File.Exists(path)) throw UsageProviderException.NeedsAuth();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var tokens = doc.RootElement.GetProperty("tokens");
            var access = tokens.GetProperty("access_token").GetString();
            var account = tokens.GetProperty("account_id").GetString();
            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(account))
                throw UsageProviderException.NeedsAuth();
            var claims = Claims(access);
            if (claims is { } c && c.TryGetProperty("exp", out var exp) &&
                exp.GetDouble() <= new DateTimeOffset(now.Value).ToUnixTimeSeconds())
                throw UsageProviderException.CredentialExpired();
            return new Credential(access, account);
        }
        catch (UsageProviderException) { throw; }
        catch { throw UsageProviderException.NeedsAuth(); }
    }

    public static ProviderAccount? Account(string? path = null)
    {
        path ??= AuthPath;
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var idToken = doc.RootElement.GetProperty("tokens").GetProperty("id_token").GetString();
            if (idToken is null) return null;
            var claims = Claims(idToken);
            if (claims is null) return null;
            string? email = claims.Value.TryGetProperty("email", out var e) ? e.GetString() : null;
            string? plan = null;
            if (claims.Value.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                auth.TryGetProperty("chatgpt_plan_type", out var p))
                plan = p.GetString();
            return new ProviderAccount(email, plan, "Codex", new Uri("https://chatgpt.com/#settings/Account"));
        }
        catch { return null; }
    }

    public static JsonElement? Claims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch { return null; }
    }
}
