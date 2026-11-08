using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace TinyHand;

/// <summary>Self-updates with Velopack (the Windows stand-in for Sparkle).</summary>
internal static class Updater
{
    public const string RepoUrl = "https://github.com/OmiChrome/tinyhand-win";

    /// <summary>
    /// Update feed (releases.win.json + packages), published to GitHub Pages by the release workflow
    /// so the GitHub releases themselves only carry the installer and the portable zip.
    /// </summary>
    public const string DefaultFeedUrl = "https://omichrome.github.io/tinyhand-win/updates";

    /// <summary>TH_UPDATE_FEED overrides the feed (for testing updates against a local folder).</summary>
    public static string FeedUrl =>
        Environment.GetEnvironmentVariable("TH_UPDATE_FEED") is { Length: > 0 } custom ? custom : DefaultFeedUrl;

    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private static UpdateManager? manager;
    private static DispatcherTimer? timer;
    private static UpdateInfo? ready;
    private static string? downloading;
    private static int percent;
    private static bool busy;
    private static UI.UpdateWindow? window;

    /// <summary>Velopack install/uninstall hooks. Returns true when the process should exit.</summary>
    public static bool HandleInstallerHooks(string[] args)
    {
        try
        {
            VelopackApp.Build()
                .OnAfterInstallFastCallback(_ => LaunchAtLogin.Enabled = true)
                .OnBeforeUninstallFastCallback(_ =>
                {
                    LaunchAtLogin.Enabled = false;
                    CursorHider.Restore();
                })
                .Run();
        }
        catch (Exception e)
        {
            Log.Write($"velopack hooks failed: {e.Message}");
        }
        return false;
    }

    private static UpdateManager? Manager
    {
        get
        {
            if (manager == null)
            {
                try
                {
                    manager = new UpdateManager(new SimpleWebSource(FeedUrl));
                }
                catch (Exception e)
                {
                    Log.Write($"update manager unavailable: {e.Message}");
                }
            }
            return manager;
        }
    }

    /// <summary>True when running from a Velopack install (not from a dev build folder).</summary>
    public static bool IsInstalled => Manager?.IsInstalled == true;

    public static bool IsAvailable => IsInstalled;

    public static void Start()
    {
        if (!IsAvailable)
        {
            return;
        }
        timer = new DispatcherTimer { Interval = CheckEvery };
        timer.Tick += (_, _) =>
        {
            if (Preferences.AutoUpdate)
            {
                CheckNow(interactive: false);
            }
        };
        timer.Start();
        if (Preferences.AutoUpdate)
        {
            // Give the app a moment to settle after launch.
            var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            first.Tick += (_, _) =>
            {
                first.Stop();
                CheckNow(interactive: false);
            };
            first.Start();
        }
    }

    private static UI.UpdateWindow Window
    {
        get
        {
            if (window == null)
            {
                window = new UI.UpdateWindow();
                window.RestartClicked += Restart;
            }
            return window;
        }
    }

    /// <summary>
    /// Look for an update. Interactive checks (from the menu or Settings) show progress straight away;
    /// automatic ones stay silent until an update is downloaded and ready.
    /// </summary>
    public static async void CheckNow(bool interactive)
    {
        var mgr = Manager;
        if (mgr == null || !mgr.IsInstalled)
        {
            return;
        }
        if (ready != null)
        {
            Window.ShowReady(ready.TargetFullRelease.Version.ToString());
            Window.ShowOnTop();
            return;
        }
        if (busy)
        {
            // Already checking or downloading (maybe automatically): show where it's at.
            if (interactive)
            {
                if (downloading != null)
                {
                    Window.ShowDownloading(downloading, percent);
                }
                else
                {
                    Window.ShowChecking();
                }
                Window.ShowOnTop();
            }
            return;
        }
        busy = true;
        if (interactive)
        {
            Window.ShowChecking();
            Window.ShowOnTop();
        }
        try
        {
            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                Log.Write("update check: up to date");
                if (interactive)
                {
                    Window.ShowMessage("You're up to date", $"TinyHand {AppController.Version} is the latest version.");
                    Window.ShowOnTop();
                }
                return;
            }
            var version = info.TargetFullRelease.Version.ToString();
            bool delta = info.DeltasToTarget is { Length: > 0 };
            Log.Write($"update {version} found; downloading {(delta ? $"{info.DeltasToTarget.Length} delta(s)" : "full package")}");
            downloading = version;
            percent = 0;
            if (interactive)
            {
                Window.ShowDownloading(version, 0);
            }
            var dispatcher = Dispatcher.CurrentDispatcher;
            await mgr.DownloadUpdatesAsync(info, p => dispatcher.BeginInvoke(() =>
            {
                percent = p;
                if (Window.IsVisible)
                {
                    Window.ShowDownloading(version, p);
                }
            }));
            Log.Write($"update {version} downloaded");
            ready = info;
            downloading = null;
            Window.ShowReady(version);
            Window.ShowOnTop();
        }
        catch (Exception e)
        {
            Log.Write($"update failed: {e}");
            downloading = null;
            if (interactive || Window.IsVisible)
            {
                Window.ShowMessage("Couldn't update", "TinyHand couldn't download the update. Check your connection and try again.");
                Window.ShowOnTop();
            }
        }
        finally
        {
            busy = false;
        }
    }

    private static void Restart()
    {
        if (ready == null || Manager == null)
        {
            return;
        }
        Log.Write($"restarting into {ready.TargetFullRelease.Version}");
        CursorHider.Restore();
        Manager.ApplyUpdatesAndRestart(ready);
    }

    /// <summary>When quitting with a downloaded update that wasn't applied, install it quietly.</summary>
    public static void ApplyOnExit()
    {
        if (ready == null || Manager == null)
        {
            return;
        }
        try
        {
            Manager.WaitExitThenApplyUpdates(ready, silent: true, restart: false);
        }
        catch (Exception e)
        {
            Log.Write($"apply on exit failed: {e.Message}");
        }
    }
}
