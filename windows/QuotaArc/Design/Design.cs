namespace QuotaArc.Design;

/// <summary>
/// Every number in the UI is measured off the Figma frame in
/// docs/design/frame-124-hover-tooltip.png. The frame fixes ratios; one
/// anchor picks the scale: the provider ring is 44pt and 117px in the frame.
/// Named Scale so files under QuotaArc.* do not collide with this namespace.
/// </summary>
internal static class Scale
{
    public const double Factor = 44.0 / 117.0;
    private const double CapRatio = 0.714;

    public static double Px(double pixels) => pixels * Factor;

    public static double FontSize(double capPixels) => Px(capPixels) / CapRatio;
}
