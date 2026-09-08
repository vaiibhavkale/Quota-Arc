namespace QuotaArc.Model;

internal static class ElapsedCopy
{
    public static string Ago(DateTime since, DateTime? now = null)
    {
        var elapsed = Text(since, now);
        return elapsed == "just now" ? elapsed : $"{elapsed} ago";
    }

    public static string Text(DateTime since, DateTime? now = null)
    {
        var n = now ?? DateTime.Now;
        var seconds = Math.Max(0, (n - since).TotalSeconds);
        if (seconds < 45) return "just now";

        var minutes = (int)Math.Round(seconds / 60);
        if (minutes < 60) return $"{Math.Max(1, minutes)} min";

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
    }
}
