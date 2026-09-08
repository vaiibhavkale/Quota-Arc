using Microsoft.Data.Sqlite;
using QuotaArc.Sessions;

namespace QuotaArc.Tests;

/// The bug this exists to stop: Cursor.exe simply running was read as "an agent
/// is working", so the notch spun continuously with nothing actually in
/// flight. These pin what genuinely counts as working, off the same
/// composerHeaders rows the Mac build reads.
public class CursorActivityMonitorTests
{
    /// The instant every fixture's `unfinishedRunAt` names.
    private static readonly DateTime RunAt = DateTimeOffset.FromUnixTimeMilliseconds(1787981829823).LocalDateTime;

    private static long Millis(DateTime date) => new DateTimeOffset(date).ToUnixTimeMilliseconds();

    /// One `composerHeaders` row. Built rather than pasted so a fixture differs
    /// from its neighbour only where the test means it to.
    private static string Header(
        string? id = "abc",
        DateTime? run = null,
        DateTime? checkpoint = null,
        DateTime? lastUpdated = null,
        DateTime? created = null,
        string? name = null,
        string? subtitle = null,
        bool? blocking = null,
        bool? plan = null)
    {
        run ??= RunAt;
        var head = new Dictionary<string, object?>();
        if (id is not null) head["composerId"] = id;
        if (run is { } r) head["unfinishedRunAt"] = Millis(r);
        if (checkpoint is { } c) head["conversationCheckpointLastUpdatedAt"] = Millis(c);
        if (lastUpdated is { } lu) head["lastUpdatedAt"] = Millis(lu);
        if (created is { } cr) head["createdAt"] = Millis(cr);
        if (name is not null) head["name"] = name;
        if (subtitle is not null) head["subtitle"] = subtitle;
        if (blocking is { } b) head["hasBlockingPendingActions"] = b;
        if (plan is { } p) head["hasPendingPlan"] = p;
        return System.Text.Json.JsonSerializer.Serialize(head);
    }

    /// `launchedAt: DateTime.MinValue` stands for "Cursor is running and has
    /// been for longer than any row in the fixture" — the ordinary case. `now`
    /// defaults to a second after the run started, since a fixture read
    /// against the wall clock would be hours stale and never busy.
    private static QuotaArc.Sessions.AgentSession? Session(
        string json,
        DateTime? launchedAt,
        TimeSpan? staleAfter = null,
        DateTime? now = null) =>
        CursorActivityMonitor.Session(
            json,
            launchedAt,
            staleAfter ?? TimeSpan.FromMinutes(15),
            now ?? RunAt.AddSeconds(1));

    private static readonly DateTime? DistantPast = DateTime.MinValue;

    /// The bug this exists to stop: Cursor is killed mid-run, `unfinishedRunAt`
    /// is never cleared, and the notch reports an agent working a day later
    /// with the editor not even open.
    [Fact]
    public void ARunIsNotBusyWhenCursorIsNotRunning() =>
        Assert.Null(Session(Header(), launchedAt: null));

    /// Same row, same absent editor, but Cursor has since been restarted: the
    /// run belongs to the process that is gone, not the one now open.
    [Fact]
    public void ARunFromBeforeThisLaunchIsNotBusy() =>
        Assert.Null(Session(Header(), launchedAt: RunAt.AddSeconds(1)));

    [Fact]
    public void ARunStartedAfterThisLaunchIsBusy()
    {
        var s = Session(Header(), launchedAt: RunAt.AddSeconds(-1));
        Assert.NotNull(s);
        Assert.Equal(AgentState.Busy, s!.State);
    }

    /// The one process liveness cannot see: a run abandoned with the editor
    /// still open. `unfinishedRunAt` stays set forever, so the only thing that
    /// says the run is over is a checkpoint that stopped moving.
    [Fact]
    public void ARunWhoseCheckpointWentSilentIsNotBusy() =>
        Assert.Null(Session(Header(checkpoint: RunAt.AddSeconds(160)),
            launchedAt: DistantPast, now: RunAt.AddSeconds(3600)));

    /// The other side of it: an agent between tool calls is quiet for seconds,
    /// not hours, and must keep its spinner.
    [Fact]
    public void ARunStillWritingIsBusy()
    {
        var s = Session(Header(checkpoint: RunAt.AddSeconds(160)),
            launchedAt: DistantPast, now: RunAt.AddSeconds(200));
        Assert.NotNull(s);
        Assert.Equal(AgentState.Busy, s!.State);
    }

    /// A blocked row still counts with the editor shut — it really is waiting
    /// on you — but it must not be dated by a run that never finished.
    [Fact]
    public void WaitingSurvivesAClosedEditorButNotAStaleRunTime()
    {
        var checkpoint = RunAt.AddSeconds(18_170);
        var s = Session(Header(checkpoint: checkpoint, plan: true), launchedAt: null);
        Assert.NotNull(s);
        Assert.Equal(AgentState.Waiting, s!.State);
        Assert.Equal(checkpoint, s.Since);
    }

    /// The restart case, read off the timestamp that actually moves: a
    /// conversation last written to before this Cursor started belongs to the
    /// process that is gone.
    [Fact]
    public void AConversationLastWrittenBeforeThisLaunchIsNotBusy() =>
        Assert.Null(Session(Header(checkpoint: RunAt.AddSeconds(160)),
            launchedAt: RunAt.AddSeconds(8_170), now: RunAt.AddSeconds(8_230)));

    /// The boundary: a conversation written at the very instant Cursor
    /// launched is the current process's, not the dead one's.
    [Fact]
    public void AWriteAtTheLaunchInstantCountsAsThisProcess()
    {
        var s = Session(Header(checkpoint: RunAt), launchedAt: RunAt);
        Assert.NotNull(s);
        Assert.Equal(AgentState.Busy, s!.State);
    }

