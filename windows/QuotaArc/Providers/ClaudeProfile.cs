using System.Security.Cryptography;
using System.Text;

namespace QuotaArc.Providers;

internal sealed record ClaudeProfile(string? Slug, string ConfigDirectory)
{
    public const string DefaultId = "claude";
    public const string DirectoryPrefix = ".claude";
    public const string DefaultKeychainService = "Claude Code-credentials";

    public static ClaudeProfile Default(string? home = null) =>
        new(null, Path.Combine(home ?? Home, DirectoryPrefix));

    public static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static List<ClaudeProfile> Discover(string? home = null)
    {
        home ??= Home;
        var extras = new List<ClaudeProfile>();
        try
        {
            foreach (var name in Directory.GetDirectories(home).Select(Path.GetFileName))
            {
                if (name is null) continue;
                var slug = SlugFromDirectoryName(name);
                if (slug is null) continue;
                var directory = Path.Combine(home, name);
                if (!IsProfileDirectory(directory)) continue;
                extras.Add(new ClaudeProfile(slug, directory));
            }
        }
        catch { /* home unreadable */ }

        extras.Sort((a, b) => string.CompareOrdinal(a.Slug, b.Slug));
        return [Default(home), .. extras];
    }

    public static string? SlugFromDirectoryName(string name)
    {
        var prefix = DirectoryPrefix + "-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var slug = name[prefix.Length..];
        return slug.Length == 0 ? null : slug;
    }

    private static readonly string[] Markers =
        ["sessions", "projects", "settings.json", "history.jsonl", ".claude.json"];

    public static bool IsProfileDirectory(string path) =>
        Directory.Exists(path) && Markers.Any(m => File.Exists(Path.Combine(path, m)) || Directory.Exists(Path.Combine(path, m)));

    public string Id => Slug is { } s ? $"{DefaultId}-{s}" : DefaultId;
    public string DisplayName => Slug is { } s ? $"Claude ({s})" : "Claude";

    public static bool IsClaude(string providerId) =>
        providerId == DefaultId || providerId.StartsWith(DefaultId + "-", StringComparison.Ordinal);

    public static string? SlugFromProviderId(string id)
    {
        var prefix = DefaultId + "-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var slug = id[prefix.Length..];
        return slug.Length == 0 ? null : slug;
    }

    public string DisplayPath
    {
        get
        {
            var home = Home;
            return ConfigDirectory.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                ? "~" + ConfigDirectory[home.Length..].Replace('\\', '/')
                : ConfigDirectory;
        }
    }

    public string SessionsDirectory => Path.Combine(ConfigDirectory, "sessions");

    public string CredentialService
    {
        get
        {
            if (Slug is null) return DefaultKeychainService;
            return $"{DefaultKeychainService}-{KeychainSuffix(ConfigDirectory)}";
        }
    }

    public static string KeychainSuffix(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    public string SourceName => Slug is null ? "Claude Code" : $"Claude Code in {DisplayPath}";

    public string SignInCommand =>
        Slug is null ? "claude" : $"set CLAUDE_CONFIG_DIR={DisplayPath}&& claude";
}
