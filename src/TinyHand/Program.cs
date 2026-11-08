using System.Windows;
using System.Windows.Threading;
using TinyHand.Native;

namespace TinyHand;

internal static class Program
{
    private const string MutexName = @"Local\TinyHand.Windows.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        // The watchdog is a second copy of the exe that only restores the cursor if we die.
        int w = Array.IndexOf(args, "--cursor-watchdog");
        if (w >= 0 && w + 1 < args.Length && int.TryParse(args[w + 1], out int parent))
        {
            CursorHider.RunWatchdog(parent);
            return 0;
        }

        if (Updater.HandleInstallerHooks(args))
        {
            return 0;
        }

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask that copy to open its settings.
            var other = Win32.FindWindowEx(IntPtr.Zero, IntPtr.Zero, MessageWindow.ClassName, null);
            if (other != IntPtr.Zero)
            {
                // We were just launched by the user, so we may take focus: pass that on.
                Win32.GetWindowThreadProcessId(other, out uint pid);
                Win32.AllowSetForegroundWindow(pid);
                Win32.PostMessage(other, AppController.ShowSettingsMessage, new IntPtr(SettingsPageArg(args)), IntPtr.Zero);
            }
            return 0;
        }

        CursorHider.RestoreIfStale();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write($"crash: {e.ExceptionObject}");
            CursorHider.Restore();
        };

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Fluent controls, but always dark and in TinyHand's own colours (not the user's accent).
        app.ThemeMode = ThemeMode.Dark;
        UI.Theme.Apply(app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"UI error: {e.Exception}");
            e.Handled = true;
        };
        AppController? controller = null;
        app.Startup += (_, _) =>
        {
            controller = new AppController();
            controller.Start();
        };
        app.SessionEnding += (_, _) => CursorHider.Restore();
        app.Exit += (_, _) => controller?.Dispose();
        app.Run();
        return 0;
    }

    /// <summary>"--settings pointers" opens that page: 0 = default, otherwise section index + 1.</summary>
    public static int SettingsPageArg(string[] args)
    {
        int i = Array.IndexOf(args, "--settings");
        if (i >= 0 && i + 1 < args.Length && Enum.TryParse<UI.SettingsSection>(args[i + 1], true, out var section))
        {
            return (int)section + 1;
        }
        return 0;
    }
}
