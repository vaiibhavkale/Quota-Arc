namespace QuotaArc.Providers;

internal sealed class CredentialCache<T>
{
    private readonly object _gate = new();
    private T? _stored;
    private DateTime? _attemptedStamp;
    private DateTime? _attemptedAt;
    private Exception? _lastError;
    private readonly Func<T, bool> _isExpired;
    private readonly double _recheckAfter;
    private readonly double _retryAfterFailure;
    private readonly Func<DateTime> _now;

    public CredentialCache(
        Func<T, bool> isExpired,
        double recheckAfter = 5 * 60,
        double retryAfterFailure = 5 * 60,
        Func<DateTime>? now = null)
    {
        _isExpired = isExpired;
        _recheckAfter = recheckAfter;
        _retryAfterFailure = retryAfterFailure;
        _now = now ?? (() => DateTime.Now);
    }

    public T Value(Func<DateTime?> itemModifiedAt, Func<T> reload)
    {
        T? held;
        DateTime? askedStamp;
        DateTime? askedAt;
        Exception? failure;
        lock (_gate)
        {
            held = _stored;
            askedStamp = _attemptedStamp;
            askedAt = _attemptedAt;
            failure = _lastError;
        }

        if (held is not null && !_isExpired(held)) return held;

        var current = itemModifiedAt();
        var alreadyAsked = false;
        if (current is { } cur && askedStamp is { } stamp)
            alreadyAsked = cur == stamp;
        else if (askedAt is { } at)
        {
            var wait = failure is null ? _recheckAfter : _retryAfterFailure;
            alreadyAsked = (_now() - at).TotalSeconds < wait;
        }

        if (alreadyAsked)
        {
            if (held is not null) return held;
            if (failure is not null) throw failure;
        }

        try
        {
            var fresh = reload();
            lock (_gate)
            {
                _stored = fresh;
                _attemptedStamp = current;
                _attemptedAt = _now();
                _lastError = null;
            }
            return fresh;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _attemptedStamp = current;
                _attemptedAt = _now();
                _lastError = ex;
            }
            throw;
        }
    }

    public void Forget()
    {
        lock (_gate)
        {
            _stored = default;
            _attemptedStamp = null;
            _attemptedAt = null;
            _lastError = null;
        }
    }
}
