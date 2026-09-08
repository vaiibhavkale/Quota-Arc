using QuotaArc.Providers;

namespace QuotaArc.Model;

internal sealed class UsageStore
{
    public event Action? Changed;

    public List<ProviderSnapshot> Snapshots { get; private set; } = [];
    public HashSet<string> Refreshing { get; private set; } = [];
    public HashSet<string> RefusedAccess { get; private set; } = [];

    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly Dictionary<string, Guid> _connectionVersions = [];
    private HashSet<string> _disconnected;
    private List<string> _order = [];
    private readonly double _refreshInterval;
    private readonly double _staleAfter;
    private readonly double _idleRefreshInterval;
    private readonly UsageArchive _archive;
    private Dictionary<string, (ProviderSnapshot Snapshot, DateTime FetchedAt)> _lastGood;
    private CancellationTokenSource? _loop;
    private DateTime? _lastAttempt;
    private bool _isRefreshing;

    public Func<bool> IsBusy { get; set; } = () => false;

    public HashSet<string> Disconnected
    {
        get => _disconnected;
        set
        {
            if (_disconnected.SetEquals(value)) return;
            var previous = _disconnected;
            var added = value.Except(previous).ToList();
            _disconnected = [.. value];
            foreach (var id in added.Concat(previous.Except(value)))
                _connectionVersions[id] = Guid.NewGuid();
            Snapshots.RemoveAll(s => _disconnected.Contains(s.Id));
            RefusedAccess.ExceptWith(_disconnected);
            foreach (var id in _disconnected) _lastGood.Remove(id);
            _archive.Save(_lastGood);
            _ = RefreshNowAsync();
            Changed?.Invoke();
        }
    }

    public List<string> Order
    {
        get => _order;
        set
        {
            if (_order.SequenceEqual(value)) return;
            _order = [.. value];
            Snapshots = ProviderOrder.Arrange(Snapshots, _order, s => s.Id);
            Changed?.Invoke();
        }
    }

    private IEnumerable<IUsageProvider> OrderedProviders =>
        ProviderOrder.Arrange(_providers, _order, p => p.Id);

    public UsageStore(
        IEnumerable<IUsageProvider> providers,
        IEnumerable<string>? disconnected = null,
        IEnumerable<string>? order = null,
        double refreshInterval = 60,
        double idleRefreshInterval = 5 * 60,
        double staleAfter = 15 * 60,
        UsageArchive? archive = null)
    {
        _providers = providers.ToList();
        _refreshInterval = refreshInterval;
        _idleRefreshInterval = idleRefreshInterval;
        _staleAfter = staleAfter;
        _archive = archive ?? new UsageArchive();
        _disconnected = [.. disconnected ?? []];
        _order = [.. order ?? []];
        _lastGood = _archive.Load();
        foreach (var id in _disconnected) _lastGood.Remove(id);
        Snapshots = OrderedProviders.Where(p => !_disconnected.Contains(p.Id)).Select(p =>
        {
            if (!_lastGood.TryGetValue(p.Id, out var remembered))
                return Placeholder(p);
            return remembered.Snapshot with { Status = new ProviderStatus.Stale(remembered.FetchedAt) };
        }).ToList();
    }

    public IReadOnlyList<ProviderSummary> ProviderSummaries =>
        OrderedProviders.Select(p => new ProviderSummary(
            p.Id, p.DisplayName, p.Glyph,
            _disconnected.Contains(p.Id) ? null : p.Account(),
            p.SignInRoute,
            RefusedAccess.Contains(p.Id))).ToList();

    public void Start()
    {
        _loop = new CancellationTokenSource();
        _ = LoopAsync(_loop.Token);
        _ = RefreshNowAsync();
    }

    public void Stop()
    {
        _loop?.Cancel();
        _loop = null;
    }

    public Task RefreshNowAsync() => RefreshAllAsync();

    /// The notch unfolding on Mac and Windows alike should feel instant: the
    /// same behaviour the Mac build now has, so a ring on either platform
    /// never sits on "Waiting for the first reading..." for the rest of a
    /// 15-second cooldown just because that cooldown started on the one
    /// attempt that failed.
    public Task RefreshIfStaleAsync(double minSeconds = 15)
    {
        var awaitingFirstReading = Snapshots.Any(s => !s.HasReading && !_disconnected.Contains(s.Id));
        if (!ShouldRefreshOnUnfold(
                sinceLastAttempt: _lastAttempt is { } t ? (DateTime.Now - t).TotalSeconds : double.MaxValue,
                minSeconds: minSeconds,
                awaitingFirstReading: awaitingFirstReading))
        {
            return Task.CompletedTask;
        }
        return RefreshAllAsync();
    }

    public static bool ShouldRefreshOnUnfold(double sinceLastAttempt, double minSeconds, bool awaitingFirstReading) =>
        awaitingFirstReading || sinceLastAttempt >= minSeconds;

