using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TinyHand.Native;

namespace TinyHand.UI;

/// <summary>First-run card. Windows needs no special permission, so it just says how to start.</summary>
internal sealed class WelcomeWindow : Window
{
    public event Action? TryIt;

    private readonly TextBlock subtitle;

    public WelcomeWindow()
    {
        Title = "Welcome to TinyHand";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Icons.AppIcon;
        FontFamily = Ui.Font;

        var stack = new StackPanel { Margin = new Thickness(32, 28, 32, 28), HorizontalAlignment = HorizontalAlignment.Center };
        var icon = Ui.Picture(Icons.AppIcon, 80);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var title = Ui.Text("Say hi to TinyHand", 20, weight: FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 16, 0, 6);
        stack.Children.Add(title);
        subtitle = Ui.Text("", 13, Ui.TextSecondary);
        subtitle.TextAlignment = TextAlignment.Center;
        stack.Children.Add(subtitle);
        var start = Ui.Button("Start", () =>
        {
            Close();
            TryIt?.Invoke();
        }, accent: true);
        start.IsDefault = true;
        start.Width = 180;
        start.Margin = new Thickness(0, 22, 0, 10);
        start.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(start);
        var tip = Ui.Text("TinyHand lives in the notification area of the taskbar. Right-click it for settings.", 11, Ui.TextTertiary);
        tip.TextAlignment = TextAlignment.Center;
        tip.MaxWidth = 280;
        stack.Children.Add(tip);
        Content = stack;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void ShowAndActivate()
    {
        Preferences.WelcomeShown = true;
        subtitle.Text = $"Click the tray icon or press {Preferences.ToggleShortcut.Display} to turn the pointer on. Clicks become taps, drags draw, and Esc puts it away.";
        if (!IsVisible)
        {
            Show();
        }
        Activate();
        Win32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }
}
