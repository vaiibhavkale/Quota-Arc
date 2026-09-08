using System.Windows.Media;
using QuotaArc.Design;
using QuotaArc.Settings;

namespace QuotaArc.Model;

internal enum UsageBand
{
    Ample,
    Watch,
    Critical,
    Exhausted
}

internal static class UsageBands
{
    public static UsageBand Band(double usedFraction) => usedFraction switch
    {
        < 0.50 => UsageBand.Ample,
        < 0.70 => UsageBand.Watch,
        < 1.00 => UsageBand.Critical,
        _ => UsageBand.Exhausted
    };

    public static Color Color(this UsageBand band, AccentColorChoice accent = AccentColorChoice.System) => band switch
    {
        UsageBand.Ample => accent.Color(),
        UsageBand.Watch => Palette.Watch,
        _ => Palette.Critical
    };

    public static Brush Brush(this UsageBand band, AccentColorChoice accent = AccentColorChoice.System)
    {
        if (band == UsageBand.Ample) return accent.Brush();
        return band == UsageBand.Watch ? Palette.WatchBrush : Palette.CriticalBrush;
    }
}
