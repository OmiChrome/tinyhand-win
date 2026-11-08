using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TinyHand.UI;

/// <summary>
/// The "In your toolbar" row in settings: up to eight slots. Drag to reorder, drag out of the row
/// to remove, hover for an ✕, or press + to add.
/// </summary>
internal sealed class PinnedRow : Border
{
    private const double SlotHeight = 58;

    private readonly SettingsWindow owner;
    private readonly bool full;
    private readonly UniformGrid grid = new() { Rows = 1, Columns = Preferences.MaxPinned };
    private readonly List<PointerStyle> baseOrder;
    private List<PointerStyle> order;

    private PointerStyle? dragging;
    private Point dragStart;
    private bool dragMoved;
    private bool removing;

    public PinnedRow(List<PointerStyle> pinned, bool full, SettingsWindow owner)
    {
        this.owner = owner;
        this.full = full;
        baseOrder = pinned;
        order = pinned.ToList();
        Padding = new Thickness(10);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Child = grid;
        Background = Brushes.Transparent;
        this.Res(BackgroundProperty, Ui.CardBackground).Res(BorderBrushProperty, Ui.CardStroke);
        Build();

        PreviewMouseMove += OnMove;
        PreviewMouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) =>
        {
            if (dragging != null)
            {
                dragging = null;
                order = baseOrder.ToList();
                Build();
            }
        };
    }

    private void Build()
    {
        grid.Children.Clear();
        foreach (var style in order)
        {
            grid.Children.Add(Slot(style));
        }
        int used = order.Count;
        if (!full && used < Preferences.MaxPinned)
        {
            grid.Children.Add(AddSlot());
            used++;
        }
        for (int i = used; i < Preferences.MaxPinned; i++)
        {
            grid.Children.Add(new Border { Height = SlotHeight, Margin = new Thickness(3), CornerRadius = new CornerRadius(8) }
                .Res(BackgroundProperty, Ui.SubtleHover));
        }
    }

    private FrameworkElement Slot(PointerStyle style)
    {
        bool lifted = dragging == style;
        var cell = new Grid { Height = SlotHeight, Margin = new Thickness(3), Background = Brushes.Transparent, ToolTip = style.Title };
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = Ui.Picture(Icons.CachedFor(style) ?? Icons.For(style), 40),
            Effect = new DropShadowEffect { BlurRadius = lifted ? 12 : 3, ShadowDepth = lifted ? 4 : 1, Opacity = lifted ? 0.25 : 0.08, Direction = 270 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(lifted ? 1.06 : 1, lifted ? 1.06 : 1),
            Opacity = lifted && removing ? 0.45 : 1,
        }.Res(BackgroundProperty, Ui.SolidBackground).Res(BorderBrushProperty, Ui.CardStroke);
        cell.Children.Add(card);
        if (lifted && removing)
        {
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0),
                Child = new TextBlock { Text = "−", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
            }.Res(BackgroundProperty, Ui.Critical);
            cell.Children.Add(badge);
        }

        var remove = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, -5, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
            Child = Icons.Glyph(Icons.Close, 8),
            Visibility = Visibility.Collapsed,
            ToolTip = "Remove from the toolbar",
            Cursor = Cursors.Hand,
        };
        remove.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            owner.Unpin(style);
        };
        cell.Children.Add(remove);
        cell.MouseEnter += (_, _) => remove.Visibility = dragging == null ? Visibility.Visible : Visibility.Collapsed;
        cell.MouseLeave += (_, _) => remove.Visibility = Visibility.Collapsed;
        cell.MouseLeftButtonDown += (_, e) =>
        {
            dragging = style;
            dragStart = e.GetPosition(this);
            dragMoved = false;
            removing = false;
            CaptureMouse();
            e.Handled = true;
        };
        return cell;
    }

    private FrameworkElement AddSlot()
    {
        var plus = Icons.Glyph(Icons.Add, 15);
        plus.SetResourceReference(TextBlock.ForegroundProperty, Ui.TextSecondary);
        var dash = new System.Windows.Shapes.Rectangle { RadiusX = 8, RadiusY = 8, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
        dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Ui.ControlStroke);
        var cell = new Grid { Height = SlotHeight, Margin = new Thickness(3), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Add a pointer" };
        cell.Children.Add(dash);
        cell.Children.Add(plus);
        cell.MouseEnter += (_, _) => cell.SetResourceReference(Panel.BackgroundProperty, Ui.SubtleHover);
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        cell.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            owner.PointerPicker(cell).IsOpen = true;
        };
        return cell;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (dragging is not { } style || !IsMouseCaptured)
        {
            return;
        }
        var p = e.GetPosition(this);
        if (!dragMoved && (p - dragStart).Length < 3)
        {
            return;
        }
        dragMoved = true;
        bool over = p.X >= -16 && p.X <= ActualWidth + 16 && p.Y >= -28 && p.Y <= ActualHeight + 28;
        if (over)
        {
            removing = false;
            double step = (ActualWidth - Padding.Left - Padding.Right) / Preferences.MaxPinned;
            var rest = baseOrder.Where(s => s != style).ToList();
            int slot = (int)Math.Clamp((p.X - Padding.Left) / step, 0, rest.Count);
            rest.Insert(slot, style);
            order = rest;
        }
        else
        {
            removing = baseOrder.Count > 1;
            order = baseOrder.ToList();
        }
        Build();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (dragging is not { } style)
        {
            return;
        }
        bool wasMoved = dragMoved;
        bool wasRemoving = removing;
        var final = order.ToList();
        dragging = null;
        removing = false;
        ReleaseMouseCapture();
        if (!wasMoved)
        {
            order = baseOrder.ToList();
            Build();
            return;
        }
        var p = e.GetPosition(this);
        bool over = p.X >= -16 && p.X <= ActualWidth + 16 && p.Y >= -28 && p.Y <= ActualHeight + 28;
        if (!over)
        {
            if (wasRemoving)
            {
                owner.Unpin(style);
            }
            else
            {
                owner.SetPinMessage("Keep at least one pointer in the toolbar.");
            }
            return;
        }
        if (!final.SequenceEqual(baseOrder))
        {
            owner.Reorder(final);
        }
        else
        {
            order = baseOrder.ToList();
            Build();
        }
    }
}
