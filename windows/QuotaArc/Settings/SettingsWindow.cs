using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuotaArc;
using QuotaArc.Model;
using QuotaArc.Notch;
using QuotaArc.Overlay;
using QuotaArc.Providers;
using QuotaArc.Settings;

namespace QuotaArc.SettingsUi;

internal sealed class SettingsWindow : Window
{
    private readonly Preferences _preferences;
    private readonly Func<IReadOnlyList<ProviderSummary>> _providers;
    private readonly Action<string> _signOut;
    private readonly Func<string, bool> _signIn;
    private readonly Action<string> _retry;
    private StackPanel _accounts = null!;
    private TextBlock _refreshStatus = null!;
    private FrameworkElement _pinPickerHost = null!;
    private UIElement? _peekPicker;
    private readonly List<System.Windows.Shapes.Ellipse> _accentSwatches = [];
    private bool _forceClose;

    public SettingsWindow(
        Preferences preferences,
        Func<IReadOnlyList<ProviderSummary>> providers,
        Action<string> signOut,
        Func<string, bool> signIn,
        Action<string> retry)
    {
        _preferences = preferences;
        _providers = providers;
        _signOut = signOut;
        _signIn = signIn;
        _retry = retry;

        Title = "Settings";
        TrySetIcon();
        Width = 560;
        Height = 720;
        MinWidth = 480;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SettingsTheme.WindowBackground;
        Foreground = SettingsTheme.TextPrimary;
        ShowInTaskbar = true;
        DarkWindow.Apply(this);

        var stack = new StackPanel { Margin = new Thickness(24, 16, 24, 28) };
        stack.Children.Add(BuildIntegrationsSection());
        stack.Children.Add(BuildAppearanceSection());
        stack.Children.Add(BuildSessionSection());
        stack.Children.Add(BuildAlertsSection());
        stack.Children.Add(BuildGeneralSection());
        stack.Children.Add(CreditFooter());

        Content = SettingsTheme.DarkScrollViewer(stack);
        Closing += OnClosing;
        Activated += (_, _) => ReloadAccounts();
        ReloadAccounts();
    }

