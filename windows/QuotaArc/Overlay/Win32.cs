using System.Runtime.InteropServices;
using System.Windows;

namespace QuotaArc.Overlay;

internal static class Win32
{
    public const int GwlExStyle = -20;
    public const int WsExTransparent = 0x00000020;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExLayered = 0x00080000;
    public const int WsExTopmost = 0x00000008;

    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const int WmNchittest = 0x0084;
    public const int HtTransparent = -1;
    public const int HtClient = 1;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);

    public static void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, value);
        else SetWindowLong32(hWnd, nIndex, value.ToInt32());
    }

    public static void AddExtendedStyle(IntPtr hwnd, int style)
    {
        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)(ex | (uint)style));
    }

    public static void SetClickThrough(IntPtr hwnd, bool clickThrough)
    {
        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if (clickThrough) ex |= WsExTransparent;
        else ex &= ~WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)ex);
    }

    public static double DpiFor(System.Windows.Forms.Screen screen)
    {
        var r = new RECT
        {
            Left = screen.Bounds.Left,
            Top = screen.Bounds.Top,
            Right = screen.Bounds.Right,
            Bottom = screen.Bounds.Bottom
        };
        var mon = MonitorFromRect(ref r, 2);
        if (GetDpiForMonitor(mon, 0, out var dpiX, out _) == 0 && dpiX > 0)
            return dpiX;
        return 96;
    }

    public static Point CursorDip(double scale)
    {
        GetCursorPos(out var pt);
        return new Point(pt.X * scale, pt.Y * scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
