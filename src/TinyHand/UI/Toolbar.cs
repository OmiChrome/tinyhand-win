using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TinyHand.Native;
using TinyHand.Overlay;

namespace TinyHand.UI;

/// <summary>
/// The strip at the bottom of the screen while the pointer is on: pinned pointers, ink colour,
/// tap sound and exit. Also owns the first-run hint and the ink colour tray.
/// </summary>
internal sealed class Toolbar
{
    public event Action<PointerStyle>? Picked;
    public event Action? SoundToggled;
    public event Action? ExitClicked;

    private static readonly Brush Selected = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Hover = Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));

    private readonly FloatingPanel panel = new(16);
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };
    private readonly StackPanel styleRow = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<PointerStyle, ToolButton> styleButtons = new();
    private List<PointerStyle> shownStyles = new();
    private PointerStyle? current;
    private readonly ToolButton inkButton;
    private readonly Image inkImage = new() { Width = 40, Height = 40 };
    private readonly ToolButton soundButton;
    private readonly TextBlock soundGlyph = Icons.Glyph(Icons.Volume, 16);

    private readonly FloatingPanel tray = new(14);
    private readonly StackPanel trayRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 5, 6, 5) };
    private readonly Dictionary<InkChoice, ToolButton> swatches = new();
    private bool inkOpen;

    private FloatingPanel? hint;
    private MonitorState monitor;

    public bool Revealed { get; private set; }

    public Toolbar()
    {
        row.Children.Add(styleRow);
        row.Children.Add(new Border
        {
            Width = 1,
            Height = 24,
            Margin = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        inkButton = new ToolButton(inkImage, "Ink colour", ToggleInk);
        soundButton = new ToolButton(soundGlyph, "Tap sound", () => SoundToggled?.Invoke());
        row.Children.Add(inkButton);
        row.Children.Add(soundButton);
        row.Children.Add(new ToolButton(Icons.Glyph(Icons.Close, 14, new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF))),
            "Exit  (Esc)", () => ExitClicked?.Invoke()));
        SetCardContent(panel, row);
        SetCardContent(tray, trayRow);

        foreach (var choice in Enum.GetValues<InkChoice>())
        {
            var image = new Image
            {
                Width = 30,
                Height = 30,
                Source = choice.Color() is { } c ? Icons.InkDot(c.ToMedia()) : Icons.RainbowDot(),
            };
            var b = new ToolButton(image, choice.Title(), () =>
            {
                Preferences.InkChoice = choice;
                SetInkOpen(false);
            }, side: 30, radius: 15);
            swatches[choice] = b;
            trayRow.Children.Add(b);
        }

        panel.SetClickThrough(true);
        tray.SetClickThrough(false);
        RebuildStyles();
        RefreshInk();
        Preferences.Changed += () =>
        {
            RebuildStyles();
            RefreshInk();
        };
        CustomHands.Changed += RebuildStyles;
    }

    private static void SetCardContent(FloatingPanel p, UIElement content) =>
        ((Border)p.Content).Child = content;

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    /// <summary>Toolbar plus ink tray (when open), in physical pixels.</summary>
    public RECT Frame
    {
        get
        {
            var r = panel.CardPixels;
            if (inkOpen)
            {
                var t = tray.CardPixels;
                r = new RECT(Math.Min(r.Left, t.Left), Math.Min(r.Top, t.Top), Math.Max(r.Right, t.Right), Math.Max(r.Bottom, t.Bottom));
            }
            return r;
        }
    }

    public void Select(PointerStyle style)
    {
        current = style;
        RebuildStyles();
        Highlight(style);
        RefreshInk();
    }

    public void SetSoundOn(bool on)
    {
        soundGlyph.Text = on ? Icons.Volume : Icons.Mute;
        soundGlyph.Foreground = new SolidColorBrush(Color.FromArgb(on ? (byte)0xD9 : (byte)0x73, 0xFF, 0xFF, 0xFF));
        soundButton.ToolTip = on ? "Tap sound on — click to mute" : "Tap sound off — click to unmute";
    }

    private void RebuildStyles()
    {
        var styles = Preferences.PinnedStyles.ToList();
        if (current != null && !styles.Contains(current))
        {
            styles.Add(current);
        }
        if (styles.SequenceEqual(shownStyles))
        {
            return;
        }
        shownStyles = styles;
        styleRow.Children.Clear();
        styleButtons.Clear();
        foreach (var style in styles)
        {
            var image = new Image { Width = 40, Height = 40, Source = Icons.For(style) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var b = new ToolButton(image, style.Title, () => Picked?.Invoke(style));
            styleButtons[style] = b;
            styleRow.Children.Add(b);
        }
        if (current != null)
        {
            Highlight(current);
        }
    }

    private void Highlight(PointerStyle style)
    {
        foreach (var (s, b) in styleButtons)
        {
            b.IsSelected = s == style;
        }
    }

    private void RefreshInk()
    {
        var style = current ?? PointerStyle.Saved;
        inkImage.Source = Icons.InkDot(Preferences.InkChoice.Resolved(style).ToMedia());
        inkButton.IsSelected = inkOpen;
        bool hidden = !Preferences.ShowInkButton;
        inkButton.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        if (hidden)
        {
            SetInkOpen(false);
        }
        foreach (var (choice, b) in swatches)
        {
            b.IsSelected = choice == Preferences.InkChoice;
        }
    }

    private void ToggleInk() => SetInkOpen(!inkOpen);

    private void SetInkOpen(bool open)
    {
        if (open == inkOpen)
        {
            return;
        }
        inkOpen = open;
        if (open)
        {
            tray.PlaceWith(p =>
            {
                var ink = inkButton.PointToScreen(new Point(inkButton.ActualWidth / 2, 0));
                var w = p.WindowPixels;
                int gap = (int)Math.Round(8 * p.PixelScale);
                int m = (int)Math.Round(FloatingPanel.ShadowMargin * p.PixelScale);
                return ((int)ink.X - w.Width / 2, panel.CardPixels.Top - gap - w.Height + m);
            });
            tray.ShowPanel();
            tray.FadeTo(1, 0.12);
        }
        else if (tray.IsVisible)
        {
            tray.FadeTo(0, 0.12, () =>
            {
                if (!inkOpen)
                {
                    tray.Hide();
                }
            });
        }
        inkButton.IsSelected = inkOpen;
    }

    public void PlaceOn(MonitorState m)
    {
        monitor = m;
        SetInkOpen(false);
        panel.PlaceWith(p =>
        {
            var w = p.WindowPixels;
            double s = p.PixelScale;
            int x = m.Bounds.Left + (m.Bounds.Width - w.Width) / 2;
            int y = m.Work.Bottom - (int)Math.Round(14 * s) + (int)Math.Round(FloatingPanel.ShadowMargin * s) - w.Height;
            return (x, y);
        });
        hint?.Reposition();
    }

    public void Show()
    {
        panel.ShowPanel();
        if (monitor.Handle != IntPtr.Zero)
        {
            PlaceOn(monitor);
        }
    }

    public void Hide()
    {
        SetInkOpen(false);
        HideHint();
        Revealed = false;
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        panel.Opacity = 0;
        panel.SetClickThrough(true);
        panel.Hide();
    }

    public void SetRevealed(bool value)
    {
        if (value == Revealed)
        {
            return;
        }
        Revealed = value;
        if (!value)
        {
            SetInkOpen(false);
        }
        panel.SetClickThrough(!value);
        panel.FadeTo(value ? 1 : 0, value ? 0.15 : 0.35);
    }

    public void ShowHint(IEnumerable<(string Key, string Text)> parts)
    {
        HideHint();
        var p = new FloatingPanel(12);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 9, 16, 9) };
        bool first = true;
        foreach (var (key, text) in parts)
        {
            var tb = new TextBlock { FontSize = 12.5, Margin = new Thickness(first ? 0 : 14, 0, 0, 0) };
            tb.Inlines.Add(new System.Windows.Documents.Run(key) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            tb.Inlines.Add(new System.Windows.Documents.Run(" " + text)
            {
                Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            });
            line.Children.Add(tb);
            first = false;
        }
        SetCardContent(p, line);
        p.SetClickThrough(true);
        p.PlaceWith(h =>
        {
            var w = h.WindowPixels;
            var bar = panel.CardPixels;
            int gap = (int)Math.Round(12 * h.PixelScale);
            int m = (int)Math.Round(FloatingPanel.ShadowMargin * h.PixelScale);
            return (bar.Left + (bar.Width - w.Width) / 2, bar.Top - gap - w.Height + m);
        });
        p.ShowPanel();
        p.FadeTo(1, 0.2);
        hint = p;
    }

    public void HideHint()
    {
        if (hint is not { } p)
        {
            return;
        }
        hint = null;
        p.FadeTo(0, 0.3, p.Close);
    }

    /// <summary>A flat icon button that works without the window ever being activated.</summary>
    private sealed class ToolButton : Border
    {
        private readonly Action action;
        private bool selected;
        private bool pressed;

        public ToolButton(UIElement content, string tip, Action action, double side = 40, double radius = 10)
        {
            this.action = action;
            Width = side;
            Height = side;
            CornerRadius = new CornerRadius(radius);
            Background = Brushes.Transparent;
            Child = content;
            ToolTip = tip;
            Margin = new Thickness(3, 0, 3, 0);
            Cursor = Cursors.Arrow;
            MouseEnter += (_, _) => Refresh();
            MouseLeave += (_, _) =>
            {
                pressed = false;
                Refresh();
            };
            MouseLeftButtonDown += (_, e) =>
            {
                pressed = true;
                e.Handled = true;
            };
            MouseLeftButtonUp += (_, e) =>
            {
                if (pressed)
                {
                    pressed = false;
                    e.Handled = true;
                    this.action();
                }
            };
        }

        public bool IsSelected
        {
            get => selected;
            set
            {
                selected = value;
                Refresh();
            }
        }

        private void Refresh() => Background = selected ? Selected : IsMouseOver ? Hover : Brushes.Transparent;
    }
}
