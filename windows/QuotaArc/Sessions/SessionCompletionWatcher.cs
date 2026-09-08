namespace QuotaArc.Sessions;

internal readonly record struct SessionCompletionEvent(
    AgentSession Session,
    SessionCompletionReason Reason,
    string ProviderId);

internal enum SessionCompletionReason
{
    Finished,
    Blocked
}

internal struct SessionCompletionWatcher
{
    private Dictionary<string, AgentState> _previous = [];
    private bool _hasSeeded;

    public SessionCompletionWatcher() { }

    public List<SessionCompletionEvent> Absorb(IReadOnlyDictionary<string, List<AgentSession>> sessions)
    {
        var current = new Dictionary<string, AgentState>();
        var events = new List<SessionCompletionEvent>();

        foreach (var (providerId, live) in sessions)
        {
            foreach (var session in live)
            {
                var key = $"{providerId}\u0001{session.Id}";
                current[key] = session.State;
                if (!_hasSeeded || !_previous.TryGetValue(key, out var was)) continue;
                var reason = Reason(was, session.State);
                if (reason is null) continue;
                events.Add(new SessionCompletionEvent(session, reason.Value, providerId));
            }
        }

        _previous = current;
        _hasSeeded = true;
        return [.. events.OrderByDescending(e => e.Session.Since)];
    }

    public static SessionCompletionReason? Reason(AgentState was, AgentState now)
    {
        if (was != AgentState.Busy) return null;
        return now switch
        {
            AgentState.Idle => SessionCompletionReason.Finished,
            AgentState.Waiting => SessionCompletionReason.Blocked,
            _ => null
        };
    }
}
