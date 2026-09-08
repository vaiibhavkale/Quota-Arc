namespace QuotaArc.Settings;

internal enum NotchScreenScope
{
    MainDisplay,
    AllDisplays
}

internal static class NotchScreenScopeInfo
{
    public static NotchScreenScope[] All { get; } = Enum.GetValues<NotchScreenScope>();

    public static string Raw(this NotchScreenScope value) => value switch
    {
        NotchScreenScope.AllDisplays => "allDisplays",
        _ => "mainDisplay"
    };

    public static NotchScreenScope Parse(string? raw) => raw switch
    {
        "allDisplays" => NotchScreenScope.AllDisplays,
        _ => NotchScreenScope.MainDisplay
    };

    public static string Title(this NotchScreenScope value) => value switch
    {
        NotchScreenScope.AllDisplays => "All displays",
        _ => "Main display"
    };

    public static string Explanation(this NotchScreenScope value) => value switch
    {
        NotchScreenScope.AllDisplays =>
            "Each display gets its own notch, and hovering one opens only that one.",
        _ => "The notch appears only on one display."
    };
}

internal enum DisplayPreferenceKind
{
    FollowActiveWindow,
    Pinned
}

internal sealed record DisplayPreference(DisplayPreferenceKind Kind, string? DeviceName)
{
    public static DisplayPreference FollowActiveWindow { get; } = new(DisplayPreferenceKind.FollowActiveWindow, null);
    public static DisplayPreference Pinned(string deviceName) => new(DisplayPreferenceKind.Pinned, deviceName);

    public static DisplayPreference Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "followActiveWindow")
            return FollowActiveWindow;
        return Pinned(raw);
    }

    public string Raw() => Kind == DisplayPreferenceKind.FollowActiveWindow
        ? "followActiveWindow"
        : DeviceName ?? "followActiveWindow";

    public string Explanation(IReadOnlyList<DisplayOption> displays)
    {
        if (Kind != DisplayPreferenceKind.Pinned)
            return "Moves to the display containing the window in the foreground.";
        var name = displays.FirstOrDefault(d => d.Id == DeviceName)?.Name;
        if (!string.IsNullOrEmpty(name))
            return $"Pinned to {name}.";
        return "That display is disconnected. The notch follows the active window until it returns.";
    }
}

internal sealed record DisplayOption(string Id, string Name);
