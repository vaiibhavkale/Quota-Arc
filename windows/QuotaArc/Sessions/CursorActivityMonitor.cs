using System.Diagnostics;
using System.Text.Json;
using QuotaArc.Providers;

namespace QuotaArc.Sessions;

/// Reads what Cursor's agents are doing from the editor's own state store,
/// the same `composerHeaders` rows the Mac build reads.
///
/// Cursor.exe simply running is not "an agent is working" — someone can have
/// the editor open all day with nothing in flight — so this does not use
/// `ProcessActivityMonitor`, which reports every session as busy for as long
/// as the process exists. That was the Windows bug: the notch spun
/// continuously because "Cursor is running" and "a run is in progress" were
/// treated as the same fact.
///
/// `unfinishedRunAt` is set while a run is in flight and never cleared, so it
/// cannot say a run is over on its own — an editor killed mid-run or a run
/// abandoned with the editor still open both leave it set forever. The
/// timestamp that actually answers "is this still going" is
/// `conversationCheckpointLastUpdatedAt`, which Cursor moves on every message
/// and tool result (falling back to `lastUpdatedAt`, which a quarter of rows
/// carry instead).
internal static class CursorActivityMonitor
{
    public static List<AgentSession> Read(string store, DateTime? cursorLaunchedAt, TimeSpan staleAfter) =>
        Read(store, cursorLaunchedAt, staleAfter, DateTime.Now);

    public static List<AgentSession> Read(string store, DateTime? cursorLaunchedAt, TimeSpan staleAfter, DateTime now)
    {
        using var db = SqliteStore.Open(store);
        if (db is null) return [];

        var values = SqliteStore.Rows(
            db, "SELECT value FROM composerHeaders WHERE isArchived = 0 ORDER BY recency DESC LIMIT 40");

        return values
            .Select(json => Session(json, cursorLaunchedAt, staleAfter, now))
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderByDescending(s => s.Since)
            .ToList();
    }

    /// Only sessions that are *doing* something are worth a row — an editor
    /// with forty idle chats in its history is not forty things happening.
    public static AgentSession? Session(string json, DateTime? cursorLaunchedAt, TimeSpan staleAfter, DateTime now)
    {
        JsonElement head;
        try
        {
            using var doc = JsonDocument.Parse(json);
            head = doc.RootElement.Clone();
        }
        catch { return null; }

        if (!head.TryGetProperty("composerId", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return null;
        var id = idEl.GetString();
        if (string.IsNullOrEmpty(id)) return null;

        var blocked = IsTrue(head, "hasBlockingPendingActions") || IsTrue(head, "hasPendingPlan");
        var runStart = DateOf(head, "unfinishedRunAt");
        // A quarter of rows carry `lastUpdatedAt` and no checkpoint at all.
        var lastWrite = DateOf(head, "conversationCheckpointLastUpdatedAt") ?? DateOf(head, "lastUpdatedAt");

        var isRunning = false;
        if (runStart is { } rs && cursorLaunchedAt is { } launched)
        {
            // Nothing written yet means the composer was only just created, and
            // since `unfinishedRunAt` *is* its creation time that is the most
            // recent thing to have happened to it. The same value on a chat
            // opened last week is correctly stale.
            var touched = lastWrite ?? rs;
            if (touched >= launched)
                isRunning = (now - touched) <= staleAfter;
        }

        AgentState state;
        if (blocked) state = AgentState.Waiting;
        else if (isRunning) state = AgentState.Busy;
        else return null;

        // `runStart` is the composer's creation time, so it dates a busy row the
        // way it always has — from when the chat began. A row that is merely
        // waiting must not borrow it: a session blocked since this morning did
        // not start waiting when the chat was opened last week.
        var since = (isRunning ? runStart : null)
            ?? lastWrite
            ?? DateOf(head, "createdAt")
            ?? now;

        var name = StringOf(head, "name") ?? "Untitled chat";
        var subtitle = StringOf(head, "subtitle") ?? "Cursor";

        return new AgentSession(
            $"cursor.{id}", name, subtitle, state,
            blocked ? "needs your input" : null, since);
    }

    /// When the running editor started, or null if Cursor is not running at
    /// all. `launchedAt` is optional and often unavailable, so "running, start
    /// time unknown" must not collapse into "not running" — it answers
    /// `DateTime.MinValue`, so every row predates it and the staleness window
    /// alone decides.
    public static DateTime? CursorLaunchDate()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("Cursor"); }
        catch { return LaunchDate(found: false, launchDate: null); }

        if (processes.Length == 0) return LaunchDate(found: false, launchDate: null);

        DateTime? started = null;
        try { started = processes[0].StartTime; }
        catch { /* denied */ }
        finally { foreach (var p in processes) p.Dispose(); }

        return LaunchDate(found: true, launchDate: started);
    }

    public static DateTime? LaunchDate(bool found, DateTime? launchDate) =>
        found ? (launchDate ?? DateTime.MinValue) : null;

    private static bool IsTrue(JsonElement head, string key) =>
        head.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? StringOf(JsonElement head, string key) =>
        head.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// Cursor writes its timestamps as milliseconds since the epoch.
    private static DateTime? DateOf(JsonElement head, string key) =>
        head.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)v.GetDouble()).LocalDateTime
            : null;
}