    private UIElement BuildIntegrationsSection()
    {
        var section = new StackPanel();
        section.Children.Add(SettingsTheme.SectionTitle("INTEGRATIONS"));

        var cardInner = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(12, 10, 12, 6) };
        var refreshAll = SettingsTheme.ActionLinks(("Refresh all tokens", RefreshAllTokens));
        refreshAll.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(refreshAll, Dock.Right);
        header.Children.Add(refreshAll);
        header.Children.Add(new TextBlock
        {
            Text = "Connected",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = SettingsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        cardInner.Children.Add(header);

        _accounts = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        cardInner.Children.Add(_accounts);

        _refreshStatus = SettingsTheme.Caption("");
        _refreshStatus.Margin = new Thickness(12, 4, 12, 0);
        _refreshStatus.Visibility = Visibility.Collapsed;
        cardInner.Children.Add(_refreshStatus);

        var footnote = SettingsTheme.Caption(
            $"{AppBranding.Name} reads usage from tools already signed in on this PC. "
            + "Move up / Move down reorders the rings the notch draws. "
            + "Use Refresh token after switching accounts.");
        footnote.Margin = new Thickness(12, 10, 12, 10);
        cardInner.Children.Add(footnote);

        section.Children.Add(SettingsTheme.SectionCard(cardInner));
        return section;
    }

    private UIElement BuildAppearanceSection()
    {
        var card = new StackPanel();
        card.Children.Add(MakeAppearancePicker(
            "Show",
            Enum.GetValues<NotchVisibility>().Select(v => v.Title()).ToArray(),
            Array.IndexOf(Enum.GetValues<NotchVisibility>(), _preferences.NotchVisibility),
            i => _preferences.NotchVisibility = Enum.GetValues<NotchVisibility>()[i],
            () => _preferences.NotchVisibility.Explanation()));

        card.Children.Add(MakeAppearancePicker(
            "Edge",
            NotchEdgeInfo.All.Select(e => e.Title()).ToArray(),
            Array.IndexOf(NotchEdgeInfo.All.ToArray(), _preferences.NotchEdge),
            i => _preferences.NotchEdge = NotchEdgeInfo.All[i],
            () => _preferences.NotchEdge.Explanation()));

        card.Children.Add(MakeAppearancePicker(
            "App icon",
            Enum.GetValues<AppPresence>().Select(v => v.Title()).ToArray(),
            Array.IndexOf(Enum.GetValues<AppPresence>(), _preferences.AppPresence),
            i => _preferences.AppPresence = Enum.GetValues<AppPresence>()[i],
            () => _preferences.AppPresence.Explanation()));

        card.Children.Add(MakeAppearancePicker(
            "Reset time",
            ResetTimeFormatInfo.All.Select(v => v.Title()).ToArray(),
            Array.IndexOf(ResetTimeFormatInfo.All, _preferences.ResetTimeFormat),
            i => _preferences.ResetTimeFormat = ResetTimeFormatInfo.All[i],
            () => _preferences.ResetTimeFormat.Explanation()));

        card.Children.Add(MakeAppearancePicker(
            "Displays",
            NotchScreenScopeInfo.All.Select(v => v.Title()).ToArray(),
            Array.IndexOf(NotchScreenScopeInfo.All, _preferences.NotchScope),
            i =>
            {
                _preferences.NotchScope = NotchScreenScopeInfo.All[i];
                UpdatePinPickerVisibility();
            },
            () => _preferences.NotchScope.Explanation()));

        _pinPickerHost = DisplayPinPicker();
        card.Children.Add(_pinPickerHost);
        UpdatePinPickerVisibility();
        card.Children.Add(AccentPicker());
        var nudge = SettingsTheme.Caption("Alt-drag the pill along its edge to nudge it. Remembered per edge.");
        nudge.Margin = new Thickness(12, 4, 12, 10);
        card.Children.Add(nudge);

        var section = new StackPanel();
        section.Children.Add(SettingsTheme.SectionTitle("APPEARANCE"));
        section.Children.Add(SettingsTheme.SectionCard(card));
        return section;
    }

    private UIElement BuildGeneralSection()
    {
        var card = new StackPanel();
        card.Children.Add(SettingsTheme.Toggle(
            $"Open {AppBranding.Name} at login",
            _preferences.LaunchAtLogin,
            v => _preferences.LaunchAtLogin = v));

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionLine = SettingsTheme.Caption(
            $"Version {version?.ToString(3) ?? "1.6.0"}. This Windows build does not auto-update.");
        versionLine.Margin = new Thickness(12, 0, 12, 8);
        card.Children.Add(versionLine);

        card.Children.Add(SettingsTheme.Divider());

        var erase = SettingsTheme.SecondaryButton("Erase all data and quit", () =>
        {
            if (MessageBox.Show(this,
                    "This forgets every reading and setting, then quits.",
                    "Erase all data?",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            Preferences.EraseAllData();
            _forceClose = true;
            Application.Current.Shutdown();
        });
        erase.Foreground = SettingsTheme.Destructive;
        erase.BorderBrush = SettingsTheme.Destructive;
        erase.Margin = new Thickness(12, 0, 12, 8);
        erase.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(erase);

        var section = new StackPanel();
        section.Children.Add(SettingsTheme.SectionTitle("GENERAL"));
        section.Children.Add(SettingsTheme.SectionCard(card));
        return section;
    }

    private static UIElement CreditFooter()
    {
        var line = new TextBlock
        {
            FontSize = 11,
            Foreground = SettingsTheme.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        line.Inlines.Add("App designed and developed by ");
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("@hivinz_"))
        {
            NavigateUri = new Uri("https://x.com/hivinz_"),
            Foreground = SettingsTheme.TextSecondary
        };
        link.RequestNavigate += (_, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* ignore */ }
        };
        line.Inlines.Add(link);
        return line;
    }

    private UIElement MakeAppearancePicker(
        string label,
        string[] items,
        int selected,
        Action<int> onChange,
        Func<string> explanation)
    {
        var caption = SettingsTheme.Caption(explanation());
        caption.Margin = new Thickness(12, 0, 12, 8);
        var picker = SettingsTheme.SegmentedPicker(label, items, selected, i =>
        {
            onChange(i);
            caption.Text = explanation();
        });
        var panel = new StackPanel();
        panel.Children.Add(picker);
        panel.Children.Add(caption);
        return panel;
    }

    private UIElement BuildSessionSection()
    {
        var card = new StackPanel();
        card.Children.Add(SettingsTheme.Toggle(
            "Open the notch for a moment",
            _preferences.AnnounceSessionEnd,
            v =>
            {
                _preferences.AnnounceSessionEnd = v;
                if (_peekPicker is not null) _peekPicker.IsEnabled = v;
            }));
        _peekPicker = MakeAppearancePicker(
            "Keep it open for",
            PeekDurationInfo.All.Select(v => v.Title()).ToArray(),
            Array.IndexOf(PeekDurationInfo.All, _preferences.PeekDuration),
            i => _preferences.PeekDuration = PeekDurationInfo.All[i],
            () => _preferences.PeekDuration.Explanation());
        _peekPicker.IsEnabled = _preferences.AnnounceSessionEnd;
        card.Children.Add(_peekPicker);
        card.Children.Add(SettingsTheme.Toggle(
            "Play a sound",
            _preferences.SessionEndSound,
            v => _preferences.SessionEndSound = v));
        var sessionNote = SettingsTheme.Caption(
            $"{AppBranding.Name} already knows the moment an agent stops working "
            + "or stops to ask you something. The notch opens, and a click jumps to it.");
        sessionNote.Margin = new Thickness(12, 4, 12, 10);
        card.Children.Add(sessionNote);

        var section = new StackPanel();
        section.Children.Add(SettingsTheme.SectionTitle("WHEN A SESSION ENDS"));
        section.Children.Add(SettingsTheme.SectionCard(card));
        return section;
    }

    private UIElement BuildAlertsSection()
    {
        var card = new StackPanel();
        var note = SettingsTheme.Caption(
            "A notification when a ring's headline limit crosses 80%, and again at 100% "
            + "- once per crossing, and again only after the window rolls over. "
            + "Mute one from its row in Integrations.");
        note.Margin = new Thickness(12, 8, 12, 10);
        card.Children.Add(note);
        var section = new StackPanel();
        section.Children.Add(SettingsTheme.SectionTitle("THRESHOLD ALERTS"));
        section.Children.Add(SettingsTheme.SectionCard(card));
        return section;
    }

    private void UpdatePinPickerVisibility()
    {
        _pinPickerHost.Visibility = _preferences.NotchScope == NotchScreenScope.MainDisplay
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private FrameworkElement DisplayPinPicker()
    {
        var displays = ScreenQuery.DisplayOptions().ToList();
        var options = new List<(string Label, DisplayPreference Value)>
        {
            ("Follow active window", DisplayPreference.FollowActiveWindow)
        };
        options.AddRange(displays.Select(d => (d.Name, DisplayPreference.Pinned(d.Id))));

        var selected = 0;
        if (_preferences.DisplayPreference.Kind == DisplayPreferenceKind.Pinned)
        {
            var idx = displays.FindIndex(d => d.Id == _preferences.DisplayPreference.DeviceName);
            if (idx >= 0) selected = idx + 1;
            else
            {
                options.Add(("Unavailable display", _preferences.DisplayPreference));
                selected = options.Count - 1;
            }
        }

        var caption = SettingsTheme.Caption(_preferences.DisplayPreference.Explanation(displays));
        caption.Margin = new Thickness(12, 0, 12, 8);

        var combo = new ComboBox
        {
            ItemsSource = options.Select(o => o.Label).ToList(),
            SelectedIndex = selected,
            Margin = new Thickness(12, 0, 12, 4),
            MinHeight = 28,
            FontSize = 13,
            Background = SettingsTheme.RowBackground,
            Foreground = SettingsTheme.TextPrimary,
            BorderBrush = SettingsTheme.SectionBorder
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= options.Count) return;
            _preferences.DisplayPreference = options[combo.SelectedIndex].Value;
            caption.Text = _preferences.DisplayPreference.Explanation(displays);
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Display",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = SettingsTheme.TextPrimary,
            Margin = new Thickness(12, 8, 12, 8)
        });
        panel.Children.Add(combo);
        panel.Children.Add(caption);
        return panel;
    }

    private UIElement AccentPicker()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = "Accent colour",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = SettingsTheme.TextPrimary,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var row = new WrapPanel();
        foreach (var choice in AccentColorInfo.All)
        {
            var current = choice;
            var swatch = new System.Windows.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = current.Brush(),
                Stroke = current == _preferences.AccentColor ? SettingsTheme.TextPrimary : SettingsTheme.SectionBorder,
                StrokeThickness = current == _preferences.AccentColor ? 2 : 1,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = current == AccentColorChoice.System ? "Device accent colour" : current.Raw(),
                Tag = current
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _preferences.AccentColor = current;
                foreach (var item in _accentSwatches)
                {
                    var selected = Equals(item.Tag, _preferences.AccentColor);
                    item.Stroke = selected ? SettingsTheme.TextPrimary : SettingsTheme.SectionBorder;
                    item.StrokeThickness = selected ? 2 : 1;
                }
            };
            _accentSwatches.Add(swatch);
            row.Children.Add(swatch);
        }
        panel.Children.Add(row);
        var caption = SettingsTheme.Caption("Used for the ring's positive state. Amber and red warnings stay fixed.");
        panel.Children.Add(caption);
        return panel;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        Hide();
    }

    private void TrySetIcon()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, AppBranding.IconFileName);
            if (!File.Exists(path)) return;
            var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            Icon = frame;
        }
        catch { /* ignore */ }
    }

    public new void Show()
    {
        ReloadAccounts();
        if (Visibility != Visibility.Visible)
            base.Show();
        Activate();
        Focus();
    }

    private void ReloadAccounts()
    {
        _accounts.Children.Clear();
        var list = _providers();
        var needsSetup = list.Count > 0 && list.All(p => p.Account is null);

        if (needsSetup)
            _accounts.Children.Add(SetupNote());

        var connected = list.Where(p => _preferences.IsConnected(p.Id)).ToList();
        var disconnected = list.Where(p => !_preferences.IsConnected(p.Id)).ToList();

        for (var i = 0; i < connected.Count; i++)
            _accounts.Children.Add(AccountRow(connected[i], i, connected.Count, true));

        if (disconnected.Count > 0)
        {
            _accounts.Children.Add(new TextBlock
            {
                Text = "Not connected",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = SettingsTheme.TextMuted,
                Margin = new Thickness(12, 10, 12, 6)
            });
            foreach (var provider in disconnected)
                _accounts.Children.Add(AccountRow(provider, 0, 0, false));
        }
    }

    private UIElement SetupNote()
    {
        var border = new Border
        {
            Background = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x95, 0x00))),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(8, 4, 8, 8)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Connect an assistant to get started",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = SettingsTheme.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(SettingsTheme.Caption(
            "Sign in to Claude Desktop, Cursor, Codex, or Antigravity and its ring appears in the notch."));
        border.Child = panel;
        return border;
    }

    private UIElement AccountRow(ProviderSummary provider, int index, int connectedCount, bool connected)
    {
        var row = new Border
        {
            Background = SettingsTheme.RowBackground,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(8, 0, 8, 6)
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var glyphHost = new Viewbox
        {
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        glyphHost.Child = new System.Windows.Shapes.Path
        {
            Data = provider.Glyph.Geometry(new Rect(0, 0, 16, 16)),
            Fill = connected ? SettingsTheme.TextPrimary : SettingsTheme.TextMuted,
            Stretch = Stretch.Uniform
        };
        Grid.SetRow(glyphHost, 0);
        Grid.SetColumn(glyphHost, 0);
        root.Children.Add(glyphHost);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = provider.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = connected ? SettingsTheme.TextPrimary : SettingsTheme.TextSecondary
        });

        var detail = DetailLine(provider, connected);
        if (detail is not null)
            info.Children.Add(detail);

        if (connected)
        {
            var actions = BuildRowActions(provider, index, connectedCount);
            if (actions.Inlines.Count > 0)
                info.Children.Add(actions);
        }

        Grid.SetRow(info, 0);
        Grid.SetColumn(info, 1);
        root.Children.Add(info);

        var toggle = SettingsTheme.MakeSwitch(connected, on =>
        {
            if (on)
            {
                _preferences.SetConnected(true, provider.Id);
                _signIn(provider.Id);
                RefreshToken(provider.Id, provider.Name, quiet: true);
            }
            else
            {
                _preferences.SetConnected(false, provider.Id);
                _signOut(provider.Id);
            }
            ReloadAccounts();
        });
        Grid.SetRow(toggle, 0);
        Grid.SetColumn(toggle, 2);
        root.Children.Add(toggle);

        row.Child = root;
        return row;
    }

    private TextBlock BuildRowActions(ProviderSummary provider, int index, int connectedCount)
    {
        var actions = new List<(string, Action)>();

        if (index > 0)
            actions.Add(("Move up", () => MoveConnected(index, -1)));
        if (index < connectedCount - 1)
            actions.Add(("Move down", () => MoveConnected(index, 1)));

        if (provider.WasRefusedAccess)
            actions.Add(("Allow access", () => RefreshToken(provider.Id, provider.Name)));
        else
            actions.Add(("Refresh token", () => RefreshToken(provider.Id, provider.Name)));

        if (provider.SignIn is SignInRoute.OpenApp app)
            actions.Add(($"Open {app.Name}", () => _signIn(provider.Id)));

        actions.Add((_preferences.IsMutedAlerts(provider.Id) ? "Unmute alerts" : "Mute alerts",
            () =>
            {
                _preferences.SetAlertsMuted(!_preferences.IsMutedAlerts(provider.Id), provider.Id);
                ReloadAccounts();
            }));

        return SettingsTheme.ActionLinks(actions.ToArray());
    }

    private void MoveConnected(int index, int delta)
    {
        var connected = _providers().Where(p => _preferences.IsConnected(p.Id)).Select(p => p.Id).ToList();
        var next = index + delta;
        if (next < 0 || next >= connected.Count) return;
        (connected[index], connected[next]) = (connected[next], connected[index]);
        var rest = _providers().Where(p => !_preferences.IsConnected(p.Id)).Select(p => p.Id);
        _preferences.SetProviderOrder(connected.Concat(rest).ToList());
        ReloadAccounts();
    }

    private TextBlock? DetailLine(ProviderSummary provider, bool connected)
    {
        if (!connected)
            return SettingsTheme.Caption("Signed out", SettingsTheme.TextMuted);

        if (provider.WasRefusedAccess)
        {
            return SettingsTheme.Caption(
                "Windows blocked access to saved login",
                SettingsTheme.Warning);
        }

        if (provider.Account is { } account)
        {
            var block = SettingsTheme.Caption(account.Summary, SettingsTheme.TextSecondary);
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.ToolTip = account.Summary;
            return block;
        }

        return SettingsTheme.Caption(provider.SignIn.Explanation, SettingsTheme.Warning);
    }

    private void RefreshToken(string providerId, string name, bool quiet = false)
    {
        _retry(providerId);
        if (!quiet)
            ShowRefreshStatus($"Refreshing {name}...");
        Dispatcher.BeginInvoke(() =>
        {
            ReloadAccounts();
            if (!quiet)
                ShowRefreshStatus($"Refreshed {name}.");
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshAllTokens()
    {
        var connected = _providers().Where(p => _preferences.IsConnected(p.Id)).ToList();
        if (connected.Count == 0)
        {
            ShowRefreshStatus("No connected integrations to refresh.");
            return;
        }

        foreach (var p in connected)
            _retry(p.Id);

        ShowRefreshStatus($"Refreshing {connected.Count} integration(s)...");
        Dispatcher.BeginInvoke(() =>
        {
            ReloadAccounts();
            ShowRefreshStatus("All connected tokens refreshed.");
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ShowRefreshStatus(string message)
    {
        _refreshStatus.Text = message;
        _refreshStatus.Foreground = SettingsTheme.Accent;
        _refreshStatus.Visibility = Visibility.Visible;
    }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
