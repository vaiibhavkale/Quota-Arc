namespace QuotaArc.Providers;

internal sealed record CursorCredentials(string AccountId, string AccessToken)
{
    public string SessionCookie => $"WorkosCursorSessionToken={AccountId}::{AccessToken}";

    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cursor", "User", "globalStorage", "state.vscdb");

    public static ProviderAccount? Account(string? path = null)
    {
        path ??= StorePath;
        using var db = SqliteStore.Open(path);
        if (db is null) return null;
        var email = SqliteStore.Scalar(db, "SELECT value FROM ItemTable WHERE key = $v", "cursorAuth/cachedEmail");
        if (string.IsNullOrEmpty(email)) return null;
        var plan = SqliteStore.Scalar(db, "SELECT value FROM ItemTable WHERE key = $v", "cursorAuth/stripeMembershipType");
        return new ProviderAccount(email, plan, "Cursor", new Uri("https://cursor.com/dashboard"));
    }

    public static CursorCredentials Load(string? path = null)
    {
        path ??= StorePath;
        if (!File.Exists(path)) throw UsageProviderException.NeedsAuth();
        using var db = SqliteStore.Open(path) ?? throw UsageProviderException.NeedsAuth();
        var token = SqliteStore.Scalar(db, "SELECT value FROM ItemTable WHERE key = $v", "cursorAuth/accessToken");
        var account = SqliteStore.Scalar(db, "SELECT value FROM ItemTable WHERE key = $v", "cursorAuth/stripeMembershipAuthId");
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(account))
            throw UsageProviderException.NeedsAuth();
        return new CursorCredentials(account, token);
    }
}
