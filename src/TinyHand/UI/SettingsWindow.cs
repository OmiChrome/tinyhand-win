using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TinyHand.Native;

namespace TinyHand.UI;

internal enum SettingsSection
{
    General,
    Pointers,
    Appearance,
    Drawing,
    Sound,
    About,
}

/// <summary>Settings: General, Pointers (toolbar + custom hands), Appearance, Drawing, Sound, About.</summary>
internal sealed class SettingsWindow : Window
{
    public Func<Shortcut, bool>? ApplyToggleShortcut { get; init; }
    public Func<Shortcut, bool>? ApplyClearShortcut { get; init; }
    public event Action<bool>? RecordingChanged;

    private static readonly (SettingsSection Section, string Glyph, Color Tint)[] Sections =
    {
        (SettingsSection.General, Icons.Settings, Color.FromRgb(0x8C, 0x8C, 0x8C)),
        (SettingsSection.Pointers, Icons.Hand, Color.FromRgb(0x8C, 0x6B, 0xF2)),
        (SettingsSection.Appearance, Icons.Sparkle, Color.FromRgb(0xF2, 0x73, 0x99)),
        (SettingsSection.Drawing, Icons.Pen, Color.FromRgb(0xF5, 0x94, 0x33)),
        (SettingsSection.Sound, Icons.Volume, Color.FromRgb(0x33, 0x8C, 0xFA)),
        (SettingsSection.About, Icons.Info, Color.FromRgb(0x73, 0x73, 0x73)),
    };

    private readonly StackPanel sidebarItems = new();
    private readonly ContentControl page = new();
    private readonly ScrollViewer scroller;
    private SettingsSection section = SettingsSection.General;
    private TapSound? preview;

    // Page state
    private string? shortcutError;
    private string? pinMessage;
    private string? handMessage;
    private bool makingHand;
    private string handIdea = "";
    private string handName = "";
    private HandMaker.Result? draft;
    private string? draftError;
    private bool promptCopied;

