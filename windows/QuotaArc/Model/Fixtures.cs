using QuotaArc.Providers;

namespace QuotaArc.Model;

internal static class Fixtures
{
    public static List<ProviderSnapshot> Snapshots(DateTime? now = null)
    {
        var n = now ?? DateTime.Now;
        var sessionReset = n.AddMinutes(51);
        var midnight = n.Date.AddDays(1);
        return
        [
            new ProviderSnapshot(
                "claude", "Claude", ProviderGlyph.Claude, Fidelity.Official,
                new ProviderStatus.Ok(),
                [
                    new LimitWindow("session", "Current session", 0.73, ResetsAt: sessionReset),
                    new LimitWindow("weekly_all", "All models", 0.07, ResetsAt: midnight)
                ], "session"),
            new ProviderSnapshot(
                "cursor", "Cursor", ProviderGlyph.Cursor, Fidelity.Official,
                new ProviderStatus.Ok(),
                [new LimitWindow("included", "Included usage", 0.52, ResetsAt: midnight)],
                "included"),
            new ProviderSnapshot(
                "codex", "Codex", ProviderGlyph.Openai, Fidelity.Official,
                new ProviderStatus.Ok(),
                [
                    new LimitWindow("primary", "5h limit", 0.21, ResetsAt: n.AddHours(3)),
                    new LimitWindow("secondary", "Weekly limit", 0.34, ResetsAt: n.AddDays(4))
                ], "primary"),
            new ProviderSnapshot(
                "gemini", "Antigravity", ProviderGlyph.Antigravity, Fidelity.Official,
                new ProviderStatus.Ok(),
                [new LimitWindow("requests", "Requests today", Used: 18)])
        ];
    }
}
