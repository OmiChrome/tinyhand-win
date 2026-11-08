using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using TinyHand.Native;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using AlphaMode = Vortice.DXGI.AlphaMode;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using InterpolationMode = Vortice.Direct2D1.InterpolationMode;
using RawRectF = Vortice.RawRectF;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace TinyHand.Overlay;

internal readonly record struct MonitorState(IntPtr Handle, RECT Bounds, RECT Work, uint Dpi)
{
    public float Scale => Dpi / 96f;
}

/// <summary>
/// The full-screen, click-through overlay that draws the hand on a stick, the laser dot and the ink.
/// It owns a render thread: a topmost layered window whose content is a DirectComposition swap chain
/// drawn with Direct2D, so it costs almost nothing when idle and stays smooth at the display's refresh
/// rate while moving. Public methods are thread safe; they queue work for the render thread.
/// </summary>
internal sealed class OverlayRenderer : IDisposable
{
    private const string WindowClass = "TinyHand.Overlay";

    // Same proportions as the macOS ArmView.
    private const double MinRodFrac = 0.05;
    private const double GripLeanFrac = 0.30;
    private const double GripDepthFrac = 0.12;
    private const double GripFollow = 0.08;
    private const double LiftFrac = 0.06;
    private const double ReferenceRate = 120; // the macOS easing constants are per 1/120 s tick

    private readonly Thread thread;
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly ManualResetEventSlim started = new();
    private readonly WndProc proc;
    private volatile bool quit;

    /// <summary>Raised on the render thread when the pointer moves to another monitor.</summary>
    public event Action<MonitorState>? MonitorChanged;

    // Render-thread state
    private IntPtr hwnd;
    private bool active;
    private MonitorState monitor;
    private ID3D11Device? d3d;
    private IDXGIDevice1? dxgi;
    private ID2D1Factory1? factory;
    private ID2D1Device? d2dDevice;
    private ID2D1DeviceContext1? ctx;
    private IDXGISwapChain1? swapChain;
    private ID2D1Bitmap1? target;
    private IDCompositionDevice? dcomp;
    private IDCompositionTarget? dcompTarget;
    private IDCompositionVisual? visual;
    private ID2D1SolidColorBrush? brush;
    private ID2D1StrokeStyle? roundStroke;
    private Shadow? shadow;
    private ID2D1Bitmap1? handBitmap;
    private string? handBitmapName;

    private PointerStyle style = PointerStyle.Lavender;
    private HandAsset? asset;
    private readonly Ink ink = new();
    private bool held;
    private bool pointerHidden;
    private double pressAmount;
    private double? gripX;
    private double gripTarget;
    private Vector2? downPoint;
    private double handLenFrac = Preferences.PointerSize;
    private bool showShadow = Preferences.ShowShadow;
    private double[] lastPose = Array.Empty<double>();
    private bool needsFrame = true;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTick;
    private double lastTopmost;

    // Shared, read from other threads
    private volatile bool drawing;
    private volatile bool settling;

    public bool IsDrawing => drawing;
    public bool IsSettling => settling;