    public async Task RefreshProviderAsync(string providerId)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null || _disconnected.Contains(providerId) || Refreshing.Contains(providerId))
            return;
        Refreshing.Add(providerId);
        Changed?.Invoke();
        var version = VersionOf(providerId);
        try
        {
            var fresh = await SnapshotFromAsync(provider, version);
            if (fresh is null) return;
            var index = Snapshots.FindIndex(s => s.Id == providerId);
            if (index >= 0) Snapshots[index] = fresh;
            else Snapshots.Add(fresh);
            _lastAttempt = DateTime.Now;
            await Task.Delay(380);
        }
        finally
        {
            Refreshing.Remove(providerId);
            Changed?.Invoke();
        }
    }

    public void SignOut(string providerId)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null) return;
        _connectionVersions[providerId] = Guid.NewGuid();
        RefusedAccess.Remove(providerId);
        Snapshots.RemoveAll(s => s.Id == providerId);
        _lastGood.Remove(providerId);
        _archive.Forget(providerId);
        Changed?.Invoke();
        _ = provider.SignOutAsync();
    }

    public bool SignIn(string providerId)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null) return false;
        if (provider.Account() is not null)
        {
            _ = RefreshProviderAsync(providerId);
            return true;
        }
        return OpenAccountSource(providerId);
    }

    public void Reauthorize(string providerId)
    {
        _providers.FirstOrDefault(p => p.Id == providerId)?.ForgetCachedCredential();
        _ = RefreshProviderAsync(providerId);
    }

    public bool OpenAccountSource(string providerId)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null) return false;
        switch (provider.SignInRoute)
        {
            case SignInRoute.OpenApp app:
                return QuotaArc.Launch.App(app.Name);
            case SignInRoute.Guidance:
                return false;
            default:
                return false;
        }
    }

    public static bool ShouldRefresh(bool isBusy, double sinceLastAttempt, double idleInterval) =>
        isBusy || sinceLastAttempt >= idleInterval;

    public static bool SupersedesHistory(ProviderStatus status) => status is
        ProviderStatus.NeedsAuth or ProviderStatus.Unsupported;

    public static ProviderStatus StatusFor(Exception error) => error switch
    {
        UsageProviderException { Kind: UsageErrorKind.NeedsAuth } => new ProviderStatus.NeedsAuth(),
        UsageProviderException { Kind: UsageErrorKind.CredentialExpired } =>
            new ProviderStatus.Stale(DateTime.Now),
        UsageProviderException { Kind: UsageErrorKind.RateLimited } =>
            new ProviderStatus.Stale(DateTime.Now),
        UsageProviderException { Kind: UsageErrorKind.AccessDenied } => new ProviderStatus.AccessDenied(),
        UsageProviderException { Kind: UsageErrorKind.NothingMetered, Message: var why } =>
            new ProviderStatus.Unsupported(why),
        UsageProviderException { Kind: UsageErrorKind.BadResponse, Status: var code } =>
            new ProviderStatus.Error($"HTTP {code}"),
        _ => new ProviderStatus.Error(error.Message)
    };

    private async Task LoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_refreshInterval));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var waited = _lastAttempt is { } t
                    ? (DateTime.Now - t).TotalSeconds
                    : double.PositiveInfinity;
                if (ShouldRefresh(IsBusy(), waited, _idleRefreshInterval))
                    await RefreshAllAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshAllAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        _lastAttempt = DateTime.Now;
        try
        {
            var live = OrderedProviders.Where(p => !_disconnected.Contains(p.Id)).ToList();
            var versions = live.ToDictionary(p => p.Id, p => VersionOf(p.Id));
            Refreshing = live.Select(p => p.Id).ToHashSet();
            Changed?.Invoke();
            var next = new List<ProviderSnapshot>();
            foreach (var provider in live)
            {
                var fresh = await SnapshotFromAsync(provider, versions[provider.Id]);
                if (fresh is not null) next.Add(fresh);
            }
            Snapshots = next.Where(s => IsCurrent(s.Id, versions.GetValueOrDefault(s.Id))).ToList();
        }
        finally
        {
            Refreshing = [];
            _isRefreshing = false;
            Changed?.Invoke();
        }
    }

    private Guid VersionOf(string id)
    {
        if (!_connectionVersions.TryGetValue(id, out var v))
        {
            v = Guid.Empty;
            _connectionVersions[id] = v;
        }
        return v;
    }

    private bool IsCurrent(string id, Guid version) =>
        !_disconnected.Contains(id) && VersionOf(id) == version;

    private async Task<ProviderSnapshot?> SnapshotFromAsync(IUsageProvider provider, Guid version)
    {
        if (!IsCurrent(provider.Id, version)) return null;
        try
        {
            var fresh = await provider.FetchSnapshotAsync();
            if (!IsCurrent(provider.Id, version)) return null;
            _lastGood[provider.Id] = (fresh, DateTime.Now);
            _archive.Save(_lastGood);
            RefusedAccess.Remove(provider.Id);
            return fresh;
        }
        catch (Exception ex)
        {
            if (!IsCurrent(provider.Id, version)) return null;
            QuotaArc.Log.Error($"{provider.Id} failed: {ex.Message}");
            return Degraded(provider, ex);
        }
    }

    private ProviderSnapshot Degraded(IUsageProvider provider, Exception error)
    {
        var status = StatusFor(error);
        if (status is ProviderStatus.AccessDenied) RefusedAccess.Add(provider.Id);
        else RefusedAccess.Remove(provider.Id);

        if (SupersedesHistory(status))
        {
            _lastGood.Remove(provider.Id);
            _archive.Save(_lastGood);
            return Placeholder(provider) with { Status = status };
        }

        if (!_lastGood.TryGetValue(provider.Id, out var previous))
            return Placeholder(provider) with { Status = status };

        var age = (DateTime.Now - previous.FetchedAt).TotalSeconds;
        return previous.Snapshot with
        {
            Status = age > _staleAfter
                ? new ProviderStatus.Stale(previous.FetchedAt)
                : previous.Snapshot.Status
        };
    }

    private static ProviderSnapshot Placeholder(IUsageProvider provider) =>
        new(provider.Id, provider.DisplayName, provider.Glyph, Fidelity.Official,
            new ProviderStatus.Stale(DateTime.MinValue), []);
}
