using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TinyHand.Native;

namespace TinyHand;

internal sealed record TrayMenuItem(string Text, Action? Action, bool Checked = false, bool Enabled = true)
{
    public static readonly TrayMenuItem Separator = new("-", null);
}

/// <summary>The notification-area icon: click toggles the pointer, right-click opens the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = Win32.WM_APP + 1;
    private const uint IconId = 1;

    private readonly MessageWindow window;
    private readonly uint taskbarCreated;
    private IntPtr icon;
    private bool on;
    private bool added;
    private string tip = "TinyHand";

    public event Action? Clicked;
    public event Action<bool>? MenuOpenChanged;
    public Func<IReadOnlyList<TrayMenuItem>>? BuildMenu { get; set; }

    public TrayIcon(MessageWindow window)
    {
        this.window = window;
        taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
        window.On(CallbackMessage, OnCallback);
        // Explorer restarted: the icon is gone, add it again.
        window.On(taskbarCreated, (_, _) =>
        {
            added = false;
            Show();
            return IntPtr.Zero;
        });
        // Taskbar theme or DPI changed: redraw the glyph in the right colour and size.
        window.On(Win32.WM_SETTINGCHANGE, (_, _) =>
        {
            Update(on, tip);
            return null;
        });
        window.On(Win32.WM_DPICHANGED, (_, _) =>
        {
            Update(on, tip);
            return null;
        });
    }

    public void Show() => Update(on, tip);

    public void Update(bool active, string tooltip)
    {
        on = active;
        tip = tooltip;
        var old = icon;
        icon = MakeIcon(active);
        var data = Data();
        data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = icon;
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (!added)
        {
            added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
            data.uVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
        }
        else
        {
            Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
        }
        if (old != IntPtr.Zero)
        {
            Win32.DestroyIcon(old);
        }
    }

    /// <summary>The icon's rectangle on screen, in physical pixels (empty when hidden in the overflow).</summary>
    public RECT Bounds
    {
        get
        {
            var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = window.Handle, uID = IconId };
            return Win32.Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect : default;
        }
    }

    private NOTIFYICONDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = window.Handle,
        uID = IconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr? OnCallback(IntPtr wParam, IntPtr lParam)
    {
        uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
        switch (evt)
        {
            case Win32.NIN_SELECT:
            case Win32.NIN_KEYSELECT:
                Clicked?.Invoke();
                break;
            case Win32.WM_CONTEXTMENU:
                int x = (short)(wParam.ToInt64() & 0xFFFF);
                int y = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                ShowMenu(x, y);
                break;
        }
        return IntPtr.Zero;
    }

    public void ShowMenu(int x, int y)
    {
        var items = BuildMenu?.Invoke();
        if (items == null || items.Count == 0)
        {
            return;
        }
        var menu = Win32.CreatePopupMenu();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == TrayMenuItem.Separator)
                {
                    Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                uint flags = Win32.MF_STRING | (item.Checked ? Win32.MF_CHECKED : 0) | (item.Enabled ? 0 : Win32.MF_GRAYED);
                Win32.AppendMenu(menu, flags, (UIntPtr)(i + 1), item.Text);
            }
            // Required so the menu closes when the user clicks elsewhere.
            Win32.SetForegroundWindow(window.Handle);
            MenuOpenChanged?.Invoke(true);
            int chosen = Win32.TrackPopupMenuEx(menu,
                Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY | Win32.TPM_BOTTOMALIGN | Win32.TPM_RIGHTALIGN,
                x, y, window.Handle, IntPtr.Zero);
            Win32.PostMessage(window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            MenuOpenChanged?.Invoke(false);
            if (chosen > 0 && chosen <= items.Count)
            {
                items[chosen - 1].Action?.Invoke();
            }
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    /// <summary>Recolour the template glyph (black on transparent) for the taskbar theme.</summary>
    private static IntPtr MakeIcon(bool active)
    {
        var path = Paths.Asset(active ? "tray_on@3x.png" : "tray_off@3x.png");
        if (path == null)
        {
            return IntPtr.Zero;
        }
        var monitor = Win32.MonitorFromPoint(new POINT(0, 0), Win32.MONITOR_DEFAULTTOPRIMARY);
        int size = Win32.GetSystemMetricsForDpi(49 /* SM_CXSMICON */, Win32.MonitorDpi(monitor));
        if (size <= 0)
        {
            size = 16;
        }

        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var scaled = new TransformedBitmap(frame, new ScaleTransform((double)size / frame.PixelWidth, (double)size / frame.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth;
        int h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        byte shade = TaskbarIsLight() ? (byte)0x1A : (byte)0xFF;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = shade;
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = Win32.CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        // All-zero monochrome mask (rows are word aligned); the colour bitmap's alpha does the work.
        var maskBits = new byte[(w + 15) / 16 * 2 * h];
        var pin = GCHandle.Alloc(maskBits, GCHandleType.Pinned);
        var mask = Win32.CreateBitmap(w, h, 1, 1, pin.AddrOfPinnedObject());
        pin.Free();
        var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var hicon = Win32.CreateIconIndirect(ref info);
        Win32.DeleteObject(color);
        Win32.DeleteObject(mask);
        return hicon;
    }

    public void Dispose()
    {
        var data = Data();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
        if (icon != IntPtr.Zero)
        {
            Win32.DestroyIcon(icon);
        }
    }
}
