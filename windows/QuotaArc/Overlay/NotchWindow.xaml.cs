using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuotaArc.Notch;

namespace QuotaArc.Overlay;

public partial class NotchWindow : Window
{
    internal NotchSurface Surface { get; }
    public IntPtr Handle { get; private set; }
    private bool? _clickThrough;

    internal Action? Clicked { get; set; }
    internal Action? RightClicked { get; set; }
    internal Action<Vector>? Nudged { get; set; }

    internal NotchWindow(NotchViewModel model)
    {
        InitializeComponent();
        Surface = new NotchSurface(model);
        Host.Content = Surface;
        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += (_, _) => _nudgeStart = null;
        MouseMove += OnMouseMove;
        MouseRightButtonUp += (_, _) => RightClicked?.Invoke();
        Cursor = Cursors.Arrow;
    }

    private Point? _nudgeStart;

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _nudgeStart = e.GetPosition(this);
            CaptureMouse();
            return;
        }
        Clicked?.Invoke();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_nudgeStart is not { } start || e.LeftButton != MouseButtonState.Pressed
            || !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            if (_nudgeStart is not null && e.LeftButton != MouseButtonState.Pressed)
            {
                _nudgeStart = null;
                ReleaseMouseCapture();
            }
            return;
        }

        var now = e.GetPosition(this);
        var delta = now - start;
        _nudgeStart = now;
        Nudged?.Invoke(delta);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        Win32.AddExtendedStyle(Handle, Win32.WsExNoActivate | Win32.WsExToolWindow | Win32.WsExLayered);
        var source = HwndSource.FromHwnd(Handle);
        if (source?.CompositionTarget is { } target)
            target.RenderMode = RenderMode.Default;
        source?.AddHook(WndProc);
        Win32.SetWindowPos(Handle, Win32.HwndTopmost, 0, 0, 0, 0,
            Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(Win32.MaNoActivate);
        }
        return IntPtr.Zero;
    }

    public void Place(Rect frame)
    {
        const double eps = 0.25;
        if (Math.Abs(Left - frame.X) < eps && Math.Abs(Top - frame.Y) < eps
            && Math.Abs(Width - frame.Width) < eps && Math.Abs(Height - frame.Height) < eps)
            return;
        Left = frame.X;
        Top = frame.Y;
        Width = frame.Width;
        Height = frame.Height;
    }

    public void SetClickThrough(bool ignore)
    {
        if (_clickThrough == ignore) return;
        _clickThrough = ignore;
        if (Handle != IntPtr.Zero)
            Win32.SetClickThrough(Handle, ignore);
    }

    public Point LocalCursor()
    {
        var scale = ScreenQuery.DipScale();
        var screen = Win32.CursorDip(scale);
        return new Point(screen.X - Left, screen.Y - Top);
    }
}
