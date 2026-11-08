using System.Windows.Threading;
using TinyHand.Native;
using TinyHand.Overlay;
using TinyHand.UI;

namespace TinyHand;

/// <summary>
/// Turns the pointer on and off and keeps the pieces in step: the GPU overlay, the mouse hook,
/// the hidden system cursor, the toolbar, and the zones where real clicks are allowed
/// (taskbar, open menus, the toolbar itself). Runs on the UI thread.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private static readonly string[] MenuClasses =
    {
        "#32768",                                   // classic Win32 menus (tray menu, context menus)
        "Xaml_WindowedPopupClass",                  // WinUI/XAML popups (Explorer context menu)
        "Microsoft.UI.Content.PopupWindowSiteBridge",
        "DropDown",
    };

    private static readonly string[] ShellSurfaceClasses =
    {
        "Windows.UI.Core.CoreWindow",               // Start, Search, Notification Center
        "XamlExplorerHostIslandWindow",             // Task view, Alt+Tab, Snap layouts
        "TopLevelWindowForOverflowXamlIsland",      // tray overflow
        "Shell_Flyout",
        "NotifyIconOverflowWindow",
    };

    private readonly OverlayRenderer renderer = new();
    private readonly MouseHook hook = new();
    private readonly TapSound tapSound = new(Preferences.TapSound);
    private readonly Toolbar toolbar = new();
    private readonly HotKeys hotKeys;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;

    private MonitorState monitor;
    private DateTime revealUntil = DateTime.MinValue;
    private DateTime menuOpeningUntil = DateTime.MinValue;
    private DateTime lastButtonEvent = DateTime.MinValue;
    private DateTime lastMenuScan = DateTime.MinValue;
    private DateTime lastShellScan = DateTime.MinValue;
    private RECT[] menuRects = Array.Empty<RECT>();
    private RECT[] taskbarRects = Array.Empty<RECT>();
    private bool normalCursor;
    private bool ownMenuOpen;
    private int? escHotKey;
    private PointerStyle style;

    public bool IsActive { get; private set; }
    public event Action? ActiveChanged;
    public event Action? SoundChanged;
    public Func<RECT[]>? ExtraPassThrough { get; set; }

    public OverlayController(HotKeys hotKeys)
    {
        this.hotKeys = hotKeys;
        dispatcher = Dispatcher.CurrentDispatcher;
        timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => UpdateZones();

        style = PointerStyle.Saved;
        renderer.SetStyle(style);
        renderer.MonitorChanged += m => dispatcher.BeginInvoke(() => OnMonitorChanged(m));

        hook.OnMoved = renderer.Wake;
        hook.OnMouseDown = p =>
        {
            renderer.MouseDown(p);
            tapSound.Play();
            dispatcher.BeginInvoke(() =>
            {
                toolbar.HideHint();
                lastButtonEvent = DateTime.Now;
            });
        };
        hook.OnMouseUp = () =>
        {
            renderer.MouseUp();
            dispatcher.BeginInvoke(() => lastButtonEvent = DateTime.Now);
        };
        hook.OnInteractDown = renderer.InteractDown;
        hook.OnInteractUp = renderer.InteractUp;
        hook.OnPassThroughPress = p => dispatcher.BeginInvoke(() =>
        {
            if (taskbarRects.Any(r => r.Contains(p.X, p.Y)))
            {
                // A click on the taskbar may open a menu or flyout; let the next clicks through too.
                menuOpeningUntil = DateTime.Now.AddSeconds(1);
            }
            lastButtonEvent = DateTime.Now;
        });

        toolbar.Picked += Pick;
        toolbar.SoundToggled += () => SoundOn = !SoundOn;
        toolbar.ExitClicked += Exit;
        toolbar.Select(style);
        SyncSound();

        Preferences.Changed += () =>
        {
            renderer.PreferencesChanged();
            tapSound.Choice = Preferences.TapSound;
            hook.InteractVk = Preferences.InteractKey.VirtualKey();
            SyncSound();
            var saved = PointerStyle.Saved;
            if (saved != style)
            {
                Pick(saved);
            }
        };
        CustomHands.Changed += () =>
        {
            if (PointerStyle.Find(style.Id) == null)
            {
                Pick(PointerStyle.Lavender);
            }
        };
    }

    public PointerStyle Style => style;

    public bool SoundOn
    {
        get => style.IsLaser ? Preferences.LaserSound : Preferences.HandSound;
        set
        {
            if (style.IsLaser)
            {
                Preferences.LaserSound = value;
            }
            else
            {
                Preferences.HandSound = value;
            }
        }
    }

    public void Pick(PointerStyle next)
    {
        style = next;
        renderer.SetStyle(next);
        if (PointerStyle.Saved != next)
        {
            PointerStyle.Saved = next;
        }
        toolbar.Select(next);
        SyncSound();
    }

    private void SyncSound()
    {
        tapSound.Enabled = SoundOn;
        toolbar.SetSoundOn(SoundOn);
        SoundChanged?.Invoke();
    }

    public void Toggle()
    {
        if (IsActive)
        {
            Deactivate();
        }
        else
        {
            Activate();
        }
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }
        IsActive = true;
        tapSound.Hold(true);
        CursorHider.Hide();
        normalCursor = false;
        hook.InteractVk = Preferences.InteractKey.VirtualKey();
        hook.SetActive(true);
        renderer.SetActive(true);

        monitor = CurrentMonitor();
        toolbar.PlaceOn(monitor);
        toolbar.Show();
        revealUntil = DateTime.Now.AddSeconds(3);
        if (!Preferences.HintShown)
        {
            Preferences.HintShown = true;
            var parts = new List<(string, string)> { ("Click", "to tap"), ("Drag", "to draw") };
            if (Preferences.InteractKey != InteractKey.Off)
            {
                parts.Add(($"Hold {Preferences.InteractKey.Title()}", "to click for real"));
            }
            parts.Add(("Esc", "to exit"));
            toolbar.ShowHint(parts);
            revealUntil = DateTime.Now.AddSeconds(10);
            var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            hide.Tick += (_, _) =>
            {
                hide.Stop();
                toolbar.HideHint();
            };
            hide.Start();
        }
        SetEscEnabled(true);
        timer.Start();
        UpdateZones();
        Log.Write($"activate on {monitor.Bounds} dpi={monitor.Dpi}");
        ActiveChanged?.Invoke();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }
        IsActive = false;
        timer.Stop();
        tapSound.Hold(false);
        hook.SetActive(false);
        hook.PassThroughRects = Array.Empty<RECT>();
        renderer.SetActive(false);
        toolbar.Hide();
        SetEscEnabled(false);
        ownMenuOpen = false;
        normalCursor = false;
        CursorHider.Show();
        ActiveChanged?.Invoke();
    }

    /// <summary>Clear the drawing and turn the pointer off.</summary>
    public void Exit()
    {
        ClearInk();
        Deactivate();
    }

    public void ClearInk() => renderer.ClearInk();

    /// <summary>Our own tray menu is open: let clicks through and show the real cursor.</summary>
    public void SuspendInput(bool suspended)
    {
        if (!IsActive)
        {
            return;
        }
        ownMenuOpen = suspended;
        UpdateZones();
    }

    private void OnMonitorChanged(MonitorState m)
    {
        bool moving = monitor.Handle != IntPtr.Zero && monitor.Handle != m.Handle;
        monitor = m;
        lastShellScan = DateTime.MinValue;
        if (IsActive)
        {
            toolbar.PlaceOn(m);
            if (moving)
            {
                revealUntil = DateTime.Now.AddSeconds(1.5);
            }
        }
    }

    private static MonitorState CurrentMonitor()
    {
        var handle = Win32.MonitorFromPoint(Win32.CursorPos(), Win32.MONITOR_DEFAULTTONEAREST);
        var info = Win32.MonitorInfo(handle);
        return new MonitorState(handle, info.rcMonitor, info.rcWork, Win32.MonitorDpi(handle));
    }

    private void UpdateZones()
    {
        if (!IsActive)
        {
            return;
        }
        var now = DateTime.Now;
        var mouse = Win32.CursorPos();
        bool drawing = renderer.IsDrawing;

        var f = toolbar.Frame;
        double s = monitor.Scale;
        int padX = (int)(80 * s), up = (int)(90 * s), pad = (int)(10 * s);
        bool near = !f.IsEmpty && new RECT(f.Left - padX, f.Top - up - pad, f.Right + padX, f.Bottom + pad).Contains(mouse.X, mouse.Y);
        toolbar.SetRevealed(!drawing && (near || now < revealUntil));

        if (now - lastShellScan > TimeSpan.FromSeconds(1))
        {
            lastShellScan = now;
            taskbarRects = FindTaskbars(monitor);
        }
        bool overTaskbar = taskbarRects.Any(r => r.Contains(mouse.X, mouse.Y));
        bool menuLikely = menuRects.Length > 0 || ownMenuOpen || now < menuOpeningUntil
                          || now - lastButtonEvent < TimeSpan.FromSeconds(2) || overTaskbar;
        if (now - lastMenuScan > TimeSpan.FromMilliseconds(menuLikely ? 50 : 250))
        {
            lastMenuScan = now;
            menuRects = FindMenus();
        }
        bool overMenu = overTaskbar || menuRects.Any(r => r.Contains(mouse.X, mouse.Y)) || now < menuOpeningUntil;
        bool interacting = Preferences.InteractKey.IsHeld() || hook.InteractPressing;
        bool normal = !drawing && !Preferences.InteractKeepsHand && (interacting || overMenu || ownMenuOpen);
        SetEscEnabled(menuRects.Length == 0 && !ownMenuOpen);

        if (normal != normalCursor)
        {
            normalCursor = normal;
            renderer.SetPointerHidden(normal);
            if (normal)
            {
                CursorHider.Show();
            }
            else
            {
                CursorHider.Hide();
            }
        }

        var rects = new List<RECT>(ExtraPassThrough?.Invoke() ?? Array.Empty<RECT>());
        rects.AddRange(taskbarRects);
        rects.AddRange(menuRects);
        if (toolbar.Revealed)
        {
            rects.Add(toolbar.Frame);
        }
        if (ownMenuOpen)
        {
            rects.Add(monitor.Bounds);
        }
        hook.PassThroughRects = rects.ToArray();
    }

    private void SetEscEnabled(bool enabled)
    {
        if (enabled && IsActive && escHotKey == null)
        {
            escHotKey = hotKeys.Register(Win32.VK_ESCAPE, 0, Exit);
        }
        if (!enabled && escHotKey is { } id)
        {
            hotKeys.Unregister(id);
            escHotKey = null;
        }
    }

    /// <summary>Taskbars on this monitor (the Windows counterpart of the macOS menu bar zone).</summary>
    private static RECT[] FindTaskbars(MonitorState m)
    {
        var found = new List<RECT>();
        Win32.EnumWindows((h, _) =>
        {
            var cls = Win32.ClassName(h);
            if ((cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") && Win32.IsWindowVisible(h)
                && Win32.GetWindowRect(h, out var r) && Intersects(r, m.Bounds))
            {
                found.Add(r);
            }
            return true;
        }, IntPtr.Zero);
        if (found.Count == 0)
        {
            // Fall back to the strip the work area leaves free.
            var b = m.Bounds;
            var w = m.Work;
            if (w.Bottom < b.Bottom) found.Add(new RECT(b.Left, w.Bottom, b.Right, b.Bottom));
            if (w.Top > b.Top) found.Add(new RECT(b.Left, b.Top, b.Right, w.Top));
            if (w.Left > b.Left) found.Add(new RECT(b.Left, b.Top, w.Left, b.Bottom));
            if (w.Right < b.Right) found.Add(new RECT(w.Right, b.Top, b.Right, b.Bottom));
        }
        return found.ToArray();
    }

    private static bool Intersects(RECT a, RECT b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>Open menus, popups and shell flyouts: clicks there go through and the real cursor shows.</summary>
    private static RECT[] FindMenus()
    {
        var found = new List<RECT>();
        var fg = Win32.GetForegroundWindow();
        Win32.EnumWindows((h, _) =>
        {
            if (!Win32.IsWindowVisible(h))
            {
                return true;
            }
            var cls = Win32.ClassName(h);
            bool menu = Array.IndexOf(MenuClasses, cls) >= 0;
            bool shell = h == fg && Array.IndexOf(ShellSurfaceClasses, cls) >= 0;
            if ((menu || shell) && Win32.GetWindowRect(h, out var r) && !r.IsEmpty)
            {
                found.Add(r);
            }
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    public void Dispose()
    {
        Deactivate();
        hook.Dispose();
        renderer.Dispose();
        tapSound.Dispose();
    }
}
