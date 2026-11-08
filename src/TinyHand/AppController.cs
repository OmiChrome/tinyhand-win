using System.Windows;
using TinyHand.Native;

namespace TinyHand;

/// <summary>The app: tray icon, menu, global shortcuts and the windows. UI thread only.</summary>
internal sealed class AppController : IDisposable
{
    private readonly MessageWindow messages = new();
    private readonly HotKeys hotKeys;
    private readonly TrayIcon tray;
    private readonly OverlayController overlay;
    private UI.SettingsWindow? settings;
    private UI.WelcomeWindow? welcome;
    private int? toggleHotKey;
    private int? clearHotKey;

    public static uint ShowSettingsMessage { get; } = Win32.RegisterWindowMessage("TinyHand.ShowSettings");

    public AppController()
    {
        hotKeys = new HotKeys(messages);
        tray = new TrayIcon(messages);
        overlay = new OverlayController(hotKeys);
    }

    public void Start()
    {
        Win32.AllowDarkMenus();
        HandAsset.PreloadThumbnails(PointerStyle.All.Select(s => s.AssetName).OfType<string>());
        Log.Write($"launch {Environment.ProcessPath} v{Version}");

        overlay.ActiveChanged += UpdateIcon;
        overlay.ExtraPassThrough = () => tray.Bounds.IsEmpty ? Array.Empty<RECT>() : new[] { tray.Bounds };
        tray.Clicked += overlay.Toggle;
        tray.BuildMenu = BuildMenu;
        tray.MenuOpenChanged += open => overlay.SuspendInput(open);
        UpdateIcon();

        // A second copy was launched: show settings instead.
        messages.On(ShowSettingsMessage, (_, _) =>
        {
            OpenSettings();
            return IntPtr.Zero;
        });

        EnableLaunchAtLoginOnFirstRun();
        LaunchAtLogin.RefreshPath();
        RegisterHotKeys();
        Updater.Start();

        if (!Preferences.WelcomeShown)
        {
            ShowWelcome();
        }
    }

    public static string Version =>
        typeof(AppController).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    private IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        var items = new List<TrayMenuItem>
        {
            new($"{(overlay.IsActive ? "Turn Pointer Off" : "Turn Pointer On")}\t{Preferences.ToggleShortcut.Display}", overlay.Toggle),
            new($"Clear Drawing\t{Preferences.ClearShortcut.Display}", overlay.ClearInk),
            new("Tap Sound", () => overlay.SoundOn = !overlay.SoundOn, Checked: overlay.SoundOn),
            TrayMenuItem.Separator,
            new("Settings…", OpenSettings),
        };
        if (Updater.IsAvailable)
        {
            items.Add(new("Check for Updates…", () =>
            {
                overlay.Exit();
                Updater.CheckNow(interactive: true);
            }));
        }
        items.Add(TrayMenuItem.Separator);
        items.Add(new("Quit TinyHand", Quit));
        return items;
    }

    private void UpdateIcon()
    {
        tray.Update(overlay.IsActive, overlay.IsActive
            ? "TinyHand is on — click to turn off, right-click for the menu"
            : "TinyHand — click to turn on, right-click for the menu");
    }

    public void OpenSettings()
    {
        overlay.Exit();
        settings ??= CreateSettings();
        settings.ShowAndActivate();
    }

    private UI.SettingsWindow CreateSettings()
    {
        var w = new UI.SettingsWindow
        {
            ApplyToggleShortcut = s => Apply(s, isToggle: true),
            ApplyClearShortcut = s => Apply(s, isToggle: false),
        };
        w.RecordingChanged += recording =>
        {
            if (recording)
            {
                UnregisterHotKeys();
            }
            else
            {
                RegisterHotKeys();
            }
        };
        return w;
    }

    private void ShowWelcome()
    {
        welcome ??= new UI.WelcomeWindow();
        welcome.TryIt += () => overlay.Activate();
        welcome.ShowAndActivate();
    }

    private bool Apply(Shortcut s, bool isToggle)
    {
        UnregisterHotKeys();
        var old = isToggle ? Preferences.ToggleShortcut : Preferences.ClearShortcut;
        if (isToggle)
        {
            Preferences.ToggleShortcut = s;
        }
        else
        {
            Preferences.ClearShortcut = s;
        }
        if (RegisterHotKeys())
        {
            return true;
        }
        UnregisterHotKeys();
        if (isToggle)
        {
            Preferences.ToggleShortcut = old;
        }
        else
        {
            Preferences.ClearShortcut = old;
        }
        RegisterHotKeys();
        return false;
    }

    private bool RegisterHotKeys()
    {
        if (toggleHotKey != null || clearHotKey != null)
        {
            return true;
        }
        var t = Preferences.ToggleShortcut;
        var c = Preferences.ClearShortcut;
        toggleHotKey = hotKeys.Register(t.Vk, t.Modifiers, overlay.Toggle);
        clearHotKey = hotKeys.Register(c.Vk, c.Modifiers, overlay.ClearInk);
        return toggleHotKey != null && clearHotKey != null;
    }

    private void UnregisterHotKeys()
    {
        if (toggleHotKey is { } t)
        {
            hotKeys.Unregister(t);
        }
        if (clearHotKey is { } c)
        {
            hotKeys.Unregister(c);
        }
        toggleHotKey = null;
        clearHotKey = null;
    }

    private static void EnableLaunchAtLoginOnFirstRun()
    {
        if (Preferences.LoginItemSetUp || !Updater.IsInstalled)
        {
            return;
        }
        Preferences.LoginItemSetUp = true;
        LaunchAtLogin.Enabled = true;
        Log.Write($"first run: open at login = {LaunchAtLogin.Enabled}");
    }

    public void Quit()
    {
        overlay.Deactivate();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        overlay.Dispose();
        tray.Dispose();
        messages.Dispose();
        CursorHider.Show();
    }
}