    public OverlayRenderer()
    {
        proc = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "TinyHand overlay", Priority = ThreadPriority.AboveNormal };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        started.Wait();
    }

    // ---- Thread-safe API -------------------------------------------------------------------

    public void Wake() => wake.Set();

    private void Post(Action action)
    {
        commands.Enqueue(action);
        wake.Set();
    }

    public void SetActive(bool value) => Post(() => SetActiveNow(value));

    public void SetStyle(PointerStyle value) => Post(() =>
    {
        style = value;
        asset = null;
        assetLoaded = false;
        UpdateInkColor();
        needsFrame = true;
    });

    private bool assetLoaded;

    /// <summary>Load the hand image the first time it's needed (keeps startup light).</summary>
    private void EnsureAsset()
    {
        if (assetLoaded)
        {
            return;
        }
        assetLoaded = true;
        asset = style.AssetName is { } name ? HandAsset.Load(name) : null;
        if (!style.IsLaser && asset == null)
        {
            Log.Write($"hand {style.Id} unavailable; using the laser dot");
        }
    }

    public void PreferencesChanged() => Post(() =>
    {
        handLenFrac = Preferences.PointerSize;
        showShadow = Preferences.ShowShadow;
        UpdateInkColor();
        needsFrame = true;
    });

    /// <summary>A tap: animate the press and start a possible drag at <paramref name="screenPoint"/>.</summary>
    public void MouseDown(POINT screenPoint) => Post(() =>
    {
        pressAmount = 1;
        held = true;
        downPoint = Local(screenPoint);
        needsFrame = true;
    });

    public void MouseUp() => Post(Release);

    /// <summary>Interact key presses: animate the press but never draw.</summary>
    public void InteractDown() => Post(() =>
    {
        pressAmount = 1;
        held = true;
        needsFrame = true;
    });

    public void InteractUp() => Post(() =>
    {
        held = false;
        needsFrame = true;
    });

    public void ClearInk() => Post(() =>
    {
        ink.Clear();
        drawing = false;
        needsFrame = true;
    });

    public void SetPointerHidden(bool hidden) => Post(() =>
    {
        pointerHidden = hidden;
        needsFrame = true;
    });

    // ---- Render thread ---------------------------------------------------------------------

    private void Run()
    {
        try
        {
            CreateWindow();
        }
        catch (Exception e)
        {
            Log.Write($"overlay window failed: {e}");
        }
        started.Set();

        var handles = new[] { wake.SafeWaitHandle.DangerousGetHandle() };
        while (!quit)
        {
            Pump();
            while (commands.TryDequeue(out var cmd))
            {
                try
                {
                    cmd();
                }
                catch (Exception e)
                {
                    Log.Write($"overlay command failed: {e}");
                }
            }
            if (!active || ctx == null)
            {
                Win32.MsgWaitForMultipleObjectsEx(1, handles, Win32.INFINITE, Win32.QS_ALLINPUT, Win32.MWMO_INPUTAVAILABLE);
                continue;
            }

            bool changed;
            try
            {
                long t0 = Stopwatch.GetTimestamp();
                changed = Tick();
                long t1 = Stopwatch.GetTimestamp();
                if (changed)
                {
                    Render();
                }
                if (Perf)
                {
                    CountFrame(changed, t0, t1, Stopwatch.GetTimestamp());
                }
            }
            catch (Exception e) when (e is SharpGen.Runtime.SharpGenException or COMException)
            {
                Log.Write($"graphics device lost, recreating: {e.Message}");
                RecreateDevice();
                continue;
            }
            if (!changed)
            {
                // Nothing moved: sleep until the mouse moves (the hook wakes us) or a short timeout.
                uint timeout = settling || held ? 8u : 100u;
                Win32.MsgWaitForMultipleObjectsEx(1, handles, timeout, Win32.QS_ALLINPUT, Win32.MWMO_INPUTAVAILABLE);
            }
        }
        DisposeDevice();
        Win32.DestroyWindow(hwnd);
    }

    // TH_TEST_RECREATE=1 forces one graphics rebuild 2 s after turning on (to test recovery).
    private static readonly bool TestRecreate = Environment.GetEnvironmentVariable("TH_TEST_RECREATE") == "1";
    private bool testRecreated;
    private double activatedAt;

    // TH_PERF=1 logs per-second frame stats.
    private static readonly bool Perf = Environment.GetEnvironmentVariable("TH_PERF") == "1";
    private int perfTicks, perfFrames;
    private double perfTickMs, perfRenderMs, perfDrawMs, perfSince, perfCpu;

    private void CountFrame(bool rendered, long t0, long t1, long t2)
    {
        perfTicks++;
        perfTickMs += (t1 - t0) * 1000.0 / Stopwatch.Frequency;
        if (rendered)
        {
            perfFrames++;
            perfRenderMs += (t2 - t1) * 1000.0 / Stopwatch.Frequency;
            perfDrawMs += lastDrawMs;
        }
        double now = clock.Elapsed.TotalSeconds;
        if (now - perfSince >= 1)
        {
            var cpu = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
            Log.Write($"perf: ticks={perfTicks} frames={perfFrames} tick={perfTickMs / Math.Max(1, perfTicks):F2}ms " +
                      $"draw={perfDrawMs / Math.Max(1, perfFrames):F2}ms draw+present={perfRenderMs / Math.Max(1, perfFrames):F2}ms " +
                      $"processCpu={cpu - perfCpu:F0}ms/s settling={settling} press={pressAmount:F3} ink={ink.Count}");
            perfTicks = perfFrames = 0;
            perfTickMs = perfRenderMs = perfDrawMs = 0;
            perfSince = now;
            perfCpu = cpu;
        }
    }

    private void Pump()
    {
        while (Win32.PeekMessage(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
    }

    private IntPtr WindowProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_NCHITTEST:
                return new IntPtr(Win32.HTTRANSPARENT);
            case Win32.WM_MOUSEACTIVATE:
                return new IntPtr(Win32.MA_NOACTIVATE);
            case Win32.WM_DISPLAYCHANGE:
                monitor = default; // re-read on the next tick
                break;
        }
        return Win32.DefWindowProc(h, msg, wParam, lParam);
    }

    private void CreateWindow()
    {
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = WindowClass,
        };
        Win32.RegisterClassEx(ref wc);
        uint ex = Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED
                  | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_NOREDIRECTIONBITMAP;
        hwnd = Win32.CreateWindowEx(ex, WindowClass, "TinyHand Overlay", Win32.WS_POPUP,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"overlay window: error {Marshal.GetLastWin32Error()}");
        }
        // Layered + transparent makes the window click-through for every app; alpha 255 keeps
        // the composition content fully visible.
        Win32.SetLayeredWindowAttributes(hwnd, 0, 255, Win32.LWA_ALPHA);
    }

    private void CreateDevice()
    {
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        var result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out d3d);
        if (result.Failure || d3d == null)
        {
            Log.Write("no hardware Direct3D device; using WARP");
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out d3d).CheckError();
        }
        dxgi = d3d!.QueryInterface<IDXGIDevice1>();
        // One frame in flight: Present blocks until vsync, keeping the hand glued to the mouse.
        dxgi.SetMaximumFrameLatency(1);

        factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        d2dDevice = factory.CreateDevice(dxgi);
        ctx = d2dDevice.CreateDeviceContext(DeviceContextOptions.None).QueryInterface<ID2D1DeviceContext1>();
        ctx.UnitMode = UnitMode.Pixels;

        dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
        dcomp.CreateTargetForHwnd(hwnd, true, out dcompTarget).CheckError();
        visual = dcomp.CreateVisual();
        dcompTarget!.SetRoot(visual);

        brush = ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        roundStroke = factory.CreateStrokeStyle(new StrokeStyleProperties1
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
            MiterLimit = 10,
        });
        shadow = new Shadow(ctx) { Optimization = ShadowOptimization.Speed };
        swapChain = null;
        target = null;
        handBitmap = null;
        handBitmapName = null;
    }

    private void DisposeDevice()
    {
        ink.ReleaseDeviceResources();
        handBitmap?.Dispose();
        handBitmap = null;
        displayBitmap?.Dispose();
        displayBitmap = null;
        handBitmapName = null;
        shadow?.Dispose();
        roundStroke?.Dispose();
        brush?.Dispose();
        target?.Dispose();
        target = null;
        swapChain?.Dispose();
        swapChain = null;
        visual?.Dispose();
        dcompTarget?.Dispose();
        dcomp?.Dispose();
        ctx?.Dispose();
        ctx = null;
        d2dDevice?.Dispose();
        factory?.Dispose();
        dxgi?.Dispose();
        d3d?.Dispose();
    }

    private void RecreateDevice()
    {
        DisposeDevice();
        try
        {
            CreateDevice();
            var m = monitor;
            monitor = default;
            if (m.Handle != IntPtr.Zero)
            {
                SwitchMonitor(m.Handle);
            }
        }
        catch (Exception e)
        {
            Log.Write($"overlay device recreate failed: {e}");
            Thread.Sleep(500);
        }
    }

    private void SetActiveNow(bool value)
    {
        if (value == active)
        {
            return;
        }
        active = value;
        if (value)
        {
            if (ctx == null)
            {
                try
                {
                    // Created on first use so an idle TinyHand holds no GPU resources.
                    CreateDevice();
                }
                catch (Exception e)
                {
                    Log.Write($"graphics device failed: {e}");
                    active = false;
                    return;
                }
            }
            EnsureAsset();
            Win32.timeBeginPeriod(1);
            gripX = null;
            lastPose = Array.Empty<double>();
            needsFrame = true;
            monitor = default;
            lastTick = clock.Elapsed.TotalSeconds;
            activatedAt = lastTick;
            try
            {
                Tick();
                Render();
            }
            catch (Exception e)
            {
                // Never stay on with an invisible hand: rebuild the graphics and try once more.
                Log.Write($"first frame failed, rebuilding graphics: {e.Message}");
                RecreateDevice();
                try
                {
                    monitor = default;
                    Tick();
                    Render();
                }
                catch (Exception again)
                {
                    Log.Write($"first frame failed again: {again}");
                }
            }
            // Show the window whatever happened above; the render loop keeps retrying frames.
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }
        else
        {
            Win32.timeEndPeriod(1);
            Release();
            ink.Clear();
            drawing = false;
            held = false;
            pressAmount = 0;
            pointerHidden = false;
            // Blank frame so the next activation doesn't flash the old pose.
            if (target != null && ctx != null && swapChain != null)
            {
                try
                {
                    ctx.Target = target;
                    ctx.BeginDraw();
                    ctx.Clear(new Color4(0, 0, 0, 0));
                    ctx.EndDraw();
                    ctx.Target = null;
                    swapChain.Present(0, PresentFlags.None);
                }
                catch (Exception e)
                {
                    Log.Write($"clearing the overlay failed: {e.Message}");
                }
            }
            Win32.ShowWindow(hwnd, Win32.SW_HIDE);
        }
    }

    private Vector2 Local(POINT p) => new(p.X - monitor.Bounds.Left, p.Y - monitor.Bounds.Top);

    private void Release()
    {
        held = false;
        downPoint = null;
        ink.End(clock.Elapsed.TotalSeconds);
        drawing = false;
        needsFrame = true;
    }

    private void UpdateInkColor()
    {
        var c = Preferences.InkChoice.Resolved(style);
        ink.Color = new Color4(c.R / 255f, c.G / 255f, c.B / 255f, 1);
    }

    private void SwitchMonitor(IntPtr handle)
    {
        var info = Win32.MonitorInfo(handle);
        var next = new MonitorState(handle, info.rcMonitor, info.rcWork, Win32.MonitorDpi(handle));
        bool moving = monitor.Handle != IntPtr.Zero;
        monitor = next;
        if (moving)
        {
            Release();
        }
        ink.Clear();
        drawing = false;
        gripX = null;

        var b = next.Bounds;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Win32.SWP_NOACTIVATE);
        ResizeSwapChain((uint)b.Width, (uint)b.Height);
        needsFrame = true;
        MonitorChanged?.Invoke(next);
    }

    private uint swapWidth;
    private uint swapHeight;

    private void ResizeSwapChain(uint width, uint height)
    {
        if (ctx == null || dxgi == null || visual == null || dcomp == null)
        {
            return;
        }
        // Same size (every activation on the same monitor): nothing to do. Resizing anyway failed
        // with DXGI_ERROR_INVALID_CALL while a screen recorder held the buffers, which left the
        // overlay with nothing to draw on.
        if (swapChain != null && target != null && width == swapWidth && height == swapHeight)
        {
            return;
        }
        ctx.Target = null;
        target?.Dispose();
        target = null;
        if (swapChain != null)
        {
            var resized = swapChain.ResizeBuffers(2, width, height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            if (resized.Failure)
            {
                // The buffers are still in use (e.g. by a screen capture): start a fresh swap chain.
                Log.Write($"swap chain resize failed (0x{resized.Code:X8}); making a new one");
                swapChain.Dispose();
                swapChain = null;
            }
        }
        if (swapChain == null)
        {
            using var adapter = dxgi.GetAdapter();
            using var dxgiFactory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1
            {
                Width = width,
                Height = height,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied,
            };
            swapChain = dxgiFactory.CreateSwapChainForComposition(d3d, desc, null);
            visual.SetContent(swapChain);
            dcomp.Commit();
        }
        swapWidth = width;
        swapHeight = height;
        using var surface = swapChain.GetBuffer<IDXGISurface>(0);
        target = ctx.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw));
    }

    /// <summary>Advance animation and work out whether a new frame is needed.</summary>
    private bool Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - lastTick, 0, 0.1);
        lastTick = now;

        var cursor = Win32.CursorPos();
        var handle = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        if (handle != monitor.Handle)
        {
            SwitchMonitor(handle);
        }
        if (now - lastTopmost > 1)
        {
            // Other topmost windows (taskbar, popups) can rise above us; stay on top.
            lastTopmost = now;
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            // Health checks: the hand must never silently vanish while the pointer is on.
            bool forceRebuild = TestRecreate && !testRecreated && now - activatedAt > 2;
            if (forceRebuild)
            {
                testRecreated = true;
                Log.Write("TH_TEST_RECREATE: forcing a graphics rebuild");
            }
            bool invalid = !forceRebuild && dcomp != null && !dcomp.CheckDeviceState();
            if (invalid)
            {
                Log.Write("composition device became invalid; rebuilding graphics");
            }
            if (forceRebuild || invalid)
            {
                RecreateDevice();
                needsFrame = true;
            }
            // (Skipped during the first frame, which is drawn before the window is shown.)
            if (now - activatedAt > 1 && !Win32.IsWindowVisible(hwnd))
            {
                Log.Write("overlay window was hidden while on; showing it again");
                Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
            }
        }

        EnsureAsset();
        var local = Local(cursor);
        bool frame = needsFrame;
        needsFrame = false;

        if (downPoint is { } start)
        {
            float scale = monitor.Scale;
            if (!ink.IsDrawing)
            {
                if (Vector2.Distance(local, start) >= 4 * scale)
                {
                    ink.Begin(start, InkWidth);
                    drawing = true;
                }
            }
            if (ink.IsDrawing)
            {
                ink.Extend(local, 1.5f * scale);
            }
        }
        if (ink.NeedsRedraw(now) || ink.Cleared)
        {
            ink.Cleared = false;
            frame = true;
        }

        var pose = Aim(local, dt);
        if (!PoseEquals(pose, lastPose))
        {
            lastPose = pose;
            frame = true;
        }
        settling = pressAmount > 0.002 || (gripX is { } gx && Math.Abs(gripTarget - gx) > 0.5);
        return frame;
    }

    private float InkWidth => (float)Math.Max(3 * monitor.Scale, monitor.Bounds.Height * 0.0055);

    private static bool PoseEquals(double[] a, double[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > 0.01)
            {
                return false;
            }
        }
        return true;
    }

    // Pose computed by Aim and used by Render.
    private double poseRotation;
    private double poseRodLen;
    private double poseHandPts;
    private double poseWidthPts;
    private Vector2 poseGrip;
    private double poseLift;
    private Vector2 poseCursor;

    /// <summary>
    /// The same geometry as macOS ArmView.aim, done in a y-up frame (origin at the monitor's
    /// bottom-left) so the maths matches the original, then mapped to y-down pixels in Render.
    /// </summary>
    private double[] Aim(Vector2 cursorDown, double dt)
    {
        double w = monitor.Bounds.Width;
        double h = monitor.Bounds.Height;
        var cursor = new Vector2(cursorDown.X, (float)(h - cursorDown.Y));
        poseCursor = cursorDown;

        double follow = 1 - Math.Pow(1 - GripFollow, dt * ReferenceRate);
        double targetX = Math.Min(Math.Max(cursor.X + GripLeanFrac * w, w * 0.15), w * 1.05);
        double gx = gripX is { } prev ? prev + (targetX - prev) * follow : targetX;
        gripX = gx;
        gripTarget = targetX;
        var grip = new Vector2((float)gx, (float)(-GripDepthFrac * h));

        if (!held)
        {
            double decay = 1 - Math.Pow(1 - 0.18, dt * ReferenceRate);
            pressAmount += (0 - pressAmount) * decay;
            if (pressAmount < 0.0005)
            {
                pressAmount = 0;
            }
        }

        if (style.IsLaser || asset == null)
        {
            double r = LaserRadius();
            return new[] { 1, r, cursorDown.X, cursorDown.Y, pointerHidden ? 1 : 0 };
        }
        var a = asset;
        double dx = cursor.X - grip.X;
        double dy = cursor.Y - grip.Y;
        double reach = Math.Sqrt(dx * dx + dy * dy);
        double aimAngle = Math.Atan2(dy, dx);

        double handPts = handLenFrac * h * (1 - 0.06 * pressAmount);
        double s = handPts / (a.Wrist - a.HandTop);
        double widthPts = s * a.Aspect;
        double handTipPts = (a.Wrist - a.Tip) * s;
        double tipOffX = (a.TipX - a.RodX) * widthPts;

        double along = Math.Sqrt(Math.Max(0, reach * reach - tipOffX * tipOffX));
        double rodLen = Math.Max(MinRodFrac * h, along - handTipPts);
        double tipAngle = Math.Atan2(rodLen + handTipPts, tipOffX);
        double rotation = aimAngle - tipAngle;
        double lift = LiftFrac * h * (1 - 0.8 * pressAmount);

        poseRotation = rotation;
        poseRodLen = rodLen;
        poseHandPts = handPts;
        poseWidthPts = widthPts;
        poseGrip = grip;
        poseLift = lift;
        return new[] { 0, rotation * 100, rodLen, handPts, widthPts, grip.X, lift, pressAmount * 100, showShadow ? 1 : 0, a.Aspect * 100, pointerHidden ? 1 : 0 };
    }

    private double LaserRadius() =>
        Math.Max(4 * monitor.Scale, monitor.Bounds.Height * 0.0038 * handLenFrac / 0.13) * (1 + 0.2 * pressAmount);

    private void Render()
    {
        if (ctx != null && target == null && monitor.Handle != IntPtr.Zero)
        {
            // Lost the surface somehow: get a new one rather than silently drawing nothing.
            ResizeSwapChain((uint)monitor.Bounds.Width, (uint)monitor.Bounds.Height);
        }
        if (ctx == null || target == null || swapChain == null || brush == null || roundStroke == null || factory == null)
        {
            return;
        }
        double now = clock.Elapsed.TotalSeconds;
        long drawStart = Stopwatch.GetTimestamp();

        // The hand goes into a command list first so the drop shadow can be made from it.
        ID2D1CommandList? pointer = null;
        if (!pointerHidden)
        {
            var handImage = style.IsLaser || asset == null ? null : PrepareHand(asset);
            pointer = ctx.CreateCommandList();
            ctx.Target = pointer;
            ctx.BeginDraw();
            try
            {
                if (handImage == null)
                {
                    DrawLaserDot();
                }
                else
                {
                    DrawHand(asset!, handImage);
                }
            }
            finally
            {
                ctx.EndDraw();
                ctx.Target = null;
                pointer.Close();
            }
        }

        SharpGen.Runtime.Result end = default;
        ctx.Target = target;
        ctx.BeginDraw();
        try
        {
            ctx.Clear(new Color4(0, 0, 0, 0));
            ctx.Transform = Matrix3x2.Identity;
            ink.Draw(ctx, factory, roundStroke, brush, monitor.Scale, now);

            if (pointer != null && shadow != null)
            {
                if (style.IsLaser || asset == null)
                {
                    double r = LaserRadius();
                    shadow.SetInput(0, pointer, true);
                    shadow.BlurStandardDeviation = (float)(r * (2.2 + 1.0 * pressAmount) / 2);
                    shadow.Color = new Vector4(1f, 0.16f, 0.12f, 1f);
                    ctx.DrawImage(shadow, InterpolationMode.Linear, Vortice.Direct2D1.CompositeMode.SourceOver);
                }
                else if (showShadow)
                {
                    // macOS: shadowOffset (lift*0.6, -lift) in a y-up world = right and down on screen.
                    float radius = (float)(Math.Max(1, poseLift * 0.35) + 5 * pressAmount);
                    shadow.SetInput(0, pointer, true);
                    shadow.BlurStandardDeviation = radius / 2;
                    shadow.Color = new Vector4(0, 0, 0, (float)(0.26 + 0.02 * pressAmount));
                    var offset = new Vector2((float)(poseLift * 0.6), (float)poseLift);
                    ctx.DrawImage(shadow, offset, InterpolationMode.Linear, Vortice.Direct2D1.CompositeMode.SourceOver);
                }
                ctx.DrawImage(pointer, InterpolationMode.Linear, Vortice.Direct2D1.CompositeMode.SourceOver);
            }
        }
        finally
        {
            // Always close the pass and let go of the back buffer, even if drawing failed;
            // an open pass keeps the buffer referenced and every later resize fails.
            end = ctx.EndDraw();
            ctx.Target = null;
            shadow?.SetInput(0, null, false);
            pointer?.Dispose();
        }
        if (end.Failure)
        {
            Log.Write($"drawing failed (0x{end.Code:X8}); rebuilding graphics");
            RecreateDevice();
            return;
        }
        long drawn = Stopwatch.GetTimestamp();
        var presented = swapChain.Present(1, PresentFlags.None);
        if (presented.Failure)
        {
            Log.Write($"present failed (0x{presented.Code:X8}); rebuilding graphics");
            RecreateDevice();
            return;
        }
        lastDrawMs = (drawn - drawStart) * 1000.0 / Stopwatch.Frequency;
    }

    private double lastDrawMs;

    private void DrawLaserDot()
    {
        double r = LaserRadius();
        double core = r * 0.45;
        var c = poseCursor;
        brush!.Color = new Color4(1f, 0.16f, 0.12f, 1f);
        ctx!.FillEllipse(new Ellipse(c, (float)r, (float)r), brush);
        brush.Color = new Color4(1f, 0.86f, 0.82f, 0.95f);
        ctx.FillEllipse(new Ellipse(c, (float)core, (float)core), brush);
    }

    private ID2D1Bitmap1? displayBitmap;
    private int displayHeight;

    /// <summary>
    /// Like macOS ArmView.updateDisplayImage: keep a copy scaled (high quality, once) to about the
    /// size it's drawn at, in 64 px steps, so each frame only does a cheap linear resample.
    /// </summary>
    private ID2D1Bitmap1 DisplayBitmap(HandAsset a)
    {
        double restPts = handLenFrac * monitor.Bounds.Height / (a.Wrist - a.HandTop);
        const int step = 64;
        int needed = ((int)Math.Ceiling(restPts) + step - 1) / step * step;
        int height = needed >= a.Height * 9 / 10 ? a.Height : Math.Max(step, needed);
        if (height == a.Height)
        {
            return handBitmap!;
        }
        if (displayBitmap != null && displayHeight == height)
        {
            return displayBitmap;
        }
        displayBitmap?.Dispose();
        int width = Math.Max(1, (int)Math.Round((double)a.Width * height / a.Height));
        displayBitmap = ctx!.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
                BitmapOptions.Target));
        var previous = ctx.Target;
        ctx.Target = displayBitmap;
        ctx.BeginDraw();
        ctx.Clear(new Color4(0, 0, 0, 0));
        ctx.Transform = Matrix3x2.Identity;
        ctx.DrawBitmap(handBitmap!, new RawRectF(0, 0, width, height), 1, InterpolationMode.HighQualityCubic, null, null);
        ctx.EndDraw();
        ctx.Target = previous;
        displayHeight = height;
        return displayBitmap;
    }

    /// <summary>Upload the hand (when the style changed) and return the bitmap to draw this frame.</summary>
    private ID2D1Bitmap1 PrepareHand(HandAsset a)
    {
        var name = style.AssetName!;
        if (handBitmap == null || handBitmapName != name)
        {
            handBitmap?.Dispose();
            displayBitmap?.Dispose();
            displayBitmap = null;
            handBitmap = UploadBitmap(a);
            handBitmapName = name;
        }
        return DisplayBitmap(a);
    }

    private void DrawHand(HandAsset a, ID2D1Bitmap1 bitmap)
    {

        double h = monitor.Bounds.Height;
        double width = poseWidthPts;
        double total = poseRodLen + poseHandPts;
        double anchorX = a.RodX * width;
        // y-up rotation θ about the grip becomes a rotation by -θ in y-down pixels.
        var gripDown = new Vector2(poseGrip.X, (float)(h - poseGrip.Y));
        ctx!.Transform = Matrix3x2.CreateTranslation((float)-anchorX, (float)-total)
                         * Matrix3x2.CreateRotation((float)-poseRotation)
                         * Matrix3x2.CreateTranslation(gripDown);

        var size = bitmap.PixelSize;
        float iw = size.Width;
        float ih = size.Height;
        var handDest = new RawRectF(0, 0, (float)width, (float)poseHandPts);
        var handSrc = new RawRectF(0, (float)(a.HandTop * ih), iw, (float)(a.Wrist * ih));
        var rodDest = new RawRectF(0, (float)poseHandPts, (float)width, (float)total);
        var rodSrc = new RawRectF(0, (float)(a.Wrist * ih), iw, (float)(a.RodEnd * ih));
        // Already near display size, so linear sampling looks the same as cubic and costs far less.
        var mode = bitmap == handBitmap ? InterpolationMode.HighQualityCubic : InterpolationMode.Linear;
        ctx.DrawBitmap(bitmap, rodDest, 1, mode, rodSrc, null);
        ctx.DrawBitmap(bitmap, handDest, 1, mode, handSrc, null);
        ctx.Transform = Matrix3x2.Identity;
    }

    private unsafe ID2D1Bitmap1 UploadBitmap(HandAsset a)
    {
        var pixels = a.Pixels ?? a.ReloadPixels() ?? new byte[a.Width * a.Height * 4];
        ID2D1Bitmap1 bitmap;
        fixed (byte* p = pixels)
        {
            bitmap = ctx!.CreateBitmap(new SizeI(a.Width, a.Height), (IntPtr)p, (uint)(a.Width * 4),
                new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
        }
        // The GPU has its own copy now; keep only the measurements.
        a.Pixels = null;
        return bitmap;
    }

    public void Dispose()
    {
        quit = true;
        wake.Set();
        thread.Join(1000);
    }
}
