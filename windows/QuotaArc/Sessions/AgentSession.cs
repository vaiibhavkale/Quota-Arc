using System.Windows.Media;
using QuotaArc.Design;

namespace QuotaArc.Sessions;

internal enum AgentState
{
    Busy,
    Waiting,
    Idle
}

internal sealed record AgentSession(
    string Id,
    string Name,
    string Detail,
    AgentState State,
    string? WaitingFor,
    DateTime Since);

internal sealed class ActivitySummary
{
    public enum Kind { Working, Waiting, Idle }

    public Kind State { get; }
    public IReadOnlyList<AgentSession> Sessions { get; }

    private ActivitySummary(Kind state, IReadOnlyList<AgentSession> sessions)
    {
        State = state;
        Sessions = sessions;
    }

    public static ActivitySummary? From(IReadOnlyList<AgentSession> sessions)
    {
        if (sessions.Count == 0) return null;
        Kind state;
        if (sessions.Any(s => s.State == AgentState.Waiting)) state = Kind.Waiting;
        else if (sessions.Any(s => s.State == AgentState.Busy)) state = Kind.Working;
        else state = Kind.Idle;
        return new ActivitySummary(state, sessions);
    }

    public Color Color => State switch
    {
        Kind.Working => Palette.TextPrimary,
        Kind.Waiting => Palette.Watch,
        _ => Palette.RingTrack
    };

    public Brush Brush => State switch
    {
        Kind.Working => Palette.TextPrimaryBrush,
        Kind.Waiting => Palette.WatchBrush,
        _ => Palette.RingTrackBrush
    };
}
