using QuotaArc.Providers;

namespace QuotaArc;

internal static class Log
{
    public static void Info(string message) =>
        System.Diagnostics.Debug.WriteLine($"[quotaarc] {message}");

    public static void Error(string message) =>
        System.Diagnostics.Debug.WriteLine($"[quotaarc:error] {message}");
}

internal static class Launch
{
    public static bool App(string name)
    {
        try
        {
            var candidates = name.ToLowerInvariant() switch
            {
                "cursor" => new[] { "cursor", @"Cursor\Cursor.exe" },
                "codex" => new[] { "codex" },
                "antigravity" => new[] { "Antigravity", "antigravity" },
                _ => new[] { name }
            };
            foreach (var c in candidates)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = c,
                        UseShellExecute = true
                    });
                    return true;
                }
                catch { /* try next */ }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"launch {name}: {ex.Message}");
        }
        return false;
    }
}
