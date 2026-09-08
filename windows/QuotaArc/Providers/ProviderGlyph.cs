using System.Windows;
using System.Windows.Media;

namespace QuotaArc.Providers;

internal enum ProviderGlyph
{
    Claude,
    Openai,
    Third,
    Cursor,
    Antigravity,
    Glm,
    Grok,
    Opencode
}

internal static class ProviderGlyphInfo
{
    public static double OpticalScale(this ProviderGlyph glyph) => glyph switch
    {
        ProviderGlyph.Claude => 0.97,
        ProviderGlyph.Cursor => 0.97,
        ProviderGlyph.Openai => 0.94,
        ProviderGlyph.Antigravity => 1.0,
        ProviderGlyph.Glm => 0.95,
        ProviderGlyph.Grok => 1.0,
        ProviderGlyph.Opencode => 0.95,
        _ => 1.0
    };

    public static Point[][] Outline(this ProviderGlyph glyph) => glyph switch
    {
        ProviderGlyph.Claude => GlyphOutline.Claude,
        ProviderGlyph.Openai => GlyphOutline.Openai,
        ProviderGlyph.Third => GlyphOutline.Third,
        ProviderGlyph.Cursor => GlyphOutline.Cursor,
        ProviderGlyph.Antigravity => GlyphOutline.Antigravity,
        ProviderGlyph.Glm => GlyphOutline.Glm,
        ProviderGlyph.Grok => GlyphOutline.Grok,
        ProviderGlyph.Opencode => GlyphOutline.Opencode,
        _ => GlyphOutline.Third
    };

    public static Geometry Geometry(this ProviderGlyph glyph, Rect bounds)
    {
        var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = geo.Open())
        {
            foreach (var loop in glyph.Outline())
            {
                if (loop.Length == 0) continue;
                ctx.BeginFigure(Map(loop[0], bounds), true, true);
                for (var i = 1; i < loop.Length; i++)
                    ctx.LineTo(Map(loop[i], bounds), true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    private static Point Map(Point p, Rect rect) =>
        new(rect.X + p.X * rect.Width, rect.Y + p.Y * rect.Height);
}
