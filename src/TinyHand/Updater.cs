using System.Windows;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace TinyHand;

/// <summary>Self-updates from GitHub Releases with Velopack (the Windows stand-in for Sparkle).</summary>
internal static class Updater
{
    public const string RepoUrl = "https://github.com/OmiChrome/tinyhand-win";

    /// <summary>
    /// Update feed (releases.win.json + package), published to GitHub Pages by the release workflow
    /// so the GitHub releases themselves only carry the installer and the portable zip.
    /// </summary>
    public const string FeedUrl = "https://omichrome.github.io/tinyhand-win/updates";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private static UpdateManager? manager;
    private static DispatcherTimer? timer;
    private static UpdateInfo? ready;
    private static bool busy;

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

    public static async void CheckNow(bool interactive)
    {
        var mgr = Manager;
        if (mgr == null || !mgr.IsInstalled || busy)
        {
            return;
        }
        if (ready != null)
        {
            OfferRestart(ready);
            return;
        }
        busy = true;
        try
        {
            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                if (interactive)
                {
                    MessageBox.Show($"TinyHand {AppController.Version} is the latest version.", "TinyHand",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }
            Log.Write($"update {info.TargetFullRelease.Version} found; downloading");
            await mgr.DownloadUpdatesAsync(info);
            ready = info;
            OfferRestart(info);
        }
        catch (Exception e)
        {
            Log.Write($"update check failed: {e.Message}");
            if (interactive)
            {
                MessageBox.Show("TinyHand couldn't check for updates. Check your connection and try again.", "TinyHand",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            busy = false;
        }
    }

    private static void OfferRestart(UpdateInfo info)
    {
        var answer = MessageBox.Show(
            $"TinyHand {info.TargetFullRelease.Version} is ready to install.\n\nRestart TinyHand now to update?",
            "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes)
        {
            CursorHider.Restore();
            Manager!.ApplyUpdatesAndRestart(info);
        }
        else
        {
            // Installs quietly when TinyHand quits.
            Manager!.WaitExitThenApplyUpdates(info, silent: true, restart: false);
        }
    }
}
