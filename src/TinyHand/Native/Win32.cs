using System.Runtime.InteropServices;

namespace TinyHand.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public readonly bool IsEmpty => Right <= Left || Bottom <= Top;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public readonly RECT Inflate(int dx, int dy) => new(Left - dx, Top - dy, Right + dx, Bottom + dy);

    public override readonly string ToString() => $"{Left},{Top} {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX
{
    public int cbSize;
    public uint style;
    public IntPtr lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public uint uVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NOTIFYICONIDENTIFIER
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public Guid guidItem;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    public bool fIcon;
    public int xHotspot;
    public int yHotspot;
    public IntPtr hbmMask;
    public IntPtr hbmColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public int biSize;
    public int biWidth;
    public int biHeight;
    public short biPlanes;
    public short biBitCount;
    public int biCompression;
    public int biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public int biClrUsed;
    public int biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}

internal delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

internal static class Win32
{
    // Window messages
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP = 0x020C;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_ENTERMENULOOP = 0x0211;
    public const uint WM_EXITMENULOOP = 0x0212;

    public const int HTTRANSPARENT = -1;
    public const int MA_NOACTIVATE = 3;

    // Window styles
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const int GWL_EXSTYLE = -20;
    public const uint LWA_ALPHA = 0x2;

    public static readonly IntPtr HWND_MESSAGE = new(-3);
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // Hotkey modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Virtual keys
    public const int VK_LBUTTON = 0x01;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    // Hooks
    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;
    public const uint LLMHF_INJECTED = 0x1;

    // Monitors
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;

    // Tray
    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;
    public const uint NIF_GUID = 0x20;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = NIN_SELECT + 1;

    // Menus
    public const uint MF_STRING = 0x0;
    public const uint MF_SEPARATOR = 0x800;
    public const uint MF_CHECKED = 0x8;
    public const uint MF_GRAYED = 0x1;
    public const uint TPM_RIGHTBUTTON = 0x2;
    public const uint TPM_RETURNCMD = 0x100;
    public const uint TPM_NONOTIFY = 0x80;
    public const uint TPM_BOTTOMALIGN = 0x20;
    public const uint TPM_RIGHTALIGN = 0x8;

    // Cursors
    public const uint SPI_SETCURSORS = 0x0057;
    public static readonly uint[] SystemCursorIds =
    {
        32512, // OCR_NORMAL
        32513, // OCR_IBEAM
        32514, // OCR_WAIT
        32515, // OCR_CROSS
        32516, // OCR_UP
        32642, // OCR_SIZENWSE
        32643, // OCR_SIZENESW
        32644, // OCR_SIZEWE
        32645, // OCR_SIZENS
        32646, // OCR_SIZEALL
        32648, // OCR_NO
        32649, // OCR_HAND
        32650, // OCR_APPSTARTING
        32651, // OCR_HELP
        32671, // OCR_PIN
        32672, // OCR_PERSON
    };

    // Appbar
    public const uint ABM_GETTASKBARPOS = 0x5;

    // PlaySound
    public const uint SND_ASYNC = 0x0001;
    public const uint SND_NODEFAULT = 0x0002;
    public const uint SND_MEMORY = 0x0004;
    public const uint SND_PURGE = 0x0040;

    public const uint INFINITE = 0xFFFFFFFF;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint PM_REMOVE = 0x0001;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;

    // user32
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[]? handles, uint milliseconds, uint wakeMask, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, char[] buffer, int max);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetSystemCursor(IntPtr cursor, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateCursor(IntPtr instance, int xHot, int yHot, int width, int height, byte[] andPlane, byte[] xorPlane);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    // shell32
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);

    [DllImport("shell32.dll")]
    public static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    // gdi32
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    // kernel32
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // winmm
    [DllImport("winmm.dll", SetLastError = true)]
    public static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint period);

    // dwmapi
    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_MAINWINDOW = 2;
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    // uxtheme: lets Win32 popup menus follow the system dark mode.
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    public static void AllowDarkMenus()
    {
        try
        {
            SetPreferredAppMode(1); // AllowDark
            FlushMenuThemes();
        }
        catch
        {
            // Older builds without the ordinal: menus stay light.
        }
    }

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        int n = GetClassName(hwnd, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    public static MONITORINFO MonitorInfo(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        return info;
    }

    public static uint MonitorDpi(IntPtr monitor)
    {
        return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint x, out _) == 0 ? x : 96;
    }

    public static POINT CursorPos()
    {
        GetCursorPos(out var p);
        return p;
    }
}
