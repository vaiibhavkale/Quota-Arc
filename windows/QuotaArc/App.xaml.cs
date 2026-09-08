using System.Windows;
using System.Windows.Threading;
using QuotaArc.AppHost;
using QuotaArc.Model;
using QuotaArc.Overlay;
using QuotaArc.Providers;
using QuotaArc.Sessions;
using QuotaArc.Settings;
using QuotaArc.SettingsUi;

namespace QuotaArc;

public partial class App : Application
{
    private SingleInstanceGate? _gate;
    private NotchFleet? _fleet;
    private UsageStore? _store;
    private Preferences? _preferences;
    private SettingsWindow? _settings;
    private AppHubWindow? _hub;
    private TrayIcon? _tray;
    private ThresholdNotifier? _notifier;
    private SessionCompletionWatcher _completions = new();
    private readonly Dictionary<string, Func<List<AgentSession>>> _monitors = [];
    private DispatcherTimer? _sessionTimer;
    private FileSystemWatcher? _claudeWatch;
    private DispatcherTimer? _claudeDebounce;
    private DateTime? _claudeHistoryStamp;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception.ToString());
            args.Handled = true;
        };
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppSettings.MigrateFromPreviousInstall();

        var argsList = e.Args;
        var wantsWelcome = argsList.Contains("--welcome", StringComparer.OrdinalIgnoreCase);
        var wantsSettings = argsList.Contains("--settings", StringComparer.OrdinalIgnoreCase);
        var quietStart = argsList.Contains("--quiet", StringComparer.OrdinalIgnoreCase);

        _gate = SingleInstanceGate.TryStart(() =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                try { ShowHub(); }
                catch (Exception ex) { Log.Error("show hub: " + ex.Message); }
            });
        });
        if (_gate is null)
        {
            Shutdown();
            return;
        }
        var preferences = new Preferences();
        _preferences = preferences;
        var fleet = new NotchFleet(preferences.NotchScope, preferences.NotchEdge);
        _fleet = fleet;
        fleet.ApplyAlongOffset(preferences.Offset(preferences.NotchEdge));
        fleet.Apply(preferences.ResetTimeFormat);
        fleet.Apply(preferences.AccentColor);
        fleet.Apply(preferences.DisplayPreference);
        fleet.Apply(preferences.NotchVisibility);

        if (Environment.GetEnvironmentVariable("QUOTAARC_DEMO") == "1")
        {
            fleet.SetSnapshots(Fixtures.Snapshots());
        }
        else
        {
            var profiles = ClaudeProfile.Discover();
            Log.Info("claude profiles: " + string.Join(", ", profiles.Select(p => p.DisplayPath)));

            var store = new UsageStore(
                profiles.Select(p => (IUsageProvider)new ClaudeOAuthProvider(p))
                    .Concat<IUsageProvider>([
                        new CursorLocalProvider(),
                        new CodexLocalProvider(),
                        new AntigravityProvider()
                    ]),
                preferences.DisconnectedProviders,
                preferences.ProviderOrder);
            _store = store;

            var settings = new SettingsWindow(
                preferences,
                () => store.ProviderSummaries,
                store.SignOut,
                store.SignIn,
                store.Reauthorize);
            _settings = settings;

            var hub = new AppHubWindow(
                () => settings.Show(),
                () => fleet.RevealNotch(),
                KeepTrayVisible);
            _hub = hub;

            fleet.OnOpenSettings = () =>
            {
                try { settings.Show(); }
                catch (Exception ex) { Log.Error("settings: " + ex.Message); }
            };

            _tray = new TrayIcon(
                () => ShowHub(),
                () => settings.Show(),
                Shutdown);
            KeepTrayVisible();
            ApplyPresence(preferences.AppPresence);

            preferences.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(Preferences.AppPresence):
                        ApplyPresence(preferences.AppPresence);
                        break;
                    case nameof(Preferences.NotchVisibility):
                        fleet.Apply(preferences.NotchVisibility);
                        break;
                    case nameof(Preferences.NotchEdge):
                        fleet.ApplyAlongOffset(preferences.Offset(preferences.NotchEdge));
                        fleet.Apply(preferences.NotchEdge);
                        break;
                    case nameof(Preferences.NotchScope):
                        fleet.Apply(preferences.NotchScope);
                        break;
                    case nameof(Preferences.DisplayPreference):
                        fleet.Apply(preferences.DisplayPreference);
                        break;
                    case nameof(Preferences.ResetTimeFormat):
                        fleet.Apply(preferences.ResetTimeFormat);
                        break;
                    case nameof(Preferences.AccentColor):
                        fleet.Apply(preferences.AccentColor);
                        break;
                    case nameof(Preferences.DisconnectedProviders):
                        store.Disconnected = preferences.DisconnectedProviders;
                        break;
                    case nameof(Preferences.ProviderOrder):
                        store.Order = [.. preferences.ProviderOrder];
                        break;
                }
            };

            _notifier = new ThresholdNotifier(
                preferences.IsMutedAlerts,
                alert => Dispatcher.Invoke(() => DeliverAlert(alert)));

            store.Changed += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    fleet.SetSnapshots(store.Snapshots);
                    fleet.SetRefreshing(store.Refreshing);
                    _notifier?.Observe(store.Snapshots);
                });
            };
            store.Start();
            fleet.OnRefresh = () => _ = store.RefreshNowAsync();
            fleet.OnUnfold = () =>
            {
                // Claude's token read and Antigravity's language-server bridge
                // are both local and cheap — a subprocess or a loopback call,
                // never a rate-limited cloud endpoint — so re-reading them on
                // every unfold costs nothing and keeps the ring matching what
                // the account shows right now, the same as Mac. Every other
                // provider still goes through RefreshIfStaleAsync's cooldown:
                // those hit real external APIs, and refreshing on every hover
                // would be how you get rate limited by your own notch.
                _ = store.RefreshProviderAsync("claude");
                _ = store.RefreshProviderAsync("gemini");
                _ = store.RefreshIfStaleAsync();
            };
            fleet.OnRefreshProvider = id => _ = store.RefreshProviderAsync(id);
            fleet.OnReposition = offset => preferences.SetOffset(offset, preferences.NotchEdge);
            WatchClaudeDesktop(store);
            _claudeHistoryStamp = ClaudeDesktop.HistoryStamp();

            foreach (var profile in profiles)
            {
                var dir = profile.SessionsDirectory;
                _monitors[profile.Id] = () => ClaudeSessionMonitor.Read(dir);
            }
            _monitors["cursor"] = () => ProcessActivityMonitor.IfRunning("cursor", "Cursor", "Cursor", "cursor");
            _monitors["codex"] = () => ProcessActivityMonitor.IfRunning("codex", "Codex", "codex");
            _monitors["gemini"] = () => ProcessActivityMonitor.IfRunning("gemini", "Antigravity", "Antigravity", "antigravity");
            store.IsBusy = () => _monitors.Values.SelectMany(m => m()).Any(s => s.State == AgentState.Busy);
            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _sessionTimer.Tick += (_, _) => RefreshSessions();
            _sessionTimer.Start();
            RefreshSessions();

            if (wantsSettings)
                Dispatcher.InvokeAsync(() => settings.Show());
            else if (!quietStart && (wantsWelcome || preferences.IsFirstLaunch))
                Dispatcher.InvokeAsync(() => ShowHub(notifyTray: preferences.IsFirstLaunch));
            else if (!quietStart)
                Dispatcher.InvokeAsync(() => ShowHub());
        }

        fleet.OnQuit = Shutdown;
        fleet.Show();
        base.OnStartup(e);
    }

    private void ShowHub(bool notifyTray = false)
    {
        if (_hub is null) return;
        try
        {
            KeepTrayVisible();
            _hub.Show();
            if (notifyTray && _tray is not null)
            {
                _tray.Notify(
                    AppBranding.Name + " is running",
                    "Look for the Quota Arc icon near the clock, or move to the screen edge to see usage.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("hub: " + ex.Message);
        }
    }

    private void KeepTrayVisible()
    {
        if (_preferences?.AppPresence == AppPresence.Hidden) return;
        _tray?.Show();
    }

    private void WatchClaudeDesktop(UsageStore store)
    {
        var dir = ClaudeDesktop.AppData;
        if (!Directory.Exists(dir)) return;
        _claudeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _claudeDebounce.Tick += (_, _) =>
        {
            _claudeDebounce.Stop();
            _claudeHistoryStamp = ClaudeDesktop.HistoryStamp();
            _ = store.RefreshProviderAsync("claude");
        };
        try
        {
            _claudeWatch = new FileSystemWatcher(dir)
            {
                Filter = "plan-usage-history.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            FileSystemEventHandler bump = (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _claudeDebounce?.Stop();
                    _claudeDebounce?.Start();
                });
            };
            _claudeWatch.Changed += bump;
            _claudeWatch.Created += bump;
            _claudeWatch.Renamed += (_, ev) => bump(_, ev);
            _claudeWatch.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Log.Error("claude desktop watch: " + ex.Message);
        }
    }

    private void RefreshSessions()
    {
        if (_fleet is null) return;
        var next = new Dictionary<string, List<AgentSession>>();
        foreach (var (id, read) in _monitors)
            next[id] = read();
        _fleet.SetSessions(next);
        AnnounceCompletions(next);
        var stamp = ClaudeDesktop.HistoryStamp();
        if (stamp != _claudeHistoryStamp && _store is not null)
        {
            _claudeHistoryStamp = stamp;
            _ = _store.RefreshProviderAsync("claude");
        }
    }

    private void AnnounceCompletions(Dictionary<string, List<AgentSession>> sessions)
    {
        var events = _completions.Absorb(sessions);
        if (events.Count == 0 || _preferences is null || _fleet is null) return;
        var ev = events[0];
        if (_preferences.SessionEndSound)
        {
            if (ev.Reason == SessionCompletionReason.Blocked)
                System.Media.SystemSounds.Exclamation.Play();
            else
                System.Media.SystemSounds.Asterisk.Play();
        }
        if (!_preferences.AnnounceSessionEnd) return;
        _fleet.Peek(_preferences.PeekDuration.Seconds());
    }

    private void DeliverAlert(ThresholdAlert alert)
    {
        if (_tray is null) return;
        _tray.Show();
        var title = alert.Threshold >= 100
            ? $"{alert.ProviderName} limit reached"
            : $"{alert.ProviderName} is at {alert.UsedPercent}%";
        var body = alert.Threshold >= 100
            ? $"Its {alert.WindowLabel.ToLowerInvariant()} limit is spent."
            : $"{alert.UsedPercent}% of its {alert.WindowLabel.ToLowerInvariant()} limit used.";
        _tray.Notify(title, body);
    }

    private void ApplyPresence(AppPresence presence)
    {
        MainWindow ??= new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = presence == AppPresence.Taskbar,
            Opacity = 0,
            AllowsTransparency = true,
            ShowActivated = false,
            Title = AppBranding.Name
        };
        MainWindow.ShowInTaskbar = presence == AppPresence.Taskbar;
        if (presence == AppPresence.Taskbar)
        {
            MainWindow.Show();
            MainWindow.Hide();
            MainWindow.ShowInTaskbar = true;
        }
        else
        {
            MainWindow.ShowInTaskbar = false;
            MainWindow.Hide();
        }
        if (presence.WantsTray()) KeepTrayVisible(); else _tray?.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sessionTimer?.Stop();
        _claudeDebounce?.Stop();
        _claudeWatch?.Dispose();
        _store?.Stop();
        _fleet?.Stop();
        _hub?.ForceClose();
        _tray?.Dispose();
        _gate?.Dispose();
        base.OnExit(e);
    }
}
