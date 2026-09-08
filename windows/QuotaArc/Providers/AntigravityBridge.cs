using System.Diagnostics;
using System.Management;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using QuotaArc.Model;

namespace QuotaArc.Providers;

/// <summary>
/// Reads quota from Antigravity's local language server, the same path its
/// Models and Usage panel uses. Google's cloud quota endpoint rejects personal
/// accounts, but the loopback server already holds the client identity.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AntigravityBridge
{
    internal const string CsrfHeader = "x-codeium-csrf-token";
    private const string Service =
        "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";

    internal sealed record Endpoint(int ProcessId, IReadOnlyList<int> Ports, string CsrfToken);

    private static Endpoint? _cached;

    public static void ForgetCached() => _cached = null;

    public static Endpoint? Discover()
    {
        if (_cached is { } held && StillAlive(held)) return held;
        _cached = null;

        foreach (var proc in LanguageServers())
        {
            if (!TryParseCommandLine(proc.CommandLine, out var token)) continue;
            var ports = ListeningPorts(proc.ProcessId);
            if (ports.Count == 0) continue;
            _cached = new Endpoint(proc.ProcessId, ports, token);
            return _cached;
        }
        return null;
    }

    public static async Task<List<LimitWindow>?> QuotaAsync(Endpoint endpoint, HttpClient? http = null)
    {
        var owned = http is null;
        http ??= LocalhostHttp.Create();
        try
        {
            Exception? last = null;
            foreach (var port in endpoint.Ports)
            {
                try
                {
                    var windows = await QuotaOnPortAsync(http, port, endpoint.CsrfToken);
                    if (windows.Count > 0) return windows;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            if (last is not null) throw last;
            return [];
        }
        finally
        {
            if (owned) http.Dispose();
        }
    }

    internal static List<LimitWindow> WindowsFromBridge(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var body) ||
            !body.TryGetProperty("groups", out var groups) ||
            groups.ValueKind != JsonValueKind.Array)
            return [];

        var windows = new List<LimitWindow>();
        foreach (var group in groups.EnumerateArray())
        {
            var groupName = Str(group, "displayName");
            if (!group.TryGetProperty("buckets", out var buckets) ||
                buckets.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var bucket in buckets.EnumerateArray())
            {
                if (!TryDouble(bucket, "remainingFraction", out var remaining)) continue;
                if (remaining is < 0 or > 1) continue;
                var id = Str(bucket, "bucketId") ?? groupName ?? "quota";
                var label = groupName ?? Str(bucket, "displayName") ?? "Usage";
                DateTime? resets = null;
                if (Str(bucket, "resetTime") is { } rt)
                    resets = AntigravityCredentials.Parse(rt);
                windows.Add(new LimitWindow(id, label, 1 - remaining, ResetsAt: resets));
            }
        }
        windows.Sort(BridgeOrder);
        return windows;
    }

    /// <summary>
    /// Antigravity meters Gemini and Claude/GPT as separate pools. Pinning the
    /// ring to Gemini (or to the 5-hour window) shows 0% while another pool is
    /// the one actually burning.
    /// </summary>
    internal static string? HeadlineId(IReadOnlyList<LimitWindow> windows)
    {
        LimitWindow? best = null;
        foreach (var window in windows)
        {
            if (window.UsedFraction is not { } frac) continue;
            if (best is null || frac > (best.UsedFraction ?? 0))
                best = window;
        }
        return best?.Id ?? windows.FirstOrDefault()?.Id;
    }

    private static int BridgeOrder(LimitWindow a, LimitWindow b)
    {
        int Rank(string id) => id switch
        {
            "gemini-5h" => 0,
            "3p-5h" => 1,
            "gemini-weekly" => 2,
            "3p-weekly" => 3,
            _ => 4
        };
        var cmp = Rank(a.Id).CompareTo(Rank(b.Id));
        return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
    }

    private static bool StillAlive(Endpoint endpoint)
    {
        try
        {
            using var proc = Process.GetProcessById(endpoint.ProcessId);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<(int ProcessId, string CommandLine)> LanguageServers()
    {
        using var search = new ManagementObjectSearcher(
            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='language_server_windows_x64.exe'");
        foreach (var obj in search.Get())
        {
            if (obj["ProcessId"] is not uint pidRaw) continue;
            var pid = (int)pidRaw;
            var line = obj["CommandLine"]?.ToString() ?? "";
            if (line.Contains("--csrf_token", StringComparison.Ordinal))
                yield return (pid, line);
        }
    }

    internal static bool TryParseCommandLine(string line, out string token)
    {
        token = FlagValue(line, "--csrf_token") ?? "";
        return token.Length > 0;
    }

    private static string? FlagValue(string line, string flag)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals(flag, StringComparison.Ordinal))
                return parts[i + 1].Trim('"');
        }
        return null;
    }

    internal static List<int> ListeningPorts(int pid)
    {
        var ports = new List<int>();
        var psi = new ProcessStartInfo("netstat", "-ano -p TCP")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc is null) return ports;
        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            if (!int.TryParse(parts[^1], out var owner) || owner != pid) continue;
            var endpoint = parts[1];
            var colon = endpoint.LastIndexOf(':');
            if (colon < 0) continue;
            if (int.TryParse(endpoint[(colon + 1)..], out var port) && port > 0)
                ports.Add(port);
        }
        return ports.Distinct().OrderBy(p => p).ToList();
    }

    private static async Task<List<LimitWindow>> QuotaOnPortAsync(HttpClient http, int port, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"https://127.0.0.1:{port}{Service}"));
        req.Headers.TryAddWithoutValidation(CsrfHeader, token);
        req.Content = new StringContent("""{"forceRefresh":true}""", Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req);
        if ((int)res.StatusCode != 200)
            throw UsageProviderException.BadResponse((int)res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return WindowsFromBridge(doc.RootElement);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number) return false;
        value = n.GetDouble();
        return true;
    }
}
