using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuotaArc;
using QuotaArc.Model;
using QuotaArc.Notch;
using Microsoft.Win32;

namespace QuotaArc.Settings;

internal sealed class Preferences : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private HashSet<string> _disconnected;
    private HashSet<string> _mutedAlerts;
    private NotchVisibility _notchVisibility;
    private NotchEdge _notchEdge;
    private AppPresence _appPresence;
    private NotchScreenScope _notchScope;
    private DisplayPreference _displayPreference;
    private ResetTimeFormat _resetTimeFormat;
    private AccentColorChoice _accentColor;
    private List<string> _providerOrder;
    private bool _announceSessionEnd;
    private bool _sessionEndSound;
    private PeekDuration _peekDuration;
    private bool _launchAtLogin;
    private string? _launchAtLoginProblem;

    public bool IsFirstLaunch { get; }

    public Preferences()
    {
        IsFirstLaunch = !AppSettings.GetBool("hasLaunchedBefore");
        AppSettings.SetBool("hasLaunchedBefore", true);
        _disconnected = [.. AppSettings.GetList("hiddenProviders")];
        _mutedAlerts = [.. AppSettings.GetList("mutedAlertProviders")];
        _notchVisibility = NotchVisibilityInfo.Parse(AppSettings.Get("notchVisibility"));
        _appPresence = AppPresenceInfo.Parse(AppSettings.Get("appPresence"));
        _notchEdge = NotchEdgeInfo.Parse(AppSettings.Get("notchEdge")) ?? NotchEdge.Right;
        _notchScope = NotchScreenScopeInfo.Parse(AppSettings.Get("notchScope"));
        _displayPreference = DisplayPreference.Parse(AppSettings.Get("displayPreference"));
        _resetTimeFormat = ResetTimeFormatInfo.Parse(AppSettings.Get("resetTimeFormat"));
        _accentColor = AccentColorInfo.Parse(AppSettings.Get("accentColor"));
        _providerOrder = [.. AppSettings.GetList("providerOrder")];
        _announceSessionEnd = AppSettings.GetBoolOrDefault("announceSessionEnd", true);
        _sessionEndSound = AppSettings.GetBoolOrDefault("sessionEndSound", true);
        _peekDuration = PeekDurationInfo.Parse(AppSettings.Get("peekDuration"));
        _launchAtLogin = IsRegisteredForLogin();
    }

    public HashSet<string> DisconnectedProviders
    {
        get => _disconnected;
        set
        {
            _disconnected = value;
            AppSettings.SetList("hiddenProviders", value);
            Raise();
        }
    }

    public HashSet<string> MutedAlertProviders
    {
        get => _mutedAlerts;
        private set
        {
            _mutedAlerts = value;
            AppSettings.SetList("mutedAlertProviders", value);
            Raise();
        }
    }

    public NotchVisibility NotchVisibility
    {
        get => _notchVisibility;
        set
        {
            if (_notchVisibility == value) return;
            _notchVisibility = value;
            AppSettings.Set("notchVisibility", value.Raw());
            Raise();
        }
    }

    public NotchEdge NotchEdge
    {
        get => _notchEdge;
        set
        {
            if (_notchEdge == value) return;
            _notchEdge = value;
            AppSettings.Set("notchEdge", value.RawValue());
            Raise();
        }
    }

    public AppPresence AppPresence
    {
        get => _appPresence;
        set
        {
            if (_appPresence == value) return;
            _appPresence = value;
            AppSettings.Set("appPresence", value.Raw());
            Raise();
        }
    }

    public NotchScreenScope NotchScope
    {
        get => _notchScope;
        set
        {
            if (_notchScope == value) return;
            _notchScope = value;
            AppSettings.Set("notchScope", value.Raw());
            Raise();
        }
    }

    public DisplayPreference DisplayPreference
    {
        get => _displayPreference;
        set
        {
            if (_displayPreference == value) return;
            _displayPreference = value;
            AppSettings.Set("displayPreference", value.Raw());
            Raise();
        }
    }

    public ResetTimeFormat ResetTimeFormat
    {
        get => _resetTimeFormat;
        set
        {
            if (_resetTimeFormat == value) return;
            _resetTimeFormat = value;
            AppSettings.Set("resetTimeFormat", value.Raw());
            Raise();
        }
    }

    public AccentColorChoice AccentColor
    {
        get => _accentColor;
        set
        {
            if (_accentColor == value) return;
            _accentColor = value;
            AppSettings.Set("accentColor", value.Raw());
            Raise();
        }
    }

    public IReadOnlyList<string> ProviderOrder
    {
        get => _providerOrder;
        private set
        {
            _providerOrder = [.. value];
            AppSettings.SetList("providerOrder", _providerOrder);
            Raise();
        }
    }

    public bool AnnounceSessionEnd
    {
        get => _announceSessionEnd;
        set
        {
            if (_announceSessionEnd == value) return;
            _announceSessionEnd = value;
            AppSettings.SetBool("announceSessionEnd", value);
            Raise();
        }
    }

    public bool SessionEndSound
    {
        get => _sessionEndSound;
        set
        {
            if (_sessionEndSound == value) return;
            _sessionEndSound = value;
            AppSettings.SetBool("sessionEndSound", value);
            Raise();
        }
    }

    public PeekDuration PeekDuration
    {
        get => _peekDuration;
        set
        {
            if (_peekDuration == value) return;
            _peekDuration = value;
            AppSettings.Set("peekDuration", value.Raw());
            Raise();
        }
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (_launchAtLogin == value) return;
            _launchAtLogin = value;
            ApplyLaunchAtLogin();
            Raise();
        }
    }

    public string? LaunchAtLoginProblem
    {
        get => _launchAtLoginProblem;
        private set { _launchAtLoginProblem = value; Raise(); }
    }

    public bool IsConnected(string id) => !_disconnected.Contains(id);

    public void SetConnected(bool connected, string id)
    {
        if (connected)
        {
            _disconnected.Remove(id);
            _providerOrder = Model.ProviderOrder.JoiningConnected(id, _providerOrder, IsConnected);
            AppSettings.SetList("providerOrder", _providerOrder);
            Raise(nameof(ProviderOrder));
        }
        else
        {
            _disconnected.Add(id);
        }
        AppSettings.SetList("hiddenProviders", _disconnected);
        Raise(nameof(DisconnectedProviders));
    }

    public bool IsMutedAlerts(string id) => _mutedAlerts.Contains(id);

    public void SetAlertsMuted(bool muted, string id)
    {
        if (muted) _mutedAlerts.Add(id);
        else _mutedAlerts.Remove(id);
        AppSettings.SetList("mutedAlertProviders", _mutedAlerts);
        Raise(nameof(MutedAlertProviders));
    }

    public void SetProviderOrder(IReadOnlyList<string> ids)
    {
        ProviderOrder = Model.ProviderOrder.Remember(ids, _providerOrder);
    }

    public double Offset(NotchEdge edge) =>
        AppSettings.GetDouble($"notchOffset.{edge.RawValue()}");

    public void SetOffset(double offset, NotchEdge edge) =>
        AppSettings.SetDouble($"notchOffset.{edge.RawValue()}", offset);

    public static void EraseAllData() => AppSettings.EraseAll();

    private void ApplyLaunchAtLogin()
    {
        try
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.CreateSubKey(run);
            var exe = Environment.ProcessPath ?? "";
            if (_launchAtLogin) key.SetValue(AppBranding.ExeName, $"\"{exe}\" --quiet");
            else key.DeleteValue(AppBranding.ExeName, false);
            LaunchAtLoginProblem = null;
        }
        catch (Exception ex)
        {
            LaunchAtLoginProblem = $"Windows refused this — {ex.Message}";
            _launchAtLogin = IsRegisteredForLogin();
        }
    }

    private static bool IsRegisteredForLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(AppBranding.ExeName) is not null;
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
