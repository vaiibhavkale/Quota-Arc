using System.Globalization;
using QuotaArc.Settings;

namespace QuotaArc.Model;

internal enum ResetTimeFormat
{
    Automatic,
    Remaining
}

internal static class ResetTimeFormatInfo
{
    public static ResetTimeFormat[] All { get; } = Enum.GetValues<ResetTimeFormat>();

    public static string Raw(this ResetTimeFormat value) => value.ToString().ToLowerInvariant();

    public static ResetTimeFormat Parse(string? raw) => raw == "remaining"
        ? ResetTimeFormat.Remaining
        : ResetTimeFormat.Automatic;

    public static string Title(this ResetTimeFormat value) => value switch
    {
        ResetTimeFormat.Remaining => "Time remaining",
        _ => "Reset date"
    };

    public static string Explanation(this ResetTimeFormat value) => value switch
    {
        ResetTimeFormat.Remaining => "Time until usage resets, such as 3 Days 3h or 3h 20m.",
        _ => "Minutes under an hour; otherwise the reset date and time."
    };
}

internal static class ResetCopy
{
    public static string Text(
        DateTime resetsAt,
        DateTime? now = null,
        TimeZoneInfo? zone = null,
        ResetTimeFormat format = ResetTimeFormat.Automatic)
    {
        var n = now ?? DateTime.Now;
        zone ??= TimeZoneInfo.Local;
        var seconds = (resetsAt - n).TotalSeconds;
        if (seconds <= 0) return "Resetting…";

        if (format == ResetTimeFormat.Remaining)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Round(seconds / 60));
            var hours = remainingMinutes / 60;
            var days = hours / 24;
            if (days > 0)
                return $"Resets in {days} {(days == 1 ? "Day" : "Days")} {hours % 24}h";
            if (hours > 0)
                return $"Resets in {hours}h {remainingMinutes % 60}m";
            return $"Resets in {remainingMinutes} min";
        }

        var minutes = (int)Math.Round(seconds / 60);
        if (minutes < 60)
            return $"Resets in {Math.Max(1, minutes)} min";

        var formatter = Formatter(zone);
        if (DaysApart(n, resetsAt, zone) >= 7)
        {
            formatter.DateFormat = "MMM d";
            return $"Resets {formatter.Format(resetsAt)}";
        }

        formatter.DateFormat = "ddd h:mm tt";
        return $"Resets {formatter.Format(resetsAt)}";
    }

    public static ZoneFormatter Formatter(TimeZoneInfo zone) => new(zone);

    public static int DaysApart(DateTime from, DateTime to, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var start = TimeZoneInfo.ConvertTime(from, zone).Date;
        var end = TimeZoneInfo.ConvertTime(to, zone).Date;
        return (end - start).Days;
    }

    internal sealed class ZoneFormatter
    {
        private readonly TimeZoneInfo _zone;
        public string DateFormat { get; set; } = "g";

        public ZoneFormatter(TimeZoneInfo zone) => _zone = zone;

        public string Format(DateTime value)
        {
            var local = TimeZoneInfo.ConvertTime(value, _zone);
            return local.ToString(DateFormat, CultureInfo.CurrentCulture);
        }
    }
}
