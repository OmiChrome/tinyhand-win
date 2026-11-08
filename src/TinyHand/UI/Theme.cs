using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TinyHand.Native;

namespace TinyHand.UI;

/// <summary>
/// TinyHand's own look: always dark, warm tones from the website, lavender accent. Nothing is taken
/// from the user's Windows personalisation (accent colour, light/dark mode, Mica transparency).
/// </summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromRgb(0x1A, 0x12, 0x18);
    public static readonly Color Card = Color.FromRgb(0x26, 0x1C, 0x24);
    public static readonly Color CardStroke = Color.FromRgb(0x36, 0x2A, 0x33);
    public static readonly Color Popup = Color.FromRgb(0x22, 0x18, 0x20);
    public static readonly Color Text = Color.FromRgb(0xF2, 0xEA, 0xD8);
    public static readonly Color Accent = Color.FromRgb(0x87, 0x54, 0xF2);   // the lavender hand's ink
    public static readonly Color AccentLight = Color.FromRgb(0x9E, 0x74, 0xF6);
    public static readonly Color AccentLighter = Color.FromRgb(0xB8, 0x9A, 0xF8);
    public static readonly Color AccentDark = Color.FromRgb(0x6F, 0x3F, 0xDB);

    private static Color A(Color c, double alpha) => Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    /// <summary>Override the Fluent theme's colour resources app-wide.</summary>
    public static void Apply(Application app)
    {
        var r = app.Resources;
        void Brush(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            r[key] = b;
        }

        // Accent (the Fluent theme normally copies the Windows accent colour).
        r["SystemAccentColor"] = Accent;
        r["SystemAccentColorLight1"] = AccentLight;
        r["SystemAccentColorLight2"] = AccentLighter;
        r["SystemAccentColorLight3"] = AccentLighter;
        r["SystemAccentColorDark1"] = AccentDark;
        r["SystemAccentColorDark2"] = AccentDark;
        r["SystemAccentColorDark3"] = AccentDark;
        Brush("AccentFillColorDefaultBrush", Accent);
        Brush("AccentFillColorSecondaryBrush", A(Accent, 0.9));
        Brush("AccentFillColorTertiaryBrush", A(Accent, 0.8));
        Brush("AccentTextFillColorPrimaryBrush", AccentLighter);
        Brush("AccentTextFillColorSecondaryBrush", AccentLighter);
        Brush("AccentTextFillColorTertiaryBrush", AccentLight);
        Brush("TextOnAccentFillColorPrimaryBrush", Colors.White);
        Brush("TextOnAccentFillColorSecondaryBrush", A(Colors.White, 0.8));
        Brush("SystemFillColorAttentionBrush", Accent);
        Brush("AccentButtonBackground", Accent);
        Brush("AccentButtonBackgroundPointerOver", AccentLight);
        Brush("AccentButtonBackgroundPressed", AccentDark);
        Brush("AccentButtonBackgroundDisabled", A(Text, 0.12));
        Brush("AccentButtonForeground", Colors.White);
        Brush("AccentButtonForegroundPointerOver", Colors.White);
        Brush("AccentButtonForegroundPressed", A(Colors.White, 0.8));
        Brush("AccentButtonBorderBrush", AccentDark);
        Brush("AccentButtonBorderBrushPointerOver", Accent);
        Brush("AccentButtonBorderBrushPressed", AccentDark);
        Brush("AccentControlElevationBorderBrush", AccentDark);

        // Text and surfaces in the website's warm palette.
        Brush("TextFillColorPrimaryBrush", Text);
        Brush("TextFillColorSecondaryBrush", A(Text, 0.68));
        Brush("TextFillColorTertiaryBrush", A(Text, 0.45));
        Brush("TextFillColorDisabledBrush", A(Text, 0.36));
        Brush("CardBackgroundFillColorDefaultBrush", Card);
        Brush("CardStrokeColorDefaultBrush", CardStroke);
        Brush("SolidBackgroundFillColorBaseBrush", Popup);
        Brush("ApplicationBackgroundBrush", Background);
        Brush("SubtleFillColorSecondaryBrush", A(Text, 0.07));
        Brush("SubtleFillColorTertiaryBrush", A(Text, 0.045));
        Brush("DividerStrokeColorDefaultBrush", A(Text, 0.08));
        Brush("ControlFillColorDefaultBrush", A(Text, 0.06));
        Brush("ControlStrokeColorDefaultBrush", A(Text, 0.12));
    }

    /// <summary>Solid background and a matching title bar, with no Mica or accent-coloured caption.</summary>
    public static void ApplyWindow(Window window)
    {
        window.Background = new SolidColorBrush(Background);
        window.Foreground = new SolidColorBrush(Text);
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            int none = 1; // DWMSBT_NONE: no Mica / acrylic
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            int caption = ColorRef(Background);
            Win32.DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
            int border = ColorRef(CardStroke);
            Win32.DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref border, sizeof(int));
            int text = ColorRef(Text);
            Win32.DwmSetWindowAttribute(hwnd, 36 /* DWMWA_TEXT_COLOR */, ref text, sizeof(int));
        };
    }

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
