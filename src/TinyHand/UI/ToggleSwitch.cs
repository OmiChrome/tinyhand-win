using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TinyHand.UI;

/// <summary>A Windows 11 style on/off switch.</summary>
internal sealed class ToggleSwitch : Grid
{
    private readonly Border track;
    private readonly Ellipse knob;
    private readonly TranslateTransform slide = new();
    private readonly TextBlock label;
    private bool isOn;

    public event Action<bool>? Toggled;

    public ToggleSwitch(bool on)
    {
        isOn = on;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        label = Ui.Text("", 14);
        label.Margin = new Thickness(0, 0, 12, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MinWidth = 24;
        label.TextAlignment = TextAlignment.Right;
        Children.Add(label);

        track = new Border { Width = 40, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        knob = new Ellipse
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3, 0, 0, 0),
            RenderTransform = slide,
        };
        track.Child = knob;
        SetColumn(track, 1);
        Children.Add(track);

        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            IsOn = !IsOn;
            Toggled?.Invoke(IsOn);
        };
        Refresh(animate: false);
    }

    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn == value)
            {
                return;
            }
            isOn = value;
            Refresh(animate: true);
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty)
        {
            Opacity = IsEnabled ? 1 : 0.45;
            Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
        }
    }

    private void Refresh(bool animate)
    {
        label.Text = isOn ? "On" : "Off";
        if (isOn)
        {
            track.SetResourceReference(Border.BackgroundProperty, Ui.Accent);
            track.SetResourceReference(Border.BorderBrushProperty, Ui.Accent);
            knob.SetResourceReference(Shape.FillProperty, Ui.OnAccent);
        }
        else
        {
            track.Background = Brushes.Transparent;
            track.SetResourceReference(Border.BorderBrushProperty, Ui.TextSecondary);
            knob.SetResourceReference(Shape.FillProperty, Ui.TextSecondary);
        }
        double x = isOn ? 20 : 0;
        if (animate)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = x;
        }
    }
}