    public SettingsWindow()
    {
        Title = "TinyHand Settings";
        Width = 880;
        Height = 640;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Icons.AppIcon;
        FontFamily = Ui.Font;
        UseLayoutRounding = true;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new DockPanel { Margin = new Thickness(12, 16, 12, 16), LastChildFill = false };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        brand.Children.Add(Ui.Picture(Icons.AppIcon, 22));
        var brandName = Ui.Text("TinyHand", 13, weight: FontWeights.SemiBold);
        brandName.Margin = new Thickness(8, 0, 0, 0);
        brandName.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(brandName);
        DockPanel.SetDock(brand, Dock.Bottom);
        sidebar.Children.Add(brand);
        DockPanel.SetDock(sidebarItems, Dock.Top);
        sidebar.Children.Add(sidebarItems);
        root.Children.Add(sidebar);

        scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Padding = new Thickness(28, 20, 32, 32), Child = page, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch },
        };
        Grid.SetColumn(scroller, 1);
        root.Children.Add(scroller);
        Content = root;

        BuildSidebar();
        ShowPage();

        CustomHands.Changed += () => Dispatcher.BeginInvoke(ShowPage);
        HandAsset.PreloadThumbnails(PointerStyle.All.Select(s => s.AssetName).OfType<string>(), ShowPage);
        Closing += (_, e) =>
        {
            // Keep the window around so reopening is instant.
            e.Cancel = true;
            RecordingChanged?.Invoke(false);
            Hide();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int round = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    public void ShowAndActivate(SettingsSection? show = null)
    {
        if (show is { } s)
        {
            section = s;
        }
        shortcutError = null;
        pinMessage = null;
        BuildSidebar();
        ShowPage();
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Win32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void BuildSidebar()
    {
        sidebarItems.Children.Clear();
        foreach (var (s, glyph, tint) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.IconTile(glyph, tint));
            var label = Ui.Text(s.ToString(), 14, weight: s == section ? FontWeights.SemiBold : FontWeights.Normal);
            label.Margin = new Thickness(12, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            var item = new Border
            {
                Child = row,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 1, 0, 1),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            if (s == section)
            {
                item.SetResourceReference(Border.BackgroundProperty, Ui.SubtleHover);
            }
            item.MouseEnter += (_, _) =>
            {
                if (s != section)
                {
                    item.SetResourceReference(Border.BackgroundProperty, Ui.SubtlePressed);
                }
            };
            item.MouseLeave += (_, _) =>
            {
                if (s != section)
                {
                    item.Background = Brushes.Transparent;
                }
            };
            var target = s;
            item.MouseLeftButtonUp += (_, _) =>
            {
                section = target;
                makingHand = false;
                BuildSidebar();
                ShowPage();
                scroller.ScrollToTop();
            };
            sidebarItems.Children.Add(item);
        }
    }

    private void ShowPage()
    {
        var stack = new StackPanel();
        if (section == SettingsSection.Pointers && makingHand)
        {
            var back = Ui.LinkButton("‹  Pointers", () =>
            {
                makingHand = false;
                ShowPage();
            }, 12);
            back.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(back);
            stack.Children.Add(Ui.Title("Make your own hand"));
        }
        else
        {
            stack.Children.Add(Ui.Title(section.ToString()));
        }
        stack.Children.Add(Ui.Spacer(16));
        var body = section switch
        {
            SettingsSection.General => GeneralPage(),
            SettingsSection.Pointers => makingHand ? MakeHandPage() : PointersPage(),
            SettingsSection.Appearance => AppearancePage(),
            SettingsSection.Drawing => DrawingPage(),
            SettingsSection.Sound => SoundPage(),
            _ => AboutPage(),
        };
        stack.Children.Add(body);
        page.Content = stack;
    }

    private static StackPanel Column(double spacing, params UIElement?[] children)
    {
        var s = new StackPanel();
        bool first = true;
        foreach (var c in children.Where(c => c != null))
        {
            if (!first && c is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + spacing, fe.Margin.Right, fe.Margin.Bottom);
            }
            s.Children.Add(c!);
            first = false;
        }
        return s;
    }

    private static TextBlock Message(string text, bool error) =>
        Ui.Text(text, 12, error ? Ui.Critical : Ui.TextSecondary);

    // ---- General ---------------------------------------------------------------------------

    private UIElement GeneralPage()
    {
        var toggle = Recorder(Preferences.ToggleShortcut, ApplyToggleShortcut);
        var clear = Recorder(Preferences.ClearShortcut, ApplyClearShortcut);

        var interact = Ui.Combo(Enum.GetValues<InteractKey>().Select(k => (k == InteractKey.Off ? "Off" : $"Hold {k.Title()}", k)),
            Preferences.InteractKey, k =>
            {
                Preferences.InteractKey = k;
                ShowPage();
            });
        UIElement? whileHolding = null;
        if (Preferences.InteractKey != InteractKey.Off)
        {
            whileHolding = Ui.Row("While holding",
                Preferences.InteractKeepsHand ? "The hand stays, in menus too." : "The normal cursor comes back, in menus too.",
                Ui.Combo(new[] { ("Show the cursor", false), ("Keep the hand", true) }, Preferences.InteractKeepsHand, v =>
                {
                    Preferences.InteractKeepsHand = v;
                    ShowPage();
                }));
        }

        var login = new ToggleSwitch(LaunchAtLogin.Enabled);
        login.Toggled += on => LaunchAtLogin.Enabled = on;
        UIElement? updates = null;
        if (Updater.IsAvailable)
        {
            var auto = new ToggleSwitch(Preferences.AutoUpdate);
            auto.Toggled += on => Preferences.AutoUpdate = on;
            updates = Ui.Row("Check for updates automatically", null, auto);
        }

        return Column(24,
            Ui.Card("Shortcuts",
                Ui.Row("Turn the pointer on or off", null, toggle),
                Ui.Row("Clear the drawing", null, clear)),
            shortcutError == null ? null : Message(shortcutError, error: true),
            Ui.Card("Clicking for real",
                Ui.Row("Hold to use the app", "Clicks, drags and scrolls reach the app.", interact),
                whileHolding),
            Ui.Card("Startup",
                Ui.Row("Open TinyHand when you sign in", null, login),
                updates));
    }

    private ShortcutRecorder Recorder(Shortcut current, Func<Shortcut, bool>? apply)
    {
        var r = new ShortcutRecorder(current);
        r.RecordingChanged += on => RecordingChanged?.Invoke(on);
        r.Changed = s =>
        {
            bool ok = apply?.Invoke(s) ?? false;
            shortcutError = ok ? null : $"{s.Display} is already used by another app. Pick a different shortcut.";
            Dispatcher.BeginInvoke(ShowPage);
            return ok;
        };
        return r;
    }

    // ---- Pointers --------------------------------------------------------------------------

    private UIElement PointersPage()
    {
        var pinned = Preferences.PinnedStyles.ToList();
        bool full = pinned.Count >= Preferences.MaxPinned;

        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        var reset = Ui.LinkButton("Reset", () =>
        {
            pinMessage = null;
            Preferences.PinnedStyles = PointerStyle.DefaultPinned;
            ShowPage();
        }, 12);
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        header.Children.Add(Ui.Text("In your toolbar", 14, weight: FontWeights.SemiBold));

        var row = new PinnedRow(pinned, full, this);
        var hint = Ui.Text(full
            ? "The toolbar is full. Remove a pointer to add another. Drag to reorder."
            : "Press + to add a pointer. Drag to reorder, or out to remove.", 12, Ui.TextSecondary);
        hint.Margin = new Thickness(2, 8, 0, 0);

        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(row);
        top.Children.Add(hint);
        if (pinMessage != null)
        {
            var m = Message(pinMessage, error: true);
            m.Margin = new Thickness(2, 4, 0, 0);
            top.Children.Add(m);
        }
        return Column(32, top, YourHands());
    }

    internal void Pin(PointerStyle style)
    {
        var pinned = Preferences.PinnedStyles.ToList();
        if (pinned.Contains(style))
        {
            Unpin(style);
            return;
        }
        if (pinned.Count >= Preferences.MaxPinned)
        {
            pinMessage = $"The toolbar is full. Remove one above to add {style.Title}.";
            ShowPage();
            return;
        }
        pinned.Add(style);
        pinMessage = null;
        Preferences.PinnedStyles = pinned;
        ShowPage();
    }

    internal void Unpin(PointerStyle style)
    {
        var pinned = Preferences.PinnedStyles.ToList();
        if (!pinned.Contains(style))
        {
            return;
        }
        if (pinned.Count == 1)
        {
            pinMessage = "Keep at least one pointer in the toolbar.";
            ShowPage();
            return;
        }
        pinned.Remove(style);
        pinMessage = null;
        Preferences.PinnedStyles = pinned;
        ShowPage();
    }

    internal void Reorder(List<PointerStyle> order)
    {
        pinMessage = null;
        Preferences.PinnedStyles = order;
        ShowPage();
    }

    internal void Swap(PointerStyle old, PointerStyle next)
    {
        var pinned = Preferences.PinnedStyles.ToList();
        int i = pinned.IndexOf(old);
        if (i < 0 || pinned.Contains(next))
        {
            return;
        }
        pinned[i] = next;
        pinMessage = null;
        Preferences.PinnedStyles = pinned;
        ShowPage();
    }

    internal void SetPinMessage(string? message)
    {
        pinMessage = message;
        ShowPage();
    }

    /// <summary>The popup grid of every pointer, opened from the + slot.</summary>
    internal Popup PointerPicker(UIElement anchor)
    {
        var pinned = Preferences.PinnedStyles.ToHashSet();
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            VerticalOffset = 6,
        };
        var content = new StackPanel();
        void AddSection(string? title, IEnumerable<PointerStyle> styles)
        {
            if (title != null)
            {
                var t = Ui.Text(title, 12, Ui.TextSecondary, FontWeights.SemiBold);
                t.Margin = new Thickness(6, 8, 0, 4);
                content.Children.Add(t);
            }
            var wrap = new WrapPanel();
            foreach (var style in styles)
            {
                bool added = pinned.Contains(style);
                var cell = new Border
                {
                    Width = 62,
                    Height = 58,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(8),
                    Background = Brushes.Transparent,
                    Child = Ui.Picture(Icons.CachedFor(style) ?? Icons.For(style), 42),
                    Opacity = added ? 0.35 : 1,
                    ToolTip = added ? $"{style.Title} is already in the toolbar" : style.Title,
                    Cursor = added ? Cursors.Arrow : Cursors.Hand,
                };
                if (!added)
                {
                    cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, Ui.SubtleHover);
                    cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
                    cell.MouseLeftButtonUp += (_, _) =>
                    {
                        popup.IsOpen = false;
                        Pin(style);
                    };
                }
                wrap.Children.Add(cell);
            }
            content.Children.Add(wrap);
        }
        AddSection(null, PointerStyle.BuiltInHands.Append(PointerStyle.Laser));
        if (PointerStyle.CustomHandStyles.Any())
        {
            AddSection("Your hands", PointerStyle.CustomHandStyles);
        }
        var make = Ui.LinkButton("Make your own hand…", () =>
        {
            popup.IsOpen = false;
            StartMakingHand();
        }, 12);
        make.Margin = new Thickness(8, 10, 0, 4);

        var scroll = new ScrollViewer { Content = content, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var outer = new StackPanel();
        outer.Children.Add(scroll);
        outer.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 0) }.Res(Border.BackgroundProperty, Ui.Divider));
        outer.Children.Add(make);
        popup.Child = new Border
        {
            Width = 420,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = outer,
            Margin = new Thickness(0, 0, 12, 12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.25 },
        }.Res(Border.BackgroundProperty, Ui.SolidBackground).Res(Border.BorderBrushProperty, Ui.CardStroke);
        return popup;
    }

    private UIElement YourHands()
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        var make = Ui.LinkButton("Make your own…", StartMakingHand, 12);
        DockPanel.SetDock(make, Dock.Right);
        header.Children.Add(make);
        header.Children.Add(Ui.Text("Your hands", 14, weight: FontWeights.SemiBold));
        var stack = new StackPanel();
        stack.Children.Add(header);

        var custom = PointerStyle.CustomHandStyles.ToList();
        if (custom.Count == 0)
        {
            var t = Ui.Text("Turn any idea into a hand with ChatGPT or another image tool.", 12, Ui.TextSecondary);
            t.Margin = new Thickness(2, 0, 0, 0);
            stack.Children.Add(t);
        }
        else
        {
            var wrap = new WrapPanel();
            foreach (var style in custom)
            {
                wrap.Children.Add(CustomHandChip(style));
            }
            stack.Children.Add(wrap);
        }
        if (handMessage != null)
        {
            var m = Message(handMessage, error: false);
            m.Margin = new Thickness(2, 6, 0, 0);
            stack.Children.Add(m);
        }
        return stack;
    }

    private UIElement CustomHandChip(PointerStyle style)
    {
        var pinned = Preferences.PinnedStyles.Contains(style);
        bool full = Preferences.PinnedStyles.Count >= Preferences.MaxPinned;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Picture(Icons.CachedFor(style) ?? Icons.For(style), 24));
        var name = Ui.Text(style.Title, 13);
        name.Margin = new Thickness(6, 0, 6, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(name);
        var remove = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Child = Icons.Glyph(Icons.Close, 8, null),
            Opacity = 0,
            ToolTip = $"Delete {style.Title}",
            Cursor = Cursors.Hand,
        };
        ((TextBlock)remove.Child).SetResourceReference(TextBlock.ForegroundProperty, Ui.TextSecondary);
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            RemoveHand(style);
        };
        row.Children.Add(remove);
        var chip = new Border
        {
            Child = row,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Cursor = pinned ? Cursors.Arrow : Cursors.Hand,
            ToolTip = pinned ? $"{style.Title} is in your toolbar" : $"Click to add {style.Title} to the toolbar",
        }.Res(Border.BackgroundProperty, Ui.CardBackground).Res(Border.BorderBrushProperty, Ui.CardStroke);
        chip.MouseEnter += (_, _) => remove.Opacity = 1;
        chip.MouseLeave += (_, _) => remove.Opacity = 0;
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (pinned)
            {
                return;
            }
            if (full)
            {
                SwapPopup(chip, style).IsOpen = true;
            }
            else
            {
                Pin(style);
            }
        };
        return chip;
    }

    private Popup SwapPopup(UIElement anchor, PointerStyle style)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 6 };
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text($"The toolbar is full. Swap {style.Title} in for:", 12, Ui.TextSecondary));
        var cells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var old in Preferences.PinnedStyles)
        {
            var cell = new Border
            {
                Width = 44,
                Height = 44,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = Ui.Picture(Icons.CachedFor(old) ?? Icons.For(old), 32),
                ToolTip = $"Replace {old.Title}",
                Cursor = Cursors.Hand,
            }.Res(Border.BackgroundProperty, Ui.SubtleHover);
            cell.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                Swap(old, style);
            };
            cells.Children.Add(cell);
        }
        stack.Children.Add(cells);
        popup.Child = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = stack,
            Margin = new Thickness(0, 0, 12, 12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.25 },
        }.Res(Border.BackgroundProperty, Ui.SolidBackground).Res(Border.BorderBrushProperty, Ui.CardStroke);
        return popup;
    }

    private void RemoveHand(PointerStyle style)
    {
        var pinned = Preferences.PinnedStyles.Where(s => s != style).ToList();
        Preferences.PinnedStyles = pinned.Count == 0 ? PointerStyle.DefaultPinned : pinned;
        if (PointerStyle.Saved == style)
        {
            PointerStyle.Saved = PointerStyle.Lavender;
        }
        CustomHands.Remove(style);
        handMessage = null;
        ShowPage();
    }

    // ---- Make your own hand ------------------------------------------------------------------

    private void StartMakingHand()
    {
        draft = null;
        draftError = null;
        promptCopied = false;
        handMessage = null;
        makingHand = true;
        section = SettingsSection.Pointers;
        BuildSidebar();
        ShowPage();
        scroller.ScrollToTop();
    }

    private UIElement MakeHandPage()
    {
        var idea = new TextBox { Text = handIdea, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 360, Padding = new Thickness(8, 5, 8, 5) };
        idea.TextChanged += (_, _) => handIdea = idea.Text;
        SetPlaceholder(idea, "e.g. knitted wool in mustard yellow");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Button("Open in ChatGPT", OpenChatGpt, accent: true, glyph: Icons.OpenExternal));
        var copy = Ui.Button(promptCopied ? "Copied" : "Copy prompt", CopyPrompt, glyph: promptCopied ? Icons.Check : Icons.Copy);
        copy.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(copy);

        return Column(28,
            Step(1, "Describe it", "What should your hand look like?", idea),
            Step(2, "Make the image",
                "Open it in ChatGPT, or copy the prompt for another image tool. Download the result as a PNG with a transparent background.",
                buttons),
            Step(3, "Add it", null, draft == null ? DropZone() : DraftPreview(draft)));
    }

    private static void SetPlaceholder(TextBox box, string placeholder)
    {
        // The Fluent theme shows this as the placeholder text.
        box.SetValue(FrameworkElement.TagProperty, placeholder);
        box.ToolTip = placeholder;
        if (box.Text.Length == 0)
        {
            var hint = new VisualBrush(Ui.Text(placeholder, 13, Ui.TextTertiary))
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                Transform = new TranslateTransform(10, 0),
            };
            void Update() => box.Background = box.Text.Length == 0 ? hint : null;
            box.TextChanged += (_, _) => Update();
            box.Loaded += (_, _) => Update();
        }
    }

    private static UIElement Step(int number, string title, string? detail, UIElement content)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = number.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }.Res(TextBlock.ForegroundProperty, Ui.OnAccent),
        }.Res(Border.BackgroundProperty, Ui.Accent);
        grid.Children.Add(badge);
        var right = new StackPanel();
        right.Children.Add(Ui.Text(title, 14, weight: FontWeights.SemiBold));
        if (detail != null)
        {
            var d = Ui.Text(detail, 13, Ui.TextSecondary);
            d.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(d);
        }
        if (content is FrameworkElement fe)
        {
            fe.Margin = new Thickness(0, 10, 0, 0);
        }
        right.Children.Add(content);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement DropZone()
    {
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var glyph = Icons.Glyph(draftError == null ? Icons.Download : Icons.Warning, 22);
        glyph.SetResourceReference(TextBlock.ForegroundProperty, draftError == null ? Ui.TextSecondary : Ui.Caution);
        inner.Children.Add(glyph);
        var text = Ui.Text(draftError ?? "Drop the PNG here, or click to choose it", 13, draftError == null ? Ui.TextSecondary : Ui.TextPrimary);
        text.TextAlignment = TextAlignment.Center;
        text.Margin = new Thickness(24, 8, 24, 0);
        inner.Children.Add(text);
        if (draftError != null)
        {
            var again = Ui.Text("Try again with another image", 12, Ui.TextSecondary);
            again.HorizontalAlignment = HorizontalAlignment.Center;
            again.Margin = new Thickness(0, 4, 0, 0);
            inner.Children.Add(again);
        }
        var dash = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 5, 4 },
        };
        dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Ui.ControlStroke);
        var zone = new Grid { Height = 140, AllowDrop = true, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        zone.Children.Add(dash);
        zone.Children.Add(inner);
        zone.MouseLeftButtonUp += (_, _) => ChooseHandFile();
        zone.DragEnter += (_, e) =>
        {
            dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Ui.Accent);
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        zone.DragLeave += (_, _) => dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Ui.ControlStroke);
        zone.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                LoadDraft(files[0]);
            }
        };
        return zone;
    }

    private UIElement DraftPreview(HandMaker.Result d)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var crop = new CroppedBitmap(d.Image, new Int32Rect(112, 90, 800, 1000));
        var image = new Image { Source = crop, Width = 144, Height = 180 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        grid.Children.Add(new Border
        {
            Child = image,
            Padding = new Thickness(13, 10, 13, 10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xCF, 0xB5)),
            VerticalAlignment = VerticalAlignment.Top,
        });

        var right = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Ui.Text("Name", 12, Ui.TextSecondary, FontWeights.Medium));
        var name = new TextBox { Text = handName, Width = 220, MaxLength = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        name.TextChanged += (_, _) => handName = name.Text;
        right.Children.Add(name);
        var note = Ui.Text("It should show the back of the hand, like the ones in TinyHand. If you see the palm, try another image.", 12, Ui.TextSecondary);
        note.MaxWidth = 320;
        note.HorizontalAlignment = HorizontalAlignment.Left;
        note.Margin = new Thickness(0, 12, 0, 12);
        right.Children.Add(note);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Ui.Button("Add hand", SaveDraft, accent: true));
        var other = Ui.LinkButton("Use another image", ChooseHandFile, 13);
        other.Margin = new Thickness(14, 0, 0, 0);
        other.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(other);
        right.Children.Add(actions);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private void OpenChatGpt() => Ui.OpenUrl("https://chatgpt.com/?q=" + Uri.EscapeDataString(HandMaker.Prompt(handIdea)));

    private void CopyPrompt()
    {
        try
        {
            Clipboard.SetText(HandMaker.Prompt(handIdea));
        }
        catch (Exception e)
        {
            Log.Write($"clipboard failed: {e.Message}");
            return;
        }
        promptCopied = true;
        ShowPage();
        var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        reset.Tick += (_, _) =>
        {
            reset.Stop();
            promptCopied = false;
            if (makingHand)
            {
                ShowPage();
            }
        };
        reset.Start();
    }

    private void ChooseHandFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pick the PNG of your hand",
            Filter = "Images (*.png;*.webp)|*.png;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            LoadDraft(dialog.FileName);
        }
    }

    private void LoadDraft(string path)
    {
        try
        {
            draft = HandMaker.Make(path);
            draftError = null;
            handName = CustomHands.SuggestedName(path);
        }
        catch (HandMaker.Problem p)
        {
            draft = null;
            draftError = p.Message;
        }
        catch (Exception e)
        {
            Log.Write($"hand draft failed: {e}");
            draft = null;
            draftError = "TinyHand couldn't read that file.";
        }
        ShowPage();
    }

    private void SaveDraft()
    {
        if (draft == null)
        {
            return;
        }
        try
        {
            var style = CustomHands.Add(draft.Png, draft.Ink, handName);
            if (style.AssetName is { } name)
            {
                HandAsset.Thumbnail(name);
            }
            var pinned = Preferences.PinnedStyles.ToList();
            if (pinned.Count < Preferences.MaxPinned)
            {
                pinned.Insert(Math.Max(0, pinned.Count - (pinned.LastOrDefault()?.IsLaser == true ? 1 : 0)), style);
                Preferences.PinnedStyles = pinned;
                handMessage = $"{style.Title} is in your toolbar.";
            }
            else
            {
                handMessage = $"{style.Title} is ready. Click it to add it to the toolbar.";
            }
            PointerStyle.Saved = style;
            draft = null;
            handIdea = "";
            makingHand = false;
            ShowPage();
        }
        catch (Exception e)
        {
            Log.Write($"save hand failed: {e}");
            draftError = "TinyHand couldn't save that hand.";
            ShowPage();
        }
    }

    // ---- Appearance --------------------------------------------------------------------------

    private UIElement AppearancePage()
    {
        var slider = new Slider
        {
            Minimum = Preferences.PointerSizeMin,
            Maximum = Preferences.PointerSizeMax,
            Value = Preferences.PointerSize,
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        slider.ValueChanged += (_, e) => Preferences.PointerSize = e.NewValue;
        var size = new StackPanel { Orientation = Orientation.Horizontal };
        var small = Icons.Glyph(Icons.Hand, 10);
        small.SetResourceReference(TextBlock.ForegroundProperty, Ui.TextSecondary);
        var big = Icons.Glyph(Icons.Hand, 18);
        big.SetResourceReference(TextBlock.ForegroundProperty, Ui.TextSecondary);
        size.Children.Add(small);
        size.Children.Add(slider);
        size.Children.Add(big);

        var shadow = new ToggleSwitch(Preferences.ShowShadow);
        shadow.Toggled += on => Preferences.ShowShadow = on;
        return Ui.Card("Pointer",
            Ui.Row("Size", null, size),
            Ui.Row("Shadow under the pointer", null, shadow));
    }

    // ---- Drawing -----------------------------------------------------------------------------

    private UIElement DrawingPage()
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var choice in Enum.GetValues<InkChoice>())
        {
            bool selected = Preferences.InkChoice == choice;
            var ring = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = choice.Title(),
                Child = Ui.Picture(choice.Color() is { } c ? Icons.InkDot(c.ToMedia()) : Icons.RainbowDot(), 32),
            };
            ((Image)ring.Child).Margin = new Thickness(-4);
            if (selected)
            {
                ring.SetResourceReference(Border.BorderBrushProperty, Ui.Accent);
            }
            var target = choice;
            ring.MouseLeftButtonUp += (_, _) =>
            {
                Preferences.InkChoice = target;
                ShowPage();
            };
            swatches.Children.Add(ring);
        }
        var fade = Ui.Combo(new[]
        {
            ("After 3 seconds", 3.0), ("After 6 seconds", 6.0), ("After 10 seconds", 10.0), ("Keep until cleared", 0.0),
        }, Preferences.FadeSeconds, v => Preferences.FadeSeconds = v);
        var inkButton = new ToggleSwitch(Preferences.ShowInkButton);
        inkButton.Toggled += on => Preferences.ShowInkButton = on;

        return Ui.Card("Ink",
            Ui.Row("Ink colour",
                Preferences.InkChoice == InkChoice.Match ? "Matches each pointer." : $"All pointers draw in {Preferences.InkChoice.Title().ToLowerInvariant()}.",
                swatches),
            Ui.Row("Lines fade", $"Clear them any time with {Preferences.ClearShortcut.Display}.", fade),
            Ui.Row("Ink button in the toolbar", "Change the colour while you present.", inkButton));
    }

    // ---- Sound -------------------------------------------------------------------------------

    private UIElement SoundPage()
    {
        preview ??= new TapSound(Preferences.TapSound);
        preview.Warm();
        bool on = Preferences.HandSound;
        var grid = new UniformGrid { Columns = 4 };
        grid.Children.Add(SoundTile("Off", Icons.Mute, !on, () =>
        {
            Preferences.HandSound = false;
            ShowPage();
        }));
        foreach (var choice in Enum.GetValues<TapSoundChoice>())
        {
            var c = choice;
            grid.Children.Add(SoundTile(c.Title(), Icons.Wave, on && Preferences.TapSound == c, () =>
            {
                Preferences.TapSound = c;
                Preferences.HandSound = true;
                preview.Choice = c;
                preview.Enabled = true;
                preview.Play();
                ShowPage();
            }));
        }
        var hint = Ui.Text("Click a sound to hear it. It plays each time the hand taps.", 12, Ui.TextSecondary);
        hint.Margin = new Thickness(2, 8, 0, 0);
        var top = new StackPanel();
        top.Children.Add(Ui.SectionHeader("Tap sound"));
        top.Children.Add(grid);
        top.Children.Add(hint);

        var laser = new ToggleSwitch(Preferences.LaserSound) { IsEnabled = on };
        laser.Toggled += v => Preferences.LaserSound = v;
        var card = Ui.Card(null, Ui.Row("Laser dot taps too", "Off by default, for quiet pointing.", laser));
        card.Opacity = on ? 1 : 0.5;
        return Column(24, top, card);
    }

    private static UIElement SoundTile(string title, string glyph, bool selected, Action action)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var g = Icons.Glyph(glyph, 20);
        g.SetResourceReference(TextBlock.ForegroundProperty, selected ? Ui.Accent : Ui.TextSecondary);
        stack.Children.Add(g);
        var t = Ui.Text(title, 13, selected ? Ui.TextPrimary : Ui.TextSecondary, selected ? FontWeights.SemiBold : FontWeights.Normal);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        var tile = new Border
        {
            Child = stack,
            Padding = new Thickness(0, 14, 0, 14),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Cursor = Cursors.Hand,
        }.Res(Border.BackgroundProperty, Ui.CardBackground).Res(Border.BorderBrushProperty, selected ? Ui.Accent : Ui.CardStroke);
        tile.MouseLeftButtonUp += (_, _) => action();
        return tile;
    }

    // ---- About -------------------------------------------------------------------------------

    private UIElement AboutPage()
    {
        var header = new DockPanel();
        if (Updater.IsAvailable)
        {
            var check = Ui.Button("Check for updates", () => Updater.CheckNow(interactive: true));
            check.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(check, Dock.Right);
            header.Children.Add(check);
        }
        var icon = Ui.Picture(Icons.AppIcon, 72);
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        var info = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Ui.Text("TinyHand", 20, weight: FontWeights.Bold));
        info.Children.Add(Ui.Text($"Version {AppController.Version}", 13, Ui.TextSecondary));
        info.Children.Add(Ui.Text("A tiny hand for your PC. Free and open source.", 13, Ui.TextSecondary));
        header.Children.Add(info);

        var reset = Ui.LinkButton("Reset all settings…", ResetAll, 12);
        reset.HorizontalAlignment = HorizontalAlignment.Right;

        return Column(24, header,
            Ui.Card(null,
                LinkRow("Source code", "github.com/OmiChrome/tinyhand-windows", Updater.RepoUrl),
                LinkRow("Original macOS app", "tinyhand.app", "https://tinyhand.app"),
                LinkRow("Original source", "github.com/nilbuild/tinyhand", "https://github.com/nilbuild/tinyhand"),
                LinkRow("License", "MIT", Updater.RepoUrl + "/blob/main/LICENSE")),
            Ui.Text($"Settings and logs: {Paths.RoamingData}", 11, Ui.TextTertiary),
            reset);
    }

    private static UIElement LinkRow(string title, string detail, string url)
    {
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Ui.Text(detail, 13, Ui.TextSecondary));
        var arrow = Icons.Glyph(Icons.OpenExternal, 11);
        arrow.Margin = new Thickness(8, 0, 0, 0);
        arrow.SetResourceReference(TextBlock.ForegroundProperty, Ui.TextSecondary);
        right.Children.Add(arrow);
        var row = Ui.Row(title, null, right);
        var hit = new Border { Child = row, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        hit.MouseEnter += (_, _) => hit.SetResourceReference(Border.BackgroundProperty, Ui.SubtleHover);
        hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
        hit.MouseLeftButtonUp += (_, _) => Ui.OpenUrl(url);
        return hit;
    }

    private void ResetAll()
    {
        var answer = MessageBox.Show(this,
            "Shortcuts, toolbar pointers, size, shadow, drawing and sound go back to how they were at first. Open at sign-in stays as it is.",
            "Reset all settings?", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }
        Preferences.ResetAll();
        ApplyToggleShortcut?.Invoke(Shortcut.ToggleDefault);
        ApplyClearShortcut?.Invoke(Shortcut.ClearDefault);
        shortcutError = null;
        pinMessage = null;
        ShowPage();
    }
}
