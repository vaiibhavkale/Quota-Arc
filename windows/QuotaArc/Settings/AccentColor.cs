using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using QuotaArc.Design;
using MediaColor = System.Windows.Media.Color;

namespace QuotaArc.Settings;

internal enum AccentColorChoice
{
    System,
    Pink,
    Red,
    Orange,
    Yellow,
    Green,
    Teal,
    Blue,
    Indigo,
    Purple,
    OffWhite
}

internal static class AccentColorInfo
{
    public static AccentColorChoice[] All { get; } = Enum.GetValues<AccentColorChoice>();

    public static string Raw(this AccentColorChoice choice) => choice switch
    {
        AccentColorChoice.Pink => "ff33e1",
        AccentColorChoice.Red => "eb4236",
        AccentColorChoice.Orange => "eb8436",
        AccentColorChoice.Yellow => "ffd400",
        AccentColorChoice.Green => "00ff88",
        AccentColorChoice.Teal => "00e5cc",
        AccentColorChoice.Blue => "36a8eb",
        AccentColorChoice.Indigo => "6c5ce7",
        AccentColorChoice.Purple => "b026ff",
        AccentColorChoice.OffWhite => "f7f6f5",
        _ => "system"
    };

    public static AccentColorChoice Parse(string? raw) => raw switch
    {
        "ff33e1" => AccentColorChoice.Pink,
        "eb4236" => AccentColorChoice.Red,
        "eb8436" => AccentColorChoice.Orange,
        "ffd400" => AccentColorChoice.Yellow,
        "00ff88" => AccentColorChoice.Green,
        "00e5cc" => AccentColorChoice.Teal,
        "36a8eb" => AccentColorChoice.Blue,
        "6c5ce7" => AccentColorChoice.Indigo,
        "b026ff" => AccentColorChoice.Purple,
        "f7f6f5" => AccentColorChoice.OffWhite,
        _ => AccentColorChoice.System
    };

    public static Color Color(this AccentColorChoice choice) => choice switch
    {
        AccentColorChoice.Pink => Palette.Hex(0xFF33E1),
        AccentColorChoice.Red => Palette.Hex(0xEB4236),
        AccentColorChoice.Orange => Palette.Hex(0xEB8436),
        AccentColorChoice.Yellow => Palette.Hex(0xFFD400),
        AccentColorChoice.Green => Palette.Ample,
        AccentColorChoice.Teal => Palette.Hex(0x00E5CC),
        AccentColorChoice.Blue => Palette.Hex(0x36A8EB),
        AccentColorChoice.Indigo => Palette.Hex(0x6C5CE7),
        AccentColorChoice.Purple => Palette.Hex(0xB026FF),
        AccentColorChoice.OffWhite => Palette.Hex(0xF7F6F5),
        _ => DeviceAccent()
    };

    private static Color DeviceAccent()
    {
        try
        {
            if (DwmGetColorizationColor(out var colorization, out _) == 0)
            {
                var r = (byte)((colorization >> 16) & 0xFF);
                var g = (byte)((colorization >> 8) & 0xFF);
                var b = (byte)(colorization & 0xFF);
                if (r + g + b > 0) return MediaColor.FromRgb(r, g, b);
            }
        }
        catch { /* fall through */ }

        var glass = SystemParameters.WindowGlassColor;
        if (glass.R + glass.G + glass.B > 0)
            return MediaColor.FromRgb(glass.R, glass.G, glass.B);
        return Palette.Ample;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

    public static Brush Brush(this AccentColorChoice choice)
    {
        var brush = new SolidColorBrush(choice.Color());
        brush.Freeze();
        return brush;
    }
}
