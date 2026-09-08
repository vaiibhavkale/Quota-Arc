using QuotaArc;

namespace QuotaArc.AppHost;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly CancellationTokenSource? _cancel;
    private readonly Action? _onShowRequested;

    public bool IsFirstInstance { get; }

    private SingleInstanceGate(bool isFirst, Mutex? mutex, EventWaitHandle? showEvent, Action? onShowRequested)
    {
        IsFirstInstance = isFirst;
        _mutex = mutex;
        _showEvent = showEvent;
        _onShowRequested = onShowRequested;
        if (isFirst)
            _cancel = new CancellationTokenSource();
    }

    public static SingleInstanceGate? TryStart(Action onShowRequested)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(true, AppBranding.MutexName, out var created);
            if (!created)
            {
                try
                {
                    if (!mutex.WaitOne(0))
                    {
                        AskRunningInstanceToShow();
                        mutex.Dispose();
                        return null;
                    }
                    created = true;
                }
                catch (AbandonedMutexException)
                {
                    created = true;
                }
            }

            if (!created)
            {
                AskRunningInstanceToShow();
                mutex.Dispose();
                return null;
            }
        }
        catch (UnauthorizedAccessException)
        {
            mutex?.Dispose();
            AskRunningInstanceToShow();
            return null;
        }
        catch (AbandonedMutexException)
        {
            mutex ??= new Mutex(true, AppBranding.MutexName, out _);
        }

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppBranding.ShowEventName);
        var gate = new SingleInstanceGate(true, mutex, showEvent, onShowRequested);
        gate.StartListener();
        return gate;
    }

    private static void AskRunningInstanceToShow()
    {
        try
        {
            using var show = EventWaitHandle.OpenExisting(AppBranding.ShowEventName);
            show.Set();
        }
        catch
        {
            try
            {
                using var created = new EventWaitHandle(true, EventResetMode.AutoReset, AppBranding.ShowEventName);
            }
            catch { /* first instance may still be starting */ }
        }
    }

    private void StartListener()
    {
        if (_showEvent is null || _onShowRequested is null || _cancel is null) return;
        var token = _cancel.Token;
        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_showEvent.WaitOne(500))
                        continue;
                    try { _onShowRequested(); }
                    catch { /* never take down the running instance */ }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (AbandonedMutexException)
                {
                    break;
                }
                catch
                {
                    if (token.IsCancellationRequested) break;
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        try { _showEvent?.Dispose(); } catch { /* ignore */ }
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { /* ignore */ }
    }
}
