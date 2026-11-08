using System.Runtime.InteropServices;
using TinyHand.Native;

namespace TinyHand;

/// <summary>
/// Low-level mouse hook (the Windows counterpart of the macOS event tap). While installed it
/// swallows button presses so nothing underneath gets clicked, except inside pass-through
/// rectangles (toolbar, taskbar, open menus) or while the interact key is held.
/// Callbacks run on the hook thread: keep them short and hand work off.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    public Action<POINT>? OnMouseDown;
    public Action? OnMouseUp;
    public Action? OnLeftDragged;
    public Action? OnMoved;
    public Action? OnInteractDown;
    public Action? OnInteractUp;
    public Action<POINT>? OnPassThroughPress;

    private const uint InstallMessage = Win32.WM_APP + 10;
    private const uint UninstallMessage = Win32.WM_APP + 11;

    private readonly Thread thread;
    private readonly HookProc proc;
    private readonly ManualResetEventSlim ready = new();
    private uint threadId;
    private IntPtr hook;

    private volatile RECT[] passThrough = Array.Empty<RECT>();
    private volatile int interactVk;
    private volatile bool interactPress;
    private bool passingThrough;
    private bool leftHeld;

    public bool IsInstalled => hook != IntPtr.Zero;

    public RECT[] PassThroughRects
    {
        get => passThrough;
        set => passThrough = value;
    }

    /// <summary>Virtual key that lets clicks through while held (0 = off).</summary>
    public int InteractVk
    {
        get => interactVk;
        set => interactVk = value;
    }

    public bool InteractPressing => interactPress;

    public MouseHook()
    {
        proc = HookCallback;
        thread = new Thread(Run) { IsBackground = true, Name = "TinyHand mouse hook", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait();
    }

    public void SetActive(bool active)
    {
        interactPress = false;
        Win32.PostThreadMessage(threadId, active ? InstallMessage : UninstallMessage, IntPtr.Zero, IntPtr.Zero);
    }

    private void Run()
    {
        threadId = Win32.GetCurrentThreadId();
        // Make sure the thread has a message queue before anyone posts to it.
        Win32.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == InstallMessage && hook == IntPtr.Zero)
            {
                passingThrough = false;
                leftHeld = false;
                hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, proc, Win32.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                {
                    Log.Write($"mouse hook failed: error {Marshal.GetLastWin32Error()}");
                }
            }
            else if (msg.message == UninstallMessage && hook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        if (hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(hook);
        }
    }

    private static bool IsDown(uint m) => m is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN;
    private static bool IsUp(uint m) => m is Win32.WM_LBUTTONUP or Win32.WM_RBUTTONUP or Win32.WM_MBUTTONUP or Win32.WM_XBUTTONUP;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != Win32.HC_ACTION)
        {
            return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
        }
        uint m = (uint)wParam;
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        if (m == Win32.WM_MOUSEMOVE)
        {
            OnMoved?.Invoke();
            if (leftHeld && !interactPress && !passingThrough)
            {
                OnLeftDragged?.Invoke();
            }
            return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        bool down = IsDown(m);
        bool up = IsUp(m);
        if (!down && !up)
        {
            // Wheel and anything else always reach the app.
            return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // Hold the interact key to click for real.
        int vk = interactVk;
        if (down && vk != 0 && Win32.KeyDown(vk))
        {
            interactPress = true;
            OnInteractDown?.Invoke();
        }
        if (interactPress)
        {
            if (up)
            {
                interactPress = false;
                OnInteractUp?.Invoke();
            }
            return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        if (down)
        {
            passingThrough = passThrough.Any(r => r.Contains(info.pt.X, info.pt.Y));
            if (passingThrough)
            {
                OnPassThroughPress?.Invoke(info.pt);
            }
        }
        if (passingThrough)
        {
            if (up)
            {
                passingThrough = false;
            }
            return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        if (down)
        {
            if (m == Win32.WM_LBUTTONDOWN)
            {
                leftHeld = true;
            }
            OnMouseDown?.Invoke(info.pt);
        }
        else
        {
            leftHeld = false;
            OnMouseUp?.Invoke();
        }
        return new IntPtr(1); // swallowed: the app underneath never sees it
    }

    public void Dispose()
    {
        Win32.PostThreadMessage(threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
