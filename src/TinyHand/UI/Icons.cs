using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TinyHand.UI;

/// <summary>Small drawn images shared by the toolbar and settings.</summary>
internal static class Icons
{
    public static readonly FontFamily SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Segoe Fluent Icons code points
    public const string Volume = "";
    public const string Mute = "";
    public const string Close = "";
    public const string Add = "";
    public const string Settings = "";
    public const string Hand = "";
    public const string Sparkle = "";
    public const string Pen = "";
    public const string Info = "";
    public const string Pointer = "";
    public const string OpenExternal = "";
    public const string Copy = "";
    public const string Check = "";
    public const string Warning = "";
    public const string Download = "";
    public const string Back = "";
    public const string Wand = "";
    public const string Wave = "";

    public static TextBlock Glyph(string code, double size, Brush? brush = null) => new()
    {
        Text = code,
        FontFamily = SymbolFont,
        FontSize = size,
        Foreground = brush ?? Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A glossy colour dot, as on the macOS toolbar's ink button.</summary>
    public static ImageSource InkDot(Color color)
    {
        var g = new DrawingGroup();
        var c = new Point(32, 32);
        const double r = 13;
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), null,
            new EllipseGeometry(new Point(c.X, c.Y + 1), r + 1.5, r + 1.5)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new EllipseGeometry(c, r, r)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), null,
            new EllipseGeometry(new Point(c.X - r * 0.2, c.Y - r * 0.4), r * 0.35, r * 0.25)));
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        return Frozen(new DrawingImage(g));
    }

    /// <summary>Six-colour pie used for "match the pointer".</summary>
    public static ImageSource RainbowDot()
    {
        var g = new DrawingGroup();
        var c = new Point(32, 32);
        const double r = 13;
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), null,
            new EllipseGeometry(new Point(c.X, c.Y + 1), r + 1.5, r + 1.5)));
        Color[] colors = { Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0x95, 0x00), Color.FromRgb(0xFF, 0xCC, 0x00),
                           Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0x00, 0x7A, 0xFF), Color.FromRgb(0xAF, 0x52, 0xDE) };
        for (int i = 0; i < 6; i++)
        {
            double a0 = (i * 60 - 90) * Math.PI / 180;
            double a1 = ((i + 1) * 60 - 90) * Math.PI / 180;
            var fig = new PathFigure { StartPoint = c, IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), true));
            fig.Segments.Add(new ArcSegment(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, false,
                SweepDirection.Clockwise, true));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[i]), null, new PathGeometry(new[] { fig })));
        }
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        return Frozen(new DrawingImage(g));
    }

    /// <summary>The laser pointer's toolbar icon: a red dot with a soft glow.</summary>
    public static ImageSource LaserIcon()
    {
        var g = new DrawingGroup();
        var c = new Point(32, 32);
        var red = Color.FromRgb(0xFF, 0x29, 0x1F);
        var glow = new RadialGradientBrush(Color.FromArgb(0x8C, red.R, red.G, red.B), Color.FromArgb(0, red.R, red.G, red.B));
        g.Children.Add(new GeometryDrawing(glow, null, new EllipseGeometry(c, 18, 18)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(red), null, new EllipseGeometry(c, 7, 7)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xDB, 0xD1)), null, new EllipseGeometry(c, 3, 3)));
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        return Frozen(new DrawingImage(g));
    }

    private static ImageSource? laser;

    public static ImageSource? For(PointerStyle style)
    {
        if (style.AssetName is { } name)
        {
            return HandAsset.Thumbnail(name);
        }
        return laser ??= LaserIcon();
    }

    public static ImageSource? CachedFor(PointerStyle style) =>
        style.AssetName is { } name ? HandAsset.CachedThumbnail(name) : laser ??= LaserIcon();

    public static ImageSource AppIcon { get; } = Frozen(new System.Windows.Media.Imaging.BitmapImage(
        new Uri("pack://application:,,,/Assets/AppIcon.png")));

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
