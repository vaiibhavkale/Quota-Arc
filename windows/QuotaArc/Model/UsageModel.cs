using QuotaArc;
using QuotaArc.Providers;

namespace QuotaArc.Model;

internal enum Fidelity
{
    Official,
    Derived,
    Manual
}

internal static class FidelityInfo
{
    public static string Qualifier(this Fidelity f) => f == Fidelity.Official ? "" : "~";
}

internal abstract record ProviderStatus
{
    public sealed record Ok : ProviderStatus;
    public sealed record Stale(DateTime Since) : ProviderStatus;
    public sealed record NeedsAuth : ProviderStatus;
    public sealed record AccessDenied : ProviderStatus;
    public sealed record Unsupported(string Why) : ProviderStatus;
    public sealed record Error(string Why) : ProviderStatus;

    public bool IsStale => this is Stale;
    public DateTime? StaleSince => this is Stale s ? s.Since : null;
}

internal sealed record LimitWindow(
    string Id,
    string Label,
    double? UsedFraction = null,
    int? Remaining = null,
    int? Used = null,
    DateTime? ResetsAt = null)
{
    public string Summary
    {
        get
        {
            if (UsedFraction is { } frac)
            {
                var (usedText, leftText) = Percent.Halves(frac);
                return $"{usedText}% Used · {leftText}% left";
            }
            if (Remaining is { } left)
                return left == 1 ? "1 left" : $"{left} left";
            if (Used is { } spent)
                return spent == 1 ? "1 used" : $"{spent} used";
            return "No reading";
        }
    }
}

internal sealed record UsageBlock(string Reason, DateTime? ResetsAt)
{
    public string Summary(DateTime now)
    {
        if (ResetsAt is not { } at || at <= now) return Reason;
        var formatter = ResetCopy.Formatter(TimeZoneInfo.Local);
        var days = ResetCopy.DaysApart(now, at);
        formatter.DateFormat = days >= 1 ? "ddd h:mm tt" : "h:mm tt";
        return $"{Reason} until {formatter.Format(at)}";
    }
}

internal sealed record ProviderSnapshot(
    string Id,
    string DisplayName,
    ProviderGlyph Glyph,
    Fidelity Fidelity,
    ProviderStatus Status,
    IReadOnlyList<LimitWindow> Windows,
    string? HeadlineId = null,
    UsageBlock? Block = null)
{
    public LimitWindow? Headline
    {
        get
        {
            if (HeadlineId is null) return Windows.FirstOrDefault();
            return Windows.FirstOrDefault(w => w.Id == HeadlineId);
        }
    }

    public double? UsedFraction => Headline?.UsedFraction;

    public string HeadlineText
    {
        get
        {
            if (UsedFraction is { } f) return $"{Percent.Text(f)}%";
            if (Headline?.Remaining is { } left) return $"{left}";
            if (Headline?.Used is { } used) return $"{used}";
            return "—";
        }
    }

    public bool HasReading => Windows.Count > 0;
    public double? RingFraction => UsedFraction;

    public string? StatusMessage
    {
        get
        {
            if (HasReading) return null;
            return Status switch
            {
                ProviderStatus.NeedsAuth => AuthPrompt,
                ProviderStatus.AccessDenied =>
                    $"{AppBranding.Name} was refused access to {DisplayName}'s saved login. Click this ring to ask again.",
                ProviderStatus.Unsupported u => u.Why,
                ProviderStatus.Error e => $"Couldn't read usage — {e.Why}",
                _ => "Waiting for the first reading…"
            };
        }
    }

    private string AuthPrompt => Id switch
    {
        "claude" => ClaudeDesktop.IsPresent()
            ? $"Open Claude so {AppBranding.Name} can read your usage"
            : "Sign in to Claude Code to read your usage",
        _ when ClaudeProfile.IsClaude(Id) =>
            $"Sign in to Claude Code in ~/.claude-{ClaudeProfile.SlugFromProviderId(Id)} to read your usage",
        "cursor" => "Sign in to Cursor in the editor",
        "codex" => "Sign in to Codex to read your usage",
        "gemini" => "Sign in to Antigravity to read your usage",
        _ => $"Sign in to {DisplayName} to read your usage"
    };
}
