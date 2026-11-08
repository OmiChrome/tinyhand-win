using System.Diagnostics;
using TinyHand.Native;

namespace TinyHand;

/// <summary>
/// Hides the mouse cursor everywhere by swapping every system cursor for a blank one, and restores
/// the user's cursor scheme afterwards. A marker file and a watchdog process make sure the cursor
/// comes back even if TinyHand crashes or is killed.
/// </summary>
internal static class CursorHider
{
    private static readonly string Marker = Path.Combine(Paths.LocalData, "cursor-hidden");
    private static bool hidden;
    private static Process? watchdog;

    public static bool IsHidden => hidden;

    public static void Hide()
    {
        if (hidden)
        {
            return;
        }
        hidden = true;
        EnsureWatchdog();
        try
        {
            File.WriteAllText(Marker, Environment.ProcessId.ToString());
        }
        catch
        {
            // The watchdog still restores on exit.
        }
        foreach (var id in Win32.SystemCursorIds)
        {
            // SetSystemCursor takes ownership of (and destroys) the handle, so each id needs its own.
            Win32.SetSystemCursor(BlankCursor(), id);
        }
    }

    public static void Show()
    {
        if (!hidden)
        {
            return;
        }
        hidden = false;
        Restore();
    }

    /// <summary>Reload the user's cursors from the registry (undoes every SetSystemCursor).</summary>
    public static void Restore()
    {
        Win32.SystemParametersInfo(Win32.SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        try
        {
            File.Delete(Marker);
        }
        catch
        {
            // Nothing to clean up.
        }
    }

    /// <summary>On launch: if a previous run died with the cursor hidden, bring it back.</summary>
    public static void RestoreIfStale()
    {
        if (File.Exists(Marker))
        {
            Log.Write("cursor was left hidden by a previous run; restoring");
            Restore();
        }
    }

    private static IntPtr BlankCursor()
    {
        var and = Enumerable.Repeat((byte)0xFF, 32 * 4).ToArray();
        var xor = new byte[32 * 4];
        return Win32.CreateCursor(Win32.GetModuleHandle(null), 0, 0, 32, 32, and, xor);
    }

    private static void EnsureWatchdog()
    {
        if (watchdog is { HasExited: false })
        {
            return;
        }
        try
        {
            watchdog = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--cursor-watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception e)
        {
            Log.Write($"cursor watchdog failed to start: {e.Message}");
        }
    }

    /// <summary>Entry point for the watchdog process: wait for TinyHand to exit, then restore cursors.</summary>
    public static void RunWatchdog(int parentPid)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            parent.WaitForExit();
        }
        catch
        {
            // Parent already gone.
        }
        if (File.Exists(Marker))
        {
            Restore();
        }
    }
}
