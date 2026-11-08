using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TinyHand.UI;

/// <summary>
/// Small builders for the settings and welcome windows. Colours come from the WPF Fluent theme's
/// resources, so everything follows the Windows light/dark setting.
/// </summary>
internal static class Ui
{
    public const string TextPrimary = "TextFillColorPrimaryBrush";
    public const string TextSecondary = "TextFillColorSecondaryBrush";
    public const string TextTertiary = "TextFillColorTertiaryBrush";
    public const string CardBackground = "CardBackgroundFillColorDefaultBrush";
    public const string CardStroke = "CardStrokeColorDefaultBrush";
    public const string SubtleHover = "SubtleFillColorSecondaryBrush";
    public const string SubtlePressed = "SubtleFillColorTertiaryBrush";
    public const string Accent = "AccentFillColorDefaultBrush";
    public const string OnAccent = "TextOnAccentFillColorPrimaryBrush";
    public const string ControlFill = "ControlFillColorDefaultBrush";
    public const string ControlStroke = "ControlStrokeColorDefaultBrush";
    public const string Divider = "DividerStrokeColorDefaultBrush";
    public const string Critical = "SystemFillColorCriticalBrush";
    public const string Caution = "SystemFillColorCautionBrush";
    public const string SolidBackground = "SolidBackgroundFillColorBaseBrush";
    public const string LayerFill = "LayerFillColorDefaultBrush";

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    public static T Res<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    public static TextBlock Text(string text, double size = 14, string brush = TextPrimary, FontWeight? weight = null)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = Font,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Title(string text) =>
        new TextBlock
        {
            Text = text,
            FontSize = 28,
            FontFamily = DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        }.Res(TextBlock.ForegroundProperty, TextPrimary);

    public static TextBlock SectionHeader(string text) =>
        new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontFamily = Font,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 8),
        }.Res(TextBlock.ForegroundProperty, TextPrimary);

    /// <summary>A rounded card whose rows are separated by hairlines.</summary>
    public static FrameworkElement Card(string? title, params UIElement?[] rows)
    {
        var stack = new StackPanel();
        bool first = true;
        foreach (var row in rows.Where(r => r != null))
        {
            if (!first)
            {
                stack.Children.Add(new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0) }.Res(Border.BackgroundProperty, Divider));
            }
            stack.Children.Add(row!);
            first = false;
        }
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = stack,
        }.Res(Border.BackgroundProperty, CardBackground).Res(Border.BorderBrushProperty, CardStroke);
        if (title == null)
        {
            return card;
        }
        var outer = new StackPanel();
        outer.Children.Add(SectionHeader(title));
        outer.Children.Add(card);
        return outer;
    }

    /// <summary>Title (and optional detail) on the left, a control on the right.</summary>
    public static FrameworkElement Row(string title, string? detail, UIElement? trailing)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12), MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Text(title, 14));
        if (detail != null)
        {
            left.Children.Add(Text(detail, 12, TextSecondary));
        }
        grid.Children.Add(left);
        if (trailing is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 1);
            grid.Children.Add(fe);
        }
        return grid;
    }

    public static ComboBox Combo<T>(IEnumerable<(string Title, T Value)> items, T selected, Action<T> changed)
    {
        var combo = new ComboBox { MinWidth = 180, FontFamily = Font };
        var list = items.ToList();
        foreach (var (title, _) in list)
        {
            combo.Items.Add(new ComboBoxItem { Content = title });
        }
        combo.SelectedIndex = Math.Max(0, list.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, selected)));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                changed(list[combo.SelectedIndex].Value);
            }
        };
        return combo;
    }

    /// <summary>A text button that looks like a link.</summary>
    public static FrameworkElement LinkButton(string text, Action action, double size = 13)
    {
        var t = Text(text, size, Accent);
        t.Cursor = Cursors.Hand;
        t.TextWrapping = TextWrapping.NoWrap;
        t.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline;
        t.MouseLeave += (_, _) => t.TextDecorations = null;
        t.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return t;
    }

    public static Button Button(string text, Action action, bool accent = false, string? glyph = null)
    {
        object content = text;
        if (glyph != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Icons.Glyph(glyph, 12);
            g.Margin = new Thickness(0, 0, 8, 0);
            g.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            row.Children.Add(g);
            row.Children.Add(new TextBlock { Text = text, FontFamily = Font });
            content = row;
        }
        var b = new Button
        {
            Content = content,
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 96,
            FontFamily = Font,
        };
        if (accent)
        {
            b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        }
        b.Click += (_, _) => action();
        return b;
    }

    public static Image Picture(ImageSource? source, double size)
    {
        var img = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    /// <summary>Rounded square tile with a symbol, used in the sidebar.</summary>
    public static Border IconTile(string glyph, Color tint) => new()
    {
        Width = 24,
        Height = 24,
        CornerRadius = new CornerRadius(6),
        Background = new LinearGradientBrush(Color.FromArgb(0xD9, tint.R, tint.G, tint.B), tint, 90),
        Child = Icons.Glyph(glyph, 12),
    };

    public static Rectangle Spacer(double height) => new() { Height = height };

    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Write($"open {url} failed: {e.Message}");
        }
    }
}
