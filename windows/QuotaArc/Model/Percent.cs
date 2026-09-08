namespace QuotaArc.Model;

internal static class Percent
{
    public static (string Used, string Left) Halves(double fraction)
    {
        var value = fraction * 100;
        var fractional = (value > 0 && value < 1) || (value > 99 && value < 100);
        if (!fractional)
        {
            var used = (int)Math.Round(value);
            return ($"{used}", $"{Math.Max(0, 100 - used)}");
        }

        var left = Math.Max(0, 100 - value);
        return (Small(value), left > 99.9 ? ">99.9" : Small(left));
    }

    public static string Text(double fraction)
    {
        var value = fraction * 100;
        if (value <= 0 || value >= 1) return $"{(int)Math.Round(value)}";
        return Small(value);
    }

    private static string Small(double value)
    {
        if (value <= 0) return "0";
        var tenths = Math.Round(value * 10) / 10;
        if (tenths < 0.1) return "<0.1";
        if (tenths > 99.9) return ">99.9";
        return tenths.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }
}
