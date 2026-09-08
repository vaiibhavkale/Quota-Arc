namespace QuotaArc.Model;

internal sealed record ThresholdAlert(
    int Threshold,
    string ProviderId,
    string ProviderName,
    string WindowLabel,
    int UsedPercent,
    DateTime? ResetsAt);

internal sealed class ThresholdNotifier
{
    private readonly Dictionary<string, int> _crossed = [];
    private readonly Func<string, bool> _isMuted;
    private readonly Action<ThresholdAlert> _deliver;

    public ThresholdNotifier(Func<string, bool> isMuted, Action<ThresholdAlert> deliver)
    {
        _isMuted = isMuted;
        _deliver = deliver;
    }

    public void Observe(IReadOnlyList<ProviderSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
            Observe(snapshot);
    }

    private void Observe(ProviderSnapshot snapshot)
    {
        if (snapshot.UsedFraction is not { } fraction) return;
        var percent = fraction * 100;
        var level = percent >= 100 ? 100 : percent >= 80 ? 80 : 0;

        var previous = _crossed.GetValueOrDefault(snapshot.Id);
        _crossed[snapshot.Id] = level;
        if (level <= previous || _isMuted(snapshot.Id)) return;
        if (snapshot.Headline is not { } headline) return;

        foreach (var threshold in new[] { 80, 100 })
        {
            if (threshold <= previous || threshold > level) continue;
            _deliver(new ThresholdAlert(
                threshold,
                snapshot.Id,
                snapshot.DisplayName,
                headline.Label,
                (int)Math.Round(percent),
                headline.ResetsAt));
        }
    }
}
