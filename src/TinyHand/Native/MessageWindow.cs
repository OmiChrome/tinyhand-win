using System.Runtime.InteropServices;

namespace TinyHand.Native;

/// <summary>
/// A hidden message-only window on the UI thread. It receives hotkeys, tray icon callbacks
/// and the "another copy was launched" message. WPF's dispatcher pumps its messages.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    public const string ClassName = "TinyHand.MessageWindow";

    private readonly WndProc proc;
    private readonly Dictionary<uint, Func<IntPtr, IntPtr, IntPtr?>> handlers = new();

    public IntPtr Handle { get; }

    public MessageWindow()
    {
        proc = WindowProc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = ClassName,
        };
        Win32.RegisterClassEx(ref wc);
        // A regular hidden top-level window rather than HWND_MESSAGE so it also receives
        // broadcasts such as TaskbarCreated and WM_SETTINGCHANGE.
        Handle = Win32.CreateWindowEx(Win32.WS_EX_TOOLWINDOW, ClassName, "TinyHand", Win32.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>Handle a message. Return a value to stop default processing, or null to fall through.</summary>
    public void On(uint message, Func<IntPtr, IntPtr, IntPtr?> handler) => handlers[message] = handler;

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (handlers.TryGetValue(msg, out var handler))
        {
            try
            {
                var result = handler(wParam, lParam);
                if (result.HasValue)
                {
                    return result.Value;
                }
            }
            catch (Exception e)
            {
                Log.Write($"message 0x{msg:X} handler failed: {e}");
            }
        }
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose() => Win32.DestroyWindow(Handle);
}
