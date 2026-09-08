using System.Windows;
using QuotaArc.Notch;
using QuotaArc.Settings;

namespace QuotaArc.Overlay;

internal static class ScreenQuery
{
    public static IReadOnlyList<ScreenInfo> All() =>
        System.Windows.Forms.Screen.AllScreens.Select(From).ToList();

    public static IReadOnlyList<DisplayOption> DisplayOptions() =>
        All().Select(s => new DisplayOption(s.DeviceName, s.DisplayName)).ToList();

    public static ScreenInfo Preferred(DisplayPreference? preference = null)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (preference is { Kind: DisplayPreferenceKind.Pinned, DeviceName: { } name })
        {
            var pinned = screens.FirstOrDefault(s => s.DeviceName == name);
            if (pinned is not null) return From(pinned);
        }

        var screen = ScreenFromForeground()
            ?? ScreenFromCursor()
            ?? System.Windows.Forms.Screen.PrimaryScreen
            ?? screens.First();
        return From(screen);
    }

    public static ScreenInfo Primary()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen
            ?? System.Windows.Forms.Screen.AllScreens.First();
        return From(screen);
    }

    public static ScreenInfo From(System.Windows.Forms.Screen screen)
    {
        var dpi = Win32.DpiFor(screen);
        var scale = 96.0 / dpi;
        var name = string.IsNullOrWhiteSpace(screen.DeviceName) ? "Display" : screen.DeviceName;
        var label = screen.Primary ? "Main display" : FriendlyName(screen);
        return new ScreenInfo(
            ToDip(screen.Bounds, scale),
            ToDip(screen.WorkingArea, scale),
            name,
            label);
    }

    public static double DipScale(ScreenInfo? assigned = null)
    {
        System.Windows.Forms.Screen? screen = null;
        if (assigned is { DeviceName: { Length: > 0 } name })
            screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name);
        screen ??= ScreenFromCursor() ?? System.Windows.Forms.Screen.PrimaryScreen
            ?? System.Windows.Forms.Screen.AllScreens.First();
        return 96.0 / Win32.DpiFor(screen);
    }

    private static string FriendlyName(System.Windows.Forms.Screen screen)
    {
        var raw = screen.DeviceName.Replace(@"\\.\", "");
        return string.IsNullOrWhiteSpace(raw) ? "Display" : raw;
    }

    private static System.Windows.Forms.Screen? ScreenFromForeground()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        try { return System.Windows.Forms.Screen.FromHandle(hwnd); }
        catch { return null; }
    }

    private static System.Windows.Forms.Screen? ScreenFromCursor()
    {
        Win32.GetCursorPos(out var pt);
        return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
    }

    private static Rect ToDip(System.Drawing.Rectangle r, double scale) =>
        new(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);
}
