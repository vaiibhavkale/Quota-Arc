using QuotaArc;
using QuotaArc.Notch;

namespace QuotaArc.Settings;

internal enum NotchVisibility
{
    AlwaysShow,
    OnHover,
    Hidden
}

internal static class NotchVisibilityInfo
{
    public static string Raw(this NotchVisibility v) => v switch
    {
        NotchVisibility.AlwaysShow => "alwaysShow",
        NotchVisibility.OnHover => "onHover",
        _ => "hidden"
    };

    public static NotchVisibility Parse(string? raw) => raw switch
    {
        "alwaysShow" => NotchVisibility.AlwaysShow,
        "hidden" => NotchVisibility.Hidden,
        _ => NotchVisibility.OnHover
    };

    public static string Title(this NotchVisibility v) => v switch
    {
        NotchVisibility.AlwaysShow => "Always show",
        NotchVisibility.OnHover => "Show on hover",
        _ => "Hide"
    };

    public static string Explanation(this NotchVisibility v) => v switch
    {
        NotchVisibility.AlwaysShow => "The notch stays open with every reading visible.",
        NotchVisibility.OnHover => "A small pill at the screen edge that opens when you reach it.",
        _ => $"Nothing on screen. Open {AppBranding.Name} again from the Start menu or the tray icon to bring these settings back."
    };
}

internal enum AppPresence
{
    Taskbar,
    Tray,
    Hidden
}

internal static class AppPresenceInfo
{
    public static string Raw(this AppPresence v) => v.ToString().ToLowerInvariant();

    public static AppPresence Parse(string? raw) => raw switch
    {
        "taskbar" or "dock" => AppPresence.Taskbar,
        "hidden" => AppPresence.Hidden,
        _ => AppPresence.Tray
    };

    public static string Title(this AppPresence v) => v switch
    {
        AppPresence.Taskbar => "Taskbar",
        AppPresence.Tray => "Tray",
        _ => "Neither"
    };

    public static string Explanation(this AppPresence v) => v switch
    {
        AppPresence.Taskbar => $"A normal app button on the taskbar while {AppBranding.Name} is running. The tray icon stays too.",
        AppPresence.Tray => "A small icon in the notification area. Closing the window keeps Quota Arc running there.",
        _ => $"No icon anywhere. Open {AppBranding.Name} again from the Start menu to bring the window back."
    };

    public static bool WantsTray(this AppPresence v) => v != AppPresence.Hidden;
}
