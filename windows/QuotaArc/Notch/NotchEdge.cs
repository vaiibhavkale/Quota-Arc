using System.Windows;

namespace QuotaArc.Notch;

internal enum NotchEdge
{
    Right,
    Left,
    Top,
    Bottom
}

internal enum TooltipDirection
{
    Leading,
    Trailing,
    Up,
    Down
}

internal static class NotchEdgeInfo
{
    public static IReadOnlyList<NotchEdge> All { get; } =
        [NotchEdge.Right, NotchEdge.Left, NotchEdge.Top, NotchEdge.Bottom];

    public static bool IsVertical(this NotchEdge edge) =>
        edge is NotchEdge.Right or NotchEdge.Left;

    public static TooltipDirection TooltipDirection(this NotchEdge edge) => edge switch
    {
        NotchEdge.Right => Notch.TooltipDirection.Leading,
        NotchEdge.Left => Notch.TooltipDirection.Trailing,
        NotchEdge.Top => Notch.TooltipDirection.Down,
        _ => Notch.TooltipDirection.Up
    };

    public static Vector Outward(this NotchEdge edge) => edge switch
    {
        NotchEdge.Right => new Vector(1, 0),
        NotchEdge.Left => new Vector(-1, 0),
        NotchEdge.Top => new Vector(0, -1),
        _ => new Vector(0, 1)
    };

    public static Vector AlongDirection(this NotchEdge edge) =>
        edge.IsVertical() ? new Vector(0, 1) : new Vector(1, 0);

    public static string Title(this NotchEdge edge) => edge switch
    {
        NotchEdge.Right => "Right",
        NotchEdge.Left => "Left",
        NotchEdge.Top => "Top",
        _ => "Bottom"
    };

    public static string Explanation(this NotchEdge edge) => edge switch
    {
        NotchEdge.Right => "Down the right-hand edge, clear of a taskbar on that side.",
        NotchEdge.Left => "Down the left-hand edge, clear of a taskbar on that side.",
        NotchEdge.Top => "A wide bar across the top, readings side by side.",
        _ => "A wide bar resting on top of the taskbar, readings side by side."
    };

    public static string RawValue(this NotchEdge edge) => edge.ToString().ToLowerInvariant();

    public static NotchEdge? Parse(string? raw) => raw?.ToLowerInvariant() switch
    {
        "right" => NotchEdge.Right,
        "left" => NotchEdge.Left,
        "top" => NotchEdge.Top,
        "bottom" => NotchEdge.Bottom,
        _ => null
    };
}