    /// A quarter of real rows carry `lastUpdatedAt` and no checkpoint. Dropping
    /// it from the chain dated them from `createdAt` instead — a chat blocked
    /// ten minutes ago reading as blocked for weeks.
    [Fact]
    public void LastUpdatedAtStandsInForAMissingCheckpoint()
    {
        var touched = RunAt.AddSeconds(18_170);
        var s = Session(
            Header(run: null, lastUpdated: touched, created: RunAt.AddSeconds(-981_829), plan: true),
            launchedAt: null);
        Assert.NotNull(s);
        Assert.Equal(AgentState.Waiting, s!.State);
        Assert.Equal(touched, s.Since);
    }

    /// The last rung. A row blocked with nothing written to it since it was
    /// opened is dated from when it was opened — not from the wall clock,
    /// which would restamp it on every poll and republish forever.
    [Fact]
    public void AWaitingRowWithNoWritesFallsBackToCreatedAt()
    {
        var created = RunAt.AddSeconds(-981_829);
        var s = Session(Header(run: null, created: created, plan: true), launchedAt: null);
        Assert.NotNull(s);
        Assert.Equal(created, s!.Since);
    }

    /// `LaunchDate` is often unavailable — reporting that as "Cursor is
    /// closed" would silently retire every Cursor row while the editor sat
    /// there working.
    [Fact]
    public void ARunningCursorWithNoLaunchDateIsStillRunning()
    {
        Assert.Equal(DateTime.MinValue, CursorActivityMonitor.LaunchDate(found: true, launchDate: null));
        Assert.Null(CursorActivityMonitor.LaunchDate(found: false, launchDate: null));
    }

    /// The window is a boundary, and it is inclusive.
    [Fact]
    public void TheStalenessBoundaryIsInclusive()
    {
        var fixture = Header(checkpoint: RunAt);
        var atTheEdge = Session(fixture, DistantPast, staleAfter: TimeSpan.FromSeconds(60), now: RunAt.AddSeconds(60));
        Assert.NotNull(atTheEdge);
        Assert.Equal(AgentState.Busy, atTheEdge!.State);
        Assert.Null(Session(fixture, DistantPast, staleAfter: TimeSpan.FromSeconds(60), now: RunAt.AddSeconds(61)));
    }

    /// `unfinishedRunAt` is set while a run is in flight and cleared when it ends.
    [Fact]
    public void AnUnfinishedRunIsBusy()
    {
        var s = Session(Header(name: "General chat", subtitle: "Read SKILL.md", blocking: false), DistantPast);
        Assert.NotNull(s);
        Assert.Equal(AgentState.Busy, s!.State);
        Assert.Equal("General chat", s.Name);
        Assert.Equal("Read SKILL.md", s.Detail);
    }

    /// A finished run is not a session worth a row — an editor with a long
    /// chat history is not a pile of things happening.
    [Fact]
    public void AFinishedRunIsNotListed() =>
        Assert.Null(Session(Header(run: null, blocking: false), DistantPast));

    /// Blocked outranks busy: it is the only state asking for something.
    [Fact]
    public void BlockingActionsOutrankARunningTurn()
    {
        var s = Session(Header(blocking: true), DistantPast);
        Assert.NotNull(s);
        Assert.Equal(AgentState.Waiting, s!.State);
        Assert.Equal("needs your input", s.WaitingFor);
    }

    [Fact]
    public void APendingPlanAlsoCountsAsWaiting()
    {
        var s = Session(Header(run: null, plan: true), DistantPast);
        Assert.NotNull(s);
        Assert.Equal(AgentState.Waiting, s!.State);
    }

    /// Ids are namespaced, so a Cursor composer can never collide with a Claude pid.
    [Fact]
    public void IdsAreNamespaced()
    {
        var s = Session(Header(), DistantPast);
        Assert.NotNull(s);
        Assert.Equal("cursor.abc", s!.Id);
    }

    [Fact]
    public void RejectsRubbish()
    {
        Assert.Null(Session("not json", DistantPast));
        Assert.Null(Session(Header(id: null), DistantPast));
    }

    // MARK: - The store read

    /// Rows land newest-first, and an archived chat is not something
    /// happening — both are in the SQL rather than in C#, so only a real
    /// store shows them.
    [Fact]
    public void ReadOrdersNewestFirstAndSkipsArchivedRows()
    {
        var older = RunAt;
        var newer = RunAt.AddSeconds(100);
        var path = MakeStore(
        [
            (Header(id: "older", run: older, checkpoint: older), 0),
            (Header(id: "newer", run: newer, checkpoint: newer), 0),
            (Header(id: "filed", run: newer, checkpoint: newer), 1),
        ]);
        try
        {
            var found = CursorActivityMonitor.Read(path, DistantPast, TimeSpan.FromMinutes(10), newer.AddSeconds(1));
            Assert.Equal(["cursor.newer", "cursor.older"], found.Select(s => s.Id));
        }
        finally { File.Delete(path); }
    }

    private static string MakeStore((string json, int archived)[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cursor-{Guid.NewGuid()}.sqlite");
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        using (var create = db.CreateCommand())
        {
            create.CommandText = "CREATE TABLE composerHeaders (value TEXT, isArchived INT, recency INT)";
            create.ExecuteNonQuery();
        }
        for (var i = 0; i < rows.Length; i++)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO composerHeaders VALUES ($v, $a, $r)";
            insert.Parameters.AddWithValue("$v", rows[i].json);
            insert.Parameters.AddWithValue("$a", rows[i].archived);
            insert.Parameters.AddWithValue("$r", i);
            insert.ExecuteNonQuery();
        }
        return path;
    }
}
