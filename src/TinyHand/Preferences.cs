using System.Text.Json;
using System.Text.Json.Nodes;
using TinyHand.Native;

namespace TinyHand;

internal readonly record struct Shortcut(uint Vk, uint Modifiers, string Display)
{
    public static readonly Shortcut ToggleDefault = new(0x48, Win32.MOD_CONTROL | Win32.MOD_ALT, "Ctrl+Alt+H");
    public static readonly Shortcut ClearDefault = new(0x58, Win32.MOD_CONTROL | Win32.MOD_ALT, "Ctrl+Alt+X");
}

internal enum InteractKey
{
    Alt,
    Ctrl,
    Shift,
    Off,
}

internal static class InteractKeyInfo
{
    public static string Title(this InteractKey key) => key switch
    {
        InteractKey.Alt => "Alt",
        InteractKey.Ctrl => "Ctrl",
        InteractKey.Shift => "Shift",
        _ => "Off",
    };

    public static int VirtualKey(this InteractKey key) => key switch
    {
        InteractKey.Alt => Win32.VK_MENU,
        InteractKey.Ctrl => Win32.VK_CONTROL,
        InteractKey.Shift => Win32.VK_SHIFT,
        _ => 0,
    };

    public static bool IsHeld(this InteractKey key) => key != InteractKey.Off && Win32.KeyDown(key.VirtualKey());
}

/// <summary>An sRGB colour packed as 0xRRGGBB.</summary>
internal readonly record struct Rgb(uint Value)
{
    public byte R => (byte)(Value >> 16);
    public byte G => (byte)(Value >> 8);
    public byte B => (byte)Value;

    public static readonly Rgb Purple = new(0x8754F2);
    public static readonly Rgb Red = new(0xFF291F);

    public System.Windows.Media.Color ToMedia(byte alpha = 255) => System.Windows.Media.Color.FromArgb(alpha, R, G, B);
}

internal enum InkChoice
{
    Match,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    White,
    Black,
}

internal static class InkChoiceInfo
{
    public static string Title(this InkChoice c) => c == InkChoice.Match ? "Match the pointer" : c.ToString();

    public static Rgb? Color(this InkChoice c) => c switch
    {
        InkChoice.Red => Rgb.Red,
        InkChoice.Orange => new Rgb(0xFF8A00),
        InkChoice.Yellow => new Rgb(0xFFCC00),
        InkChoice.Green => new Rgb(0x30C25A),
        InkChoice.Blue => new Rgb(0x0A84FF),
        InkChoice.Purple => new Rgb(0x8754F2),
        InkChoice.White => new Rgb(0xFFFFFF),
        InkChoice.Black => new Rgb(0x1D1D21),
        _ => null,
    };

    public static Rgb Resolved(this InkChoice c, PointerStyle style) => c.Color() ?? style.InkColor;
}

internal enum TapSoundChoice
{
    Thock,
    Click,
    Pop,
    Wood,
    Boop,
    Squeak,
}

internal static class TapSoundChoiceInfo
{
    public static string Title(this TapSoundChoice c) => c == TapSoundChoice.Wood ? "Wood block" : c.ToString();
    public static string FileName(this TapSoundChoice c) => $"tap_{c.ToString().ToLowerInvariant()}.wav";
}

/// <summary>
/// User settings, stored as JSON in %APPDATA%\TinyHand\settings.json. Setters save and raise
/// <see cref="Changed"/>; call them from the UI thread.
/// </summary>
internal static class Preferences
{
    public static event Action? Changed;

    private static readonly string FilePath = Path.Combine(Paths.RoamingData, "settings.json");
    private static readonly object Gate = new();
    private static JsonObject data = Load();

    public const int MaxPinned = 8;
    public const double PointerSizeMin = 0.08;
    public const double PointerSizeMax = 0.20;

    public static Shortcut ToggleShortcut
    {
        get => GetShortcut("shortcut.toggle") ?? Shortcut.ToggleDefault;
        set => SetShortcut("shortcut.toggle", value);
    }

    public static Shortcut ClearShortcut
    {
        get => GetShortcut("shortcut.clear") ?? Shortcut.ClearDefault;
        set => SetShortcut("shortcut.clear", value);
    }

    public static double PointerSize
    {
        get => Math.Clamp(Get("pointerSize", 0.13), PointerSizeMin, PointerSizeMax);
        set => Set("pointerSize", value);
    }

    public static bool ShowShadow
    {
        get => Get("showShadow", true);
        set => Set("showShadow", value);
    }

    public static double FadeSeconds
    {
        get => Get("fadeSeconds", 3.0);
        set => Set("fadeSeconds", value);
    }

