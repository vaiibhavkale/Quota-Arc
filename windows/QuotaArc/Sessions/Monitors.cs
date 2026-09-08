using System.Globalization;
using System.Text.Json;

namespace QuotaArc.Sessions;

internal static class ClaudeSessionMonitor
{
    public static List<AgentSession> Read(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var found = new List<AgentSession>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var json = doc.RootElement;
                if (!json.TryGetProperty("pid", out var pidEl)) continue;
                var pid = pidEl.GetInt32();
                if (!json.TryGetProperty("cwd", out var cwdEl)) continue;
                var cwd = cwdEl.GetString() ?? "";
                var raw = json.TryGetProperty("status", out var st) ? st.GetString() : null;
                var tempo = json.TryGetProperty("tempo", out var te) ? te.GetString() : null;
                var state = (tempo, raw) switch
                {
                    ("blocked", _) or (_, "waiting") => AgentState.Waiting,
                    ("active", _) or (_, "busy") => AgentState.Busy,
                    _ => AgentState.Idle
                };
                DateTime? startedAt = null;
                if (json.TryGetProperty("startedAt", out var sa) && sa.ValueKind == JsonValueKind.Number)
                    startedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)sa.GetDouble()).LocalDateTime;
                if (!ProcessLiveness.IsAlive(pid, startedAt)) continue;

                var millis = json.TryGetProperty("statusUpdatedAt", out var su) && su.ValueKind == JsonValueKind.Number
                    ? su.GetDouble()
                    : json.TryGetProperty("updatedAt", out var up) && up.ValueKind == JsonValueKind.Number
                        ? up.GetDouble() : (double?)null;
                var folder = Path.GetFileName(cwd.TrimEnd('\\', '/'));
                var name = json.TryGetProperty("name", out var n) ? n.GetString() ?? folder : folder;
                var waiting = json.TryGetProperty("waitingFor", out var w) ? w.GetString()
                    : json.TryGetProperty("needs", out var needs) ? needs.GetString() : null;
                var since = millis is { } ms
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime
                    : DateTime.Now;
                found.Add(new AgentSession(
                    $"claude.{pid}", name, $"{Surface(json)} · {folder}", state, waiting, since));
            }
            catch { /* skip file */ }
        }
        found.Sort((a, b) => b.Since.CompareTo(a.Since));
        return found;
    }

    private static string Surface(JsonElement json)
    {
        var entry = json.TryGetProperty("entrypoint", out var e) ? e.GetString() : null;
        return entry switch
        {
            "claude-desktop" or "claude-desktop-3p" => "Desktop",
            "claude-vscode" => "VS Code",
            "local-agent" => "Agent",
            _ => "Terminal"
        };
    }
}

internal static class ProcessActivityMonitor
{
    public static List<AgentSession> IfRunning(string id, string name, params string[] processNames)
    {
        if (!ProcessLiveness.NamedProcessRunning(processNames)) return [];
        return
        [
            new AgentSession(id, name, "running", AgentState.Busy, null, DateTime.Now)
        ];
    }
}
