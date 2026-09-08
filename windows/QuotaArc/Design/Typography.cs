using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace QuotaArc.Design;

internal static class Typography
{
    public static readonly Typeface Ui = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly Typeface UiSemibold = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static double PercentSize => Scale.FontSize(27);
    public static double CardTitleSize => Scale.FontSize(26);
    public static double CardBodySize => Scale.FontSize(18);
    public static double PixelsPerDip { get; set; } = 1;

    public static FormattedText Make(string text, Typeface typeface, double size, Brush brush, double pixelsPerDip = 0, double? wrapWidth = null)
    {
        var dip = pixelsPerDip > 0 ? pixelsPerDip : PixelsPerDip;
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            dip);
        if (wrapWidth is { } w)
            ft.MaxTextWidth = w;
        return ft;
    }
}