    public static bool HandSound
    {
        get => Get("sound.hands", true);
        set => Set("sound.hands", value);
    }

    public static bool LaserSound
    {
        get => Get("sound.laser", false);
        set => Set("sound.laser", value);
    }

    public static TapSoundChoice TapSound
    {
        get => GetEnum("tapSound", TapSoundChoice.Thock);
        set => Set("tapSound", value.ToString());
    }

    public static InteractKey InteractKey
    {
        get => GetEnum("interactKey", InteractKey.Alt);
        set => Set("interactKey", value.ToString());
    }

    public static bool InteractKeepsHand
    {
        get => Get("interactKeepsHand", false);
        set => Set("interactKeepsHand", value);
    }

    public static InkChoice InkChoice
    {
        get => GetEnum("inkChoice", InkChoice.Match);
        set => Set("inkChoice", value.ToString());
    }

    public static bool ShowInkButton
    {
        get => Get("showInkButton", true);
        set => Set("showInkButton", value);
    }

    public static string PointerStyleId
    {
        get => Get("pointerStyle", "lavender");
        set => Set("pointerStyle", value);
    }

    public static IReadOnlyList<PointerStyle> PinnedStyles
    {
        get
        {
            JsonArray? raw;
            lock (Gate)
            {
                raw = data["pinnedStyles"] as JsonArray;
            }
            if (raw == null)
            {
                return PointerStyle.DefaultPinned;
            }
            var pinned = raw.Select(n => n?.GetValue<string>())
                .Select(id => id == null ? null : PointerStyle.Find(id))
                .OfType<PointerStyle>()
                .Distinct()
                .ToList();
            return pinned.Count == 0 ? PointerStyle.DefaultPinned : pinned;
        }
        set => SetNode("pinnedStyles", new JsonArray(value.Select(s => (JsonNode)JsonValue.Create(s.Id)!).ToArray()));
    }

    public static bool AutoUpdate
    {
        get => Get("updates.auto", true);
        set => Set("updates.auto", value);
    }

    // Machine flags that are not user settings and are not touched by Reset.
    public static bool WelcomeShown
    {
        get => Get("welcomeShown", false);
        set => Set("welcomeShown", value, notify: false);
    }

    public static bool HintShown
    {
        get => Get("firstRunHintShown", false);
        set => Set("firstRunHintShown", value, notify: false);
    }

    public static bool LoginItemSetUp
    {
        get => Get("loginItemSetUp", false);
        set => Set("loginItemSetUp", value, notify: false);
    }

    public static void ResetAll()
    {
        lock (Gate)
        {
            foreach (var key in new[]
                     {
                         "shortcut.toggle", "shortcut.clear", "pointerSize", "showShadow", "fadeSeconds",
                         "sound.hands", "sound.laser", "interactKey", "interactKeepsHand", "pinnedStyles",
                         "pointerStyle", "inkChoice", "showInkButton", "tapSound",
                     })
            {
                data.Remove(key);
            }
        }
        Save();
        NotifyChanged();
    }

    public static void NotifyChanged() => Changed?.Invoke();

    private static T Get<T>(string key, T fallback)
    {
        lock (Gate)
        {
            try
            {
                return data[key] is JsonValue v && v.TryGetValue(out T? value) && value != null ? value : fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }

    private static T GetEnum<T>(string key, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(Get(key, ""), ignoreCase: true, out var v) ? v : fallback;

    private static void Set<T>(string key, T value, bool notify = true) =>
        SetNode(key, JsonValue.Create(value), notify);

    private static void SetNode(string key, JsonNode? node, bool notify = true)
    {
        lock (Gate)
        {
            data[key] = node;
        }
        Save();
        if (notify)
        {
            NotifyChanged();
        }
    }

    private static Shortcut? GetShortcut(string key)
    {
        lock (Gate)
        {
            if (data[key] is not JsonObject o)
            {
                return null;
            }
            try
            {
                return new Shortcut(o["vk"]!.GetValue<uint>(), o["modifiers"]!.GetValue<uint>(), o["display"]!.GetValue<string>());
            }
            catch
            {
                return null;
            }
        }
    }

    private static void SetShortcut(string key, Shortcut s) =>
        SetNode(key, new JsonObject { ["vk"] = s.Vk, ["modifiers"] = s.Modifiers, ["display"] = s.Display });

    private static JsonObject Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                return o;
            }
        }
        catch (Exception e)
        {
            Log.Write($"settings unreadable, starting fresh: {e.Message}");
        }
        return new JsonObject();
    }

    private static void Save()
    {
        string json;
        lock (Gate)
        {
            json = data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Write($"settings save failed: {e.Message}");
        }
    }
}
