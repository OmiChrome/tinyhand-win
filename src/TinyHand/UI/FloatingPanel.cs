using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using TinyHand.Native;

namespace TinyHand.UI;

/// <summary>
/// A borderless, always-on-top panel that never takes focus (toolbar, hint, ink tray).
/// Positioned in physical pixels so it lands exactly where the overlay expects it on any monitor.
/// </summary>
internal class FloatingPanel : Window
{
    public const double ShadowMargin = 14;

    private IntPtr hwnd;
    private bool clickThrough;
    private Func<FloatingPanel, (int X, int Y)>? placer;

    protected Border Card { get; }

    public FloatingPanel(double radius)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        Card = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(ShadowMargin),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.35,
                Color = Colors.Black,
                RenderingBias = RenderingBias.Performance,
            },
        };
        Content = Card;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition);
    }

    public IntPtr Handle => hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        ApplyExStyle();
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg == (int)Win32.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Win32.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>Make the window ignore the mouse entirely (clicks go to whatever is below).</summary>
    public void SetClickThrough(bool value)
    {
        if (clickThrough == value)
        {
            return;
        }
        clickThrough = value;
        ApplyExStyle();
    }

    private void ApplyExStyle()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW;
        if (clickThrough)
        {
            ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED;
        }
        else
        {
            ex &= ~(long)Win32.WS_EX_TRANSPARENT;
        }
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Show without activating. Creates the window the first time.</summary>
    public void ShowPanel()
    {
        if (!IsVisible)
        {
            Show();
        }
        Reposition();
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Keep the panel where the placer says, re-applied after size or DPI changes.</summary>
    public void PlaceWith(Func<FloatingPanel, (int X, int Y)> place)
    {
        placer = place;
        Reposition();
    }

    public void Reposition()
    {
        if (hwnd == IntPtr.Zero || placer == null)
        {
            return;
        }
        var (x, y) = placer(this);
        Win32.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>The whole window in physical pixels, including the shadow margin.</summary>
    public RECT WindowPixels
    {
        get
        {
            if (hwnd == IntPtr.Zero)
            {
                return default;
            }
            Win32.GetWindowRect(hwnd, out var r);
            return r;
        }
    }

    public double PixelScale => hwnd == IntPtr.Zero ? 1 : Win32.GetDpiForWindow(hwnd) / 96.0;

    /// <summary>The visible card in physical pixels (no shadow margin).</summary>
    public RECT CardPixels
    {
        get
        {
            var r = WindowPixels;
            int m = (int)Math.Round(ShadowMargin * PixelScale);
            return r.IsEmpty ? r : new RECT(r.Left + m, r.Top + m, r.Right - m, r.Bottom - m);
        }
    }

    public void FadeTo(double opacity, double seconds, Action? done = null)
    {
        var anim = new DoubleAnimation(opacity, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.HoldEnd };
        if (done != null)
        {
            anim.Completed += (_, _) => done();
        }
        BeginAnimation(OpacityProperty, anim);
    }
}
