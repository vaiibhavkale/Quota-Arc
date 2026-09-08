using System.Windows.Media;

namespace QuotaArc.Design;

internal static class Palette
{
    public static readonly Color Notch = Colors.Black;
    public static readonly Color Card = Colors.Black;
    public static readonly Color RingTrack = Hex(0x303030);
    public static readonly Color BarTrack = Hex(0x2D2D2D);
    public static readonly Color Ample = Hex(0x00FF88);
    public static readonly Color Watch = Hex(0xF2FF00);
    public static readonly Color Critical = Hex(0xFF3F00);
    public static readonly Color TextPrimary = Colors.White;
    public static readonly Color TextSecondary = Hex(0x808080);

    public static readonly Brush NotchBrush = new SolidColorBrush(Notch);
    public static readonly Brush CardBrush = new SolidColorBrush(Card);
    public static readonly Brush RingTrackBrush = new SolidColorBrush(RingTrack);
    public static readonly Brush BarTrackBrush = new SolidColorBrush(BarTrack);
    public static readonly Brush AmpleBrush = new SolidColorBrush(Ample);
    public static readonly Brush WatchBrush = new SolidColorBrush(Watch);
    public static readonly Brush CriticalBrush = new SolidColorBrush(Critical);
    public static readonly Brush TextPrimaryBrush = new SolidColorBrush(TextPrimary);
    public static readonly Brush TextSecondaryBrush = new SolidColorBrush(TextSecondary);

    static Palette()
    {
        foreach (var brush in new Brush[]
        {
            NotchBrush, CardBrush, RingTrackBrush, BarTrackBrush,
            AmpleBrush, WatchBrush, CriticalBrush, TextPrimaryBrush, TextSecondaryBrush
        })
        {
            brush.Freeze();
        }
    }

    public static Color Hex(uint hex) => Color.FromRgb(
        (byte)((hex >> 16) & 0xFF),
        (byte)((hex >> 8) & 0xFF),
        (byte)(hex & 0xFF));
}
