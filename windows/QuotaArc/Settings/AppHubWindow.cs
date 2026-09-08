using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuotaArc;

namespace QuotaArc.SettingsUi;

internal sealed class AppHubWindow : Window
{
    private readonly Action _openSettings;
    private readonly Action? _showNotch;
    private readonly Action? _onHidden;
    private bool _forceClose;

    public AppHubWindow(Action openSettings, Action? showNotch = null, Action? onHidden = null)
    {
        _openSettings = openSettings;
        _showNotch = showNotch;
        _onHidden = onHidden;

        Title = AppBranding.Name;
        Width = 460;
        Height = 640;
        MinWidth = 420;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SettingsTheme.WindowBackground;
        Foreground = SettingsTheme.TextPrimary;
        ShowInTaskbar = true;
        TrySetIcon();
        DarkWindow.Apply(this);

        Content = BuildContent();
        Closing += OnClosing;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                _onHidden?.Invoke();
        };
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(32, 24, 32, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = Header();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var hero = BuildHero();
        Grid.SetRow(hero, 1);
        root.Children.Add(hero);

        var footer = Footer();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private UIElement Header()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(LogoMark(96));
        stack.Children.Add(new TextBlock
        {
            Text = $"{AppBranding.Name} is running",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = SettingsTheme.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        });
        return stack;
    }

    private UIElement BuildHero()
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        panel.Children.Add(Body(
            "Your coding-assistant usage lives on the screen edge as a small notch.",
            SettingsTheme.TextSecondary, 14, 22, 10));

        panel.Children.Add(Body(
            "How to open Quota Arc after this window is closed:",
            SettingsTheme.TextPrimary, 13, 20, 8));

        panel.Children.Add(Body(
            "1. Click the Quota Arc icon in the system tray (bottom-right, near the clock). If you do not see it, click the ^ arrow to show hidden icons.\n"
            + "2. Open it from the Start menu or the desktop shortcut.\n"
            + "3. Move the mouse to the screen edge to unfold the notch.",
            SettingsTheme.TextMuted, 13, 20, 22));

        var settings = SettingsTheme.PrimaryButton("Open Settings", () => _openSettings());
        settings.Margin = new Thickness(0, 0, 0, 10);
        settings.HorizontalAlignment = HorizontalAlignment.Stretch;
        settings.MinHeight = 44;
        settings.FontSize = 14;
        panel.Children.Add(settings);

        if (_showNotch is not null)
        {
            var notch = SettingsTheme.SecondaryButton("Show usage notch", _showNotch);
            notch.HorizontalAlignment = HorizontalAlignment.Stretch;
            notch.MinHeight = 40;
            notch.FontSize = 13;
            panel.Children.Add(notch);
        }

        var background = SettingsTheme.SecondaryButton("Run in background", Hide);
        background.Margin = new Thickness(0, 10, 0, 0);
        background.HorizontalAlignment = HorizontalAlignment.Stretch;
        background.MinHeight = 40;
        background.FontSize = 13;
        panel.Children.Add(background);

        return panel;
    }

    private static TextBlock Body(string text, Brush color, double size, double lineHeight, double bottom)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = color,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, bottom),
            LineHeight = lineHeight
        };
    }

    private UIElement Footer()
    {
        return SettingsTheme.Caption(
            "Closing this window does not quit Quota Arc. It stays in the system tray "
            + "until you choose Quit from that icon.",
            SettingsTheme.TextMuted);
    }

    private static UIElement LogoMark(double size)
    {
        var image = TryLoadLogo(size);
        if (image is not null)
        {
            var frame = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 5),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = image
            };
            return frame;
        }

        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Stroke = SettingsTheme.Accent,
            StrokeThickness = 3,
            Fill = Brushes.Transparent
        };
        var notch = new System.Windows.Shapes.Rectangle
        {
            Width = size * 0.35,
            Height = size * 0.55,
            Fill = SettingsTheme.Accent,
            RadiusX = 4,
            RadiusY = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, size * 0.08, size * 0.06, 0)
        };
        var host = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        host.Children.Add(ring);
        host.Children.Add(notch);
        return host;
    }

    private static Image? TryLoadLogo(double size)
    {
        foreach (var name in new[] { AppBranding.LogoFileName, AppBranding.IconFileName })
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, name);
                if (!File.Exists(path)) continue;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = (int)(size * 2);
                bitmap.EndInit();
                bitmap.Freeze();
                return new Image
                {
                    Source = bitmap,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.UniformToFill
                };
            }
            catch { /* try next */ }
        }
        return null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        Hide();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public new void Show()
    {
        if (Visibility != Visibility.Visible)
            base.Show();
        Activate();
        Focus();
    }

    private void TrySetIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, AppBranding.IconFileName);
            if (!File.Exists(path)) return;
            var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            Icon = frame;
        }
        catch { /* ignore */ }
    }
}
